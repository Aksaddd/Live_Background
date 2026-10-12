"""Build the Lively wallpaper package and the single-file browser preview.

Usage: python tools/build.py
Inputs:  assets/forest.jpg (left monitor, 2560x1440), src/*
Outputs: dist/lively/, dist/MistyRidge-lively.zip, dist/preview.html
"""
import base64
import json
import pathlib
import shutil
import subprocess
import sys
import zipfile

from PIL import Image, ImageOps

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC, ASSETS, DIST = ROOT / "src", ROOT / "assets", ROOT / "dist"


def data_uri(path, mime):
    return f"data:{mime};base64," + base64.b64encode(path.read_bytes()).decode()


def main():
    image, trans = ASSETS / "forest.jpg", ASSETS / "transmission.png"
    out = subprocess.run([sys.executable, str(ROOT / "tools" / "haze_map.py"), str(image), str(trans)],
                         check=True, capture_output=True, text=True).stdout
    airlight = json.loads(out)["airlight"]
    sway = ASSETS / "sway.png"
    subprocess.run([sys.executable, str(ROOT / "tools" / "sway_map.py"), str(image),
                    ",".join(str(a) for a in airlight), str(sway)], check=True, capture_output=True)

    assets_js = "window.MIST_ASSETS = " + json.dumps({
        "image": data_uri(image, "image/jpeg"),
        "transmission": data_uri(trans, "image/png"),
        "sway": data_uri(sway, "image/png"),
        "airlight": airlight,
    }) + ";\n"

    lively = DIST / "lively"
    shutil.rmtree(lively, ignore_errors=True)  # only our own outputs; package_app.py shares dist/
    lively.mkdir(parents=True)
    (lively / "assets.js").write_text(assets_js)
    shutil.copy(SRC / "mist.js", lively / "mist.js")
    shutil.copy(SRC / "wallpaper.html", lively / "index.html")
    shutil.copy(SRC / "LivelyInfo.json", lively / "LivelyInfo.json")
    shutil.copy(SRC / "LivelyProperties.json", lively / "LivelyProperties.json")

    left = Image.open(image).convert("RGB")
    pano = Image.new("RGB", (left.width * 2, left.height))
    pano.paste(left, (0, 0))
    pano.paste(ImageOps.mirror(left), (left.width, 0))
    pano.resize((640, 180), Image.LANCZOS).save(lively / "thumbnail.jpg", quality=88)

    with zipfile.ZipFile(DIST / "MistyRidge-lively.zip", "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(lively.iterdir()):
            z.write(f, f.name)

    preview = (SRC / "preview.html").read_text()
    preview = preview.replace("{{ASSETS_JS}}", assets_js).replace("{{MIST_JS}}", (SRC / "mist.js").read_text())
    (DIST / "preview.html").write_text(preview)

    for f in sorted(DIST.rglob("*")):
        if f.is_file():
            print(f"{f.relative_to(ROOT)}  {f.stat().st_size / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
