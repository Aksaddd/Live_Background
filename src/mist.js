/*
 * Misty Ridge — animated mist for a mirrored two-monitor forest wallpaper.
 *
 * How it works
 *   The photo's haze was measured offline with the dark-channel prior
 *   (tools/haze_map.py), giving per-pixel transmission t and the fog colour A.
 *   Atmospheric scattering says  I = J·t + A·(1 − t), so we can recover the
 *   "clear" scene J and re-render it with an animated transmission t' = t^k,
 *   where k comes from drifting noise. k = 1 reproduces the photo exactly;
 *   k > 1 thickens fog, k < 1 thins it. Because near trees have t ≈ 1 they are
 *   barely touched, so fog naturally stays behind them.
 *
 *   Pass 1 renders the two noise fields (far billows, near wisps) into a small
 *   offscreen texture; pass 2 composites at full resolution.
 *
 *   The panorama is the left photo plus its mirror. Each monitor gets its own
 *   independent noise and a mirrored wind that blows upslope toward the ridge
 *   at the centre, so the trees stay symmetric but the mist never does.
 *
 *   Time loops seamlessly every LOOP seconds by blending two noise samples
 *   (variance-preserving), which keeps shader coordinates small so precision
 *   never degrades no matter how long the wallpaper runs.
 */
