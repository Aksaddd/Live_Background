# Live Background

A small Windows 11 app that runs an animated web wallpaper behind the desktop icons, plus **Misty Ridge**: depth-aware drifting mist for a mirrored two-monitor forest panorama (2 × 2560×1440).

## Install (Windows 10/11)

1. Download `dist/LiveBackground-win-x64.zip` and unzip it somewhere permanent.
2. Run `LiveBackground.exe`. It is unsigned, so SmartScreen may ask: **More info → Run anyway**.
3. Double-click the tray icon (or right-click → **Adjust mist and trees…**) for sliders that change mist speed, movement, wisps, tree sway and frame rate live. The tray menu also has **Pause** and **Start with Windows**.

It needs the Microsoft Edge WebView2 Runtime and .NET Framework 4.8, both of which ship with Windows 11. Settings and a log live in `%LOCALAPPDATA%\LiveBackground` (tray → **Open log folder**).

## How it works

- **App (`app/`)**: C# / WinForms on .NET Framework 4.8 with WebView2. It asks Explorer to split the desktop (message `0x052C` to Progman) and parents its window below the icons: inside the WorkerW on the classic desktop, or as a layered child of Progman under `SHELLDLL_DefView` on the Windows 11 24H2 "raised" desktop. It re-attaches after Explorer restarts, display changes or a browser crash, and pauses rendering when every monitor is covered by maximised or fullscreen windows, or the session is locked.
- **Wallpaper (`src/`)**: WebGL. The photo's haze was measured with the dark-channel prior (`tools/haze_map.py`), so the shader can re-render fog with animated transmission (`I = J·t' + A·(1−t')`). Mist stays behind near trees, and each monitor gets independent upslope wind. Treetops sway in gusts: `tools/sway_map.py` marks how far each pixel may bend (crown tips most, trunks not at all), and the shader shifts them a few pixels with the same wind.
- Wallpapers use Lively's `livelyPropertyListener(name, value)` convention, so the same folder also works in Lively Wallpaper (`dist/MistyRidge-lively.zip`).

## Build

```
python tools/package_app.py   # needs the .NET 8 SDK and Pillow + NumPy; works on Linux
```

Outputs `dist/LiveBackground-win-x64.zip`, `dist/MistyRidge-lively.zip` and the single-file browser preview `dist/preview.html`.
