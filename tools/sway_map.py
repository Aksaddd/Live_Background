"""Build a sway map: how far each pixel may bend in the wind (0 = fixed, 1 = treetop).

Trees are the pixels clearly darker than the fog. Within a tree, the bend grows toward the
top of the crown (distance up to the first fog pixel above), so trunks stay put and tips move.
The map is dilated and blurred slightly so the fog right around each tip moves with it;
otherwise trees would look thinner when they lean.

Usage: python sway_map.py <input.jpg> <airlight r,g,b> <out_sway.png>
"""
import sys

import numpy as np
from PIL import Image


def box(a, r):
    p = np.pad(a, ((r + 1, r), (r + 1, r)), mode="edge")
    c = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def max_filter(a, r):
    out = a.copy()
    for d in range(1, r + 1):
        out[:, d:] = np.maximum(out[:, d:], a[:, :-d]); out[:, :-d] = np.maximum(out[:, :-d], a[:, d:])
    b = out.copy()
    for d in range(1, r + 1):
        out[d:, :] = np.maximum(out[d:, :], b[:-d, :]); out[:-d, :] = np.maximum(out[:-d, :], b[d:, :])
    return out


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def main(src, air, dst):
    img = np.asarray(Image.open(src).convert("RGB"), dtype=np.float64) / 255.0
    A = np.array([float(v) for v in air.split(",")])
    lum = img @ np.array([0.2126, 0.7152, 0.0722])
    a_lum = float(A @ np.array([0.2126, 0.7152, 0.0722]))
    darkness = np.clip((a_lum - box(lum, 1)) / a_lum, 0, 1)  # 0 = fog, 1 = black

    tree = darkness > 0.42
    h, w = tree.shape
    # Rows since the last fog pixel above, per column.
    depth = np.zeros((h, w))
    crown = 0.11 * h  # ~160 px: length of crown that visibly bends
    run = np.where(tree[0], crown, 0.0)  # trees cut off by the top edge have no visible tip
    for y in range(h):
        run = np.where(tree[y], run + 1, 0)
        depth[y] = run
    tip = np.clip(1 - depth / crown, 0, 1) ** 1.3

    strength = smoothstep(0.4, 0.75, darkness)  # near, high-contrast trees sway most
    sway = np.where(tree, tip * strength, 0.0)
    sway = box(max_filter(sway, 4), 3)
    # Extra horizontal smoothing: neighbouring columns must bend together or the image tears.
    p = np.pad(sway, ((0, 0), (9, 8)), mode="edge").cumsum(1)
    sway = (p[:, 17:] - p[:, :-17]) / 17

    Image.fromarray((np.clip(sway, 0, 1) * 255 + 0.5).astype(np.uint8), "L").save(dst, optimize=True)
    print(f"sway: mean={sway.mean():.3f} covered={(sway > 0.05).mean():.2%}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
