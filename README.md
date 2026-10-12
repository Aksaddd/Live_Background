# Misty Ridge wallpaper

Animated mist for a mirrored two-monitor forest wallpaper (2 × 2560×1440, spanned), packaged for [Lively Wallpaper](https://github.com/rocksdanister/lively).

- `src/mist.js` — WebGL renderer. Re-renders the photo's fog with animated transmission (`I = J·t' + A·(1−t')`), so mist stays behind near trees.
- `tools/haze_map.py` — estimates per-pixel transmission and fog colour with the dark-channel prior.
- `tools/build.py` — builds `dist/lively/`, `dist/MistyRidge-lively.zip` and the single-file `dist/preview.html`.

Install: in Lively click **+**, drop in `dist/MistyRidge-lively.zip`, set **Settings → Wallpaper → arrangement: Span**, then pick Misty Ridge. Right-click → Customise for speed, strength, wisps and frame-rate cap.
