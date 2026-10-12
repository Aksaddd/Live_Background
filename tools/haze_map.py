"""Estimate atmospheric transmission (how much fog sits in front of each pixel)
using the dark-channel prior (He et al., 2009) with guided-filter refinement.

Usage: python haze_map.py <input.jpg> <out_transmission.png>
Prints the estimated airlight (fog colour) as JSON.
"""
import json
import sys

import numpy as np
from PIL import Image


def box(a, r):
    """Mean over a (2r+1)^2 window, edge-clamped, via integral images."""
    p = np.pad(a, ((r + 1, r), (r + 1, r)), mode="edge")
    c = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    s = c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]
    return s / (k * k)


def min_filter(a, r):
    p = np.pad(a, r, mode="edge")
    h, w = a.shape
    out = np.full(a.shape, np.inf, dtype=a.dtype)
    for d in range(2 * r + 1):  # separable: rows then columns
        out = np.minimum(out, p[r:r + h, d:d + w])
    p = np.pad(out, ((r, r), (0, 0)), mode="edge")
    out2 = np.full(a.shape, np.inf, dtype=a.dtype)
    for d in range(2 * r + 1):
        out2 = np.minimum(out2, p[d:d + h, :])
    return out2


def guided(I, p, r, eps):
    mI, mp = box(I, r), box(p, r)
    cov = box(I * p, r) - mI * mp
    var = box(I * I, r) - mI * mI
    a = cov / (var + eps)
    b = mp - a * mI
    return box(a, r) * I + box(b, r)


def main(src, dst):
    img = np.asarray(Image.open(src).convert("RGB"), dtype=np.float64) / 255.0
    dark = min_filter(img.min(axis=2), 7)

    # Airlight: mean colour of the brightest 0.1% of the dark channel.
    n = max(1, dark.size // 1000)
    idx = np.argsort(dark.ravel())[-n:]
    A = img.reshape(-1, 3)[idx].mean(axis=0)

    omega = 0.95
    t = 1.0 - omega * min_filter((img / A).min(axis=2), 7)
    gray = img.mean(axis=2)
    t = guided(gray, t, 40, 1e-3)
    t = np.clip(t, 0.05, 1.0)

    Image.fromarray((t * 255 + 0.5).astype(np.uint8), "L").save(dst, optimize=True)
    print(json.dumps({"airlight": [round(float(x), 4) for x in A],
                      "t_min": round(float(t.min()), 3),
                      "t_mean": round(float(t.mean()), 3)}))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