(function () {
  "use strict";

  const LOOP = 1800; // seconds
  const HALF_ASPECT = 2560 / 1440;

  const defaults = { speed: 1.0, density: 0.85, wisps: 0.6, fps: 30, fogScale: 1 / 3 };

  const VERT = `
attribute vec2 aPos;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); }`;

  const NOISE = `
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x * 34.0) + 1.0) * x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }
// 3D simplex noise — Ashima Arts / Stefan Gustavson (MIT).
float snoise(vec3 v) {
  const vec2 C = vec2(1.0 / 6.0, 1.0 / 3.0);
  const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);
  vec3 i = floor(v + dot(v, C.yyy));
  vec3 x0 = v - i + dot(i, C.xxx);
  vec3 g = step(x0.yzx, x0.xyz);
  vec3 l = 1.0 - g;
  vec3 i1 = min(g.xyz, l.zxy);
  vec3 i2 = max(g.xyz, l.zxy);
  vec3 x1 = x0 - i1 + C.xxx;
  vec3 x2 = x0 - i2 + C.yyy;
  vec3 x3 = x0 - D.yyy;
  i = mod289(i);
  vec4 p = permute(permute(permute(
            i.z + vec4(0.0, i1.z, i2.z, 1.0))
          + i.y + vec4(0.0, i1.y, i2.y, 1.0))
          + i.x + vec4(0.0, i1.x, i2.x, 1.0));
  float n_ = 0.142857142857;
  vec3 ns = n_ * D.wyz - D.xzx;
  vec4 j = p - 49.0 * floor(p * ns.z * ns.z);
  vec4 x_ = floor(j * ns.z);
  vec4 y_ = floor(j - 7.0 * x_);
  vec4 x = x_ * ns.x + ns.yyyy;
  vec4 y = y_ * ns.x + ns.yyyy;
  vec4 h = 1.0 - abs(x) - abs(y);
  vec4 b0 = vec4(x.xy, y.xy);
  vec4 b1 = vec4(x.zw, y.zw);
  vec4 s0 = floor(b0) * 2.0 + 1.0;
  vec4 s1 = floor(b1) * 2.0 + 1.0;
  vec4 sh = -step(h, vec4(0.0));
  vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
  vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
  vec3 p0 = vec3(a0.xy, h.x);
  vec3 p1 = vec3(a0.zw, h.y);
  vec3 p2 = vec3(a1.xy, h.z);
  vec3 p3 = vec3(a1.zw, h.w);
  vec4 norm = taylorInvSqrt(vec4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
  p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
  vec4 m = max(0.6 - vec4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
  m = m * m;
  return 42.0 * dot(m * m, vec4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}`;

  // Pass 1: noise fields. R = far billows, G = near wisps, both in 0..1.
  const FOG_FRAG = `
precision highp float;
uniform vec2 uFogRes;
uniform vec2 uCoverScale;
uniform vec2 uCoverOffset;
uniform float uT;      // time within the loop, seconds
uniform float uPhase;  // uT / LOOP
${NOISE}

float fbm(vec3 p, int oct) {
  float s = 0.0, a = 0.5, n = 0.0;
  for (int i = 0; i < 5; i++) {
    if (i >= oct) break;
    s += a * snoise(p);
    n += a;
    p = p * 2.03 + vec3(17.1, 9.2, 3.7);
    a *= 0.5;
  }
  return s / n;
}

// Wind blows upslope: right and up on the left monitor, mirrored on the right.
vec2 windDir(float side) { return normalize(vec2(side < 0.5 ? 0.75 : -0.75, -0.66)); }

vec2 fields(vec2 p, vec2 w, float t, vec3 seed) {
  // Far billows: big, slow, gently domain-warped.
  vec3 pb = vec3(p * 1.1 - w * t * 0.016, t * 0.012) + seed;
  vec2 warp = vec2(snoise(pb * 0.6 + 31.0), snoise(pb * 0.6 + 71.0));
  float billow = fbm(pb + vec3(warp * 0.55, 0.0), 5);

  // Near wisps: streaked along the wind, faster, more turbulent.
  vec2 along = w, across = vec2(-w.y, w.x);
  vec2 q = vec2(dot(p, along) * 1.6, dot(p, across) * 3.6);
  vec3 pw = vec3(q - vec2(t * 0.075, 0.0), t * 0.03) + seed * 1.7;
  float wisp = fbm(pw + vec3(warp * 0.35, 0.0), 4);
  return vec2(billow, wisp);
}

void main() {
  vec2 suv = gl_FragCoord.xy / uFogRes;
  suv.y = 1.0 - suv.y;                       // y down
  vec2 uv = suv * uCoverScale + uCoverOffset; // panorama uv
  float side = step(0.5, uv.x);
  vec2 p = vec2((uv.x - 0.5 * side) * 2.0 * ${HALF_ASPECT.toFixed(6)}, uv.y);
  vec3 seed = side < 0.5 ? vec3(0.0) : vec3(41.3, 17.9, 7.1);
  vec2 w = windDir(side);

  // Seamless loop: blend f(t) and f(t - LOOP), preserving variance.
  vec2 a = fields(p, w, uT, seed);
  vec2 b = fields(p, w, uT - ${LOOP.toFixed(1)}, seed);
  float wa = 1.0 - uPhase, wb = uPhase;
  vec2 f = (a * wa + b * wb) / sqrt(wa * wa + wb * wb);
  gl_FragColor = vec4(clamp(f * 0.5 + 0.5, 0.0, 1.0), 0.0, 1.0);
}`;

  // Pass 2: re-render the photo's fog with animated transmission.
  const COMP_FRAG = `
precision highp float;
uniform sampler2D uImage;
uniform sampler2D uTrans;
uniform sampler2D uFog;
uniform vec2 uRes;
uniform vec2 uCoverScale;
uniform vec2 uCoverOffset;
uniform vec3 uAir;
uniform float uDensity;
uniform float uWisps;

void main() {
  vec2 suv = gl_FragCoord.xy / uRes;
  vec2 fogUv = suv;
  suv.y = 1.0 - suv.y;
  vec2 uv = suv * uCoverScale + uCoverOffset;
  float lx = uv.x < 0.5 ? uv.x * 2.0 : (1.0 - uv.x) * 2.0; // mirror right half
  vec2 tc = vec2(lx, uv.y);

  vec3 I = texture2D(uImage, tc).rgb;
  float te = max(texture2D(uTrans, tc).r, 0.15);
  vec3 J = clamp((I - uAir) / te + uAir, 0.0, 1.0);

  vec2 f = texture2D(uFog, fogUv).rg * 2.0 - 1.0;
  float d = -log(te);                       // optical depth: 0 near … ~1.9 far
  float farW = smoothstep(0.15, 1.2, d);
  float n = mix(f.y, f.x, farW);            // near pixels follow wisps, far follow billows
  float k = exp(uDensity * (n * 1.25 + 0.12));
  k = max(k, mix(0.55, 0.85, farW));        // don't over-reveal deep fog
  float t2 = pow(te, k);
  vec3 col = J * t2 + uAir * (1.0 - t2);

  // A thin travelling veil in mid-depth, never over the nearest trees.
  float veil = smoothstep(0.15, 0.8, f.y * 0.5 + 0.5) * smoothstep(0.06, 0.55, d);
  col = mix(col, uAir * 0.985, veil * uWisps * 0.32);

  float dither = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
  col += (dither - 0.5) / 255.0;
  gl_FragColor = vec4(col, 1.0);
}`;

  function compile(gl, type, src) {
    const s = gl.createShader(type);
    gl.shaderSource(s, src);
    gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
    return s;
  }

  function program(gl, frag) {
    const p = gl.createProgram();
    gl.attachShader(p, compile(gl, gl.VERTEX_SHADER, VERT));
    gl.attachShader(p, compile(gl, gl.FRAGMENT_SHADER, frag));
    gl.bindAttribLocation(p, 0, "aPos");
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    const u = {};
    const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < n; i++) {
      const name = gl.getActiveUniform(p, i).name;
      u[name] = gl.getUniformLocation(p, name);
    }
    return { p, u };
  }

  function texture(gl, src, format) {
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    if (src) gl.texImage2D(gl.TEXTURE_2D, 0, format, format, gl.UNSIGNED_BYTE, src);
    return t;
  }

  function loadImage(url) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error("Could not load " + url.slice(0, 40)));
      img.src = url;
    });
  }

  async function create(canvas, assets, opts) {
    const params = Object.assign({}, defaults, opts || {});
    const gl = canvas.getContext("webgl", { antialias: false, alpha: false, preserveDrawingBuffer: false, powerPreference: "low-power" });
    if (!gl) throw new Error("WebGL is not available.");

    const [img, trans] = await Promise.all([loadImage(assets.image), loadImage(assets.transmission)]);
    const imageTex = texture(gl, img, gl.RGB);
    const transTex = texture(gl, trans, gl.LUMINANCE);
    const fogTex = texture(gl, null, gl.RGBA);
    const fbo = gl.createFramebuffer();

    const quad = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, quad);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

    const fogProg = program(gl, FOG_FRAG);
    const compProg = program(gl, COMP_FRAG);

    let W = 0, H = 0, FW = 0, FH = 0;
    const cover = { scale: [1, 1], offset: [0, 0] };

    function resize() {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const w = Math.max(1, Math.round(canvas.clientWidth * dpr));
      const h = Math.max(1, Math.round(canvas.clientHeight * dpr));
      if (w === W && h === H) return;
      W = canvas.width = w;
      H = canvas.height = h;
      FW = Math.max(1, Math.round(W * params.fogScale));
      FH = Math.max(1, Math.round(H * params.fogScale));
      gl.bindTexture(gl.TEXTURE_2D, fogTex);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, FW, FH, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
      gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, fogTex, 0);
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);

      // "Cover" fit of the 32:9 panorama into whatever the window is.
      const pano = 2 * HALF_ASPECT, view = W / H;
      if (view > pano) cover.scale = [1, pano / view];
      else cover.scale = [view / pano, 1];
      cover.offset = [(1 - cover.scale[0]) / 2, (1 - cover.scale[1]) / 2];
    }

    let clock = 0, last = performance.now(), lastDraw = 0, raf = 0, frames = 0, fpsStamp = last;
    let measuredFps = 0, onFps = null;

    function draw(now) {
      raf = requestAnimationFrame(draw);
      const minGap = 1000 / params.fps - 2;
      if (now - lastDraw < minGap) return;
      lastDraw = now;
      const dt = Math.min((now - last) / 1000, 0.25);
      last = now;
      clock = (clock + dt * params.speed) % LOOP;

      resize();

      gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
      gl.viewport(0, 0, FW, FH);
      gl.useProgram(fogProg.p);
      gl.uniform2f(fogProg.u.uFogRes, FW, FH);
      gl.uniform2fv(fogProg.u.uCoverScale, cover.scale);
      gl.uniform2fv(fogProg.u.uCoverOffset, cover.offset);
      gl.uniform1f(fogProg.u.uT, clock);
      gl.uniform1f(fogProg.u.uPhase, clock / LOOP);
      gl.drawArrays(gl.TRIANGLES, 0, 3);

      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, W, H);
      gl.useProgram(compProg.p);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, imageTex);
      gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, transTex);
      gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, fogTex);
      gl.uniform1i(compProg.u.uImage, 0);
      gl.uniform1i(compProg.u.uTrans, 1);
      gl.uniform1i(compProg.u.uFog, 2);
      gl.uniform2f(compProg.u.uRes, W, H);
      gl.uniform2fv(compProg.u.uCoverScale, cover.scale);
      gl.uniform2fv(compProg.u.uCoverOffset, cover.offset);
      gl.uniform3fv(compProg.u.uAir, assets.airlight);
      gl.uniform1f(compProg.u.uDensity, params.density);
      gl.uniform1f(compProg.u.uWisps, params.wisps);
      gl.drawArrays(gl.TRIANGLES, 0, 3);

      frames++;
      if (now - fpsStamp >= 1000) {
        measuredFps = frames * 1000 / (now - fpsStamp);
        frames = 0; fpsStamp = now;
        if (onFps) onFps(measuredFps);
      }
    }

    function start() { if (!raf) { last = performance.now(); raf = requestAnimationFrame(draw); } }
    function stop() { cancelAnimationFrame(raf); raf = 0; }
    document.addEventListener("visibilitychange", () => (document.hidden ? stop() : start()));
    start();

    return {
      params,
      set(key, value) { if (key in params) params[key] = value; if (key === "fogScale") W = 0; },
      onFps(fn) { onFps = fn; },
      start, stop,
    };
  }

  window.MistyRidge = { create, defaults };
})();
