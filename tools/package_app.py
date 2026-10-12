"""Build the Live Background Windows app and bundle it with the Misty Ridge wallpaper.

Usage: python tools/package_app.py   (needs the .NET 8 SDK; builds fine on Linux)
Output: dist/LiveBackground-win-x64.zip
"""
import pathlib
import shutil
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"


def main():
    subprocess.run([sys.executable, str(ROOT / "tools" / "build.py")], check=True, stdout=subprocess.DEVNULL)
    subprocess.run(["dotnet", "build", str(ROOT / "app" / "LiveBackground.csproj"), "-c", "Release", "-nologo", "-v", "q"],
                   check=True)

    bin_dir = ROOT / "app" / "bin" / "Release" / "net48"
    stage = DIST / "LiveBackground"
    shutil.rmtree(stage, ignore_errors=True)
    stage.mkdir(parents=True)
    for name in ["LiveBackground.exe", "LiveBackground.exe.config", "Microsoft.Web.WebView2.Core.dll",
                 "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll"]:
        shutil.copy(bin_dir / name, stage / name)
    shutil.copytree(DIST / "lively", stage / "wallpapers" / "misty-ridge")

    out = DIST / "LiveBackground-win-x64.zip"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(stage.rglob("*")):
            if f.is_file():
                z.write(f, pathlib.Path("LiveBackground") / f.relative_to(stage))
    print(f"{out.relative_to(ROOT)}  {out.stat().st_size / 1e6:.2f} MB")
    for f in sorted(stage.rglob("*")):
        if f.is_file():
            print("  ", f.relative_to(stage))


if __name__ == "__main__":
    main()
