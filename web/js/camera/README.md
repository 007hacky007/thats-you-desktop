# Camera, effects, capture and drawing (U4)

`createMedia({ assets, log })` in `media.js` implements contract section 5.8.
All GLSL is our own (`shaders.js`); the app's shaders are not used.

Modules: `camera.js` (getUserMedia, flip, 10 s delayed release),
`feeduv.js` (3:4 or square centred crop, mirroring for the front camera),
`effects.js` (EFFECTS table, WebGL renderer, 2D fallback), `shaders.js`,
`maps.js` (art maps through `assets.effectMap`, procedural fallback),
`png.js` (PNG decoder so map alpha stays unpremultiplied), `capture.js`
(JPEG rule, placeholder portrait), `../draw/tools.js` and `../draw/surface.js`.

## Additions to the contract (extra, optional to use)

- `camera.supported` (false without `navigator.mediaDevices`), `camera.stream`.
  `start()` rejects with the getUserMedia error (`NotAllowedError`,
  `NotFoundError`, ...), `NotSupportedError` without mediaDevices, or
  `AbortError` when `stopNow()` ran while starting.
- Renderer: `effects` (the effects this renderer can show: only None without
  WebGL), `render()` (draw one frame now, true when a camera frame was drawn),
  `whenReady()` (resolves when the current effect's map is loaded), `preload()`
  (load all maps). While a map is loading the frame is drawn without effect.
  `setEffect` returns the effect id now in use.
- Drawing surface: `setBackground` resolves `true` when shown and `false` when
  the image failed (white background then); `isEmpty` is true while no brush or
  spray stroke is present (eraser strokes alone do not count); `export()` also
  returns `quality`; `strokeCount` and `addStroke({ tool, colour, size, points })`
  (points in 384x512 output pixels) exist for demos and tests. The element gets
  `data-testid="draw-canvas"` and `touch-action: none`; the screen sizes it with
  CSS at 3:4.

## Deviations and guesses

- Procedural Kaleidoscope map: it folds the whole picture six ways around the
  centre, so its corners are not the identity (the other eight procedural maps
  keep the corners in place, as the contract asks). The app's own Kaleidoscope
  map is not the identity at the corners either.
- Mirror keeps the left half of the shown image and reflects it to the right,
  cross-fading a band of width 0.015 at the seam. Which half the original kept
  is unverified.
- Brush soft edge: a rim at half alpha out to 0.9 of the radius, the core at
  full alpha to 0.85. Spray stamps are a linear radial gradient from alpha 0.25.

## Dev check

Serve the repo root (`python3 -m http.server`) and use a scratch page under
`work/dev/` (git-ignored) that imports `../../web/js/camera/media.js` with a
stub `assets.effectMap` pointing at the exported map PNGs and a stub
`mediaDevices` returning `canvas.captureStream()`.
