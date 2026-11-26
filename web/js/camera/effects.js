// Live camera renderer: WebGL with the 12 effects, or a plain 2D canvas
// fallback (no effects) when WebGL is not available. Nothing here touches
// browser globals at import time.

import { feedCrop, feedRect } from './feeduv.js';
import { VERTEX, FRAGMENTS, programKey } from './shaders.js';
import { loadEffectMap } from './maps.js';
import { encodeCanvasJpeg } from './capture.js';

const RAW_EFFECTS = [
  { id: 0, name: 'None', kind: 'none', map: null, smoothing: 0, taps: 0, feed: 'portrait' },
  { id: 1, name: 'Mirror', kind: 'mirror', map: null, smoothing: 0.015, taps: 0, feed: 'portrait' },
  { id: 2, name: 'Warp', kind: 'map', map: 'Warp', smoothing: 0.1, taps: 2, feed: 'portrait' },
  { id: 3, name: 'Shrink', kind: 'map', map: 'Shrink', smoothing: 0.15, taps: 4, feed: 'portrait' },
  { id: 4, name: 'Stretch', kind: 'map', map: 'Stretch', smoothing: 0.15, taps: 4, feed: 'portrait' },
  { id: 5, name: 'Kaleidoscope', kind: 'map', map: 'Kaleidoscope', smoothing: 0.2, taps: 4, feed: 'square' },
  { id: 6, name: 'Bulge', kind: 'map', map: 'Bulge', smoothing: 0.1, taps: 2, feed: 'portrait' },
  { id: 7, name: 'Twist', kind: 'map', map: 'Twist', smoothing: 0.15, taps: 4, feed: 'portrait' },
  { id: 8, name: 'V2Mirror', kind: 'v2mirror', map: null, smoothing: 0, taps: 0, feed: 'portrait', lines: [0.25, 0.75] },
  { id: 9, name: 'Square', kind: 'map', map: 'Square', smoothing: 0.1, taps: 2, feed: 'portrait' },
  { id: 10, name: 'Grid', kind: 'map', map: 'Grid', smoothing: 0.15, taps: 4, feed: 'portrait' },
  { id: 11, name: 'Hourglass', kind: 'map', map: 'Hourglass', smoothing: 0.15, taps: 4, feed: 'portrait' },
];

export const EFFECTS = Object.freeze(RAW_EFFECTS.map((e) => Object.freeze({
  ...e, key: `CameraEffectName_${e.name}`, ...(e.lines ? { lines: Object.freeze(e.lines) } : {}),
})));

export const effectById = (id) => EFFECTS.find((e) => e.id === id) || null;

const GL_OPTIONS = {
  alpha: false, depth: false, stencil: false, antialias: false,
  premultipliedAlpha: false, preserveDrawingBuffer: true,
};

function hasFrame(video) {
  return !!(video && video.readyState >= 2 && video.videoWidth > 0 && video.videoHeight > 0);
}

function createGl(canvas, log) {
  let gl = null;
  try {
    gl = canvas.getContext('webgl', GL_OPTIONS) || canvas.getContext('experimental-webgl', GL_OPTIONS);
  } catch (err) {
    log?.warn?.(`webgl: ${err && err.message}`);
  }
  return gl || null;
}

function compile(gl, type, src) {
  const sh = gl.createShader(type);
  gl.shaderSource(sh, src);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
    const msg = gl.getShaderInfoLog(sh);
    gl.deleteShader(sh);
    throw new Error(`shader compile: ${msg}`);
  }
  return sh;
}

function link(gl, fragSrc) {
  const vs = compile(gl, gl.VERTEX_SHADER, VERTEX);
  const fs = compile(gl, gl.FRAGMENT_SHADER, fragSrc);
  const p = gl.createProgram();
  gl.attachShader(p, vs);
  gl.attachShader(p, fs);
  gl.bindAttribLocation(p, 0, 'a_pos');
  gl.linkProgram(p);
  gl.deleteShader(vs);
  gl.deleteShader(fs);
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) {
    const msg = gl.getProgramInfoLog(p);
    gl.deleteProgram(p);
    throw new Error(`program link: ${msg}`);
  }
  const u = {};
  for (const name of ['u_cam', 'u_crop', 'u_map', 'u_texel', 'u_smooth', 'u_band', 'u_lines']) {
    u[name] = gl.getUniformLocation(p, name);
  }
  return { program: p, u };
}

function texture(gl) {
  const t = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, t);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  return t;
}

function unpackRaw(gl) {
  gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
  gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
}

export function createRenderer({
  width = 480,
  height = 640,
  document = globalThis.document,
  assets = null,
  log = null,
  fetch: fetchFn = globalThis.fetch,
  loadMap = loadEffectMap,
  raf = globalThis.requestAnimationFrame?.bind(globalThis),
  caf = globalThis.cancelAnimationFrame?.bind(globalThis),
  webgl: allowWebgl = true,
} = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;

  let gl = allowWebgl ? createGl(canvas, log) : null;
  const ctx2d = gl ? null : canvas.getContext('2d');
  let camera = null;
  let effectId = 0;
  let running = false;
  let frameReq = null;
  let lost = false;
  let destroyed = false;
  let glState = null; // { buffer, camTex, programs: Map, maps: Map }
  const mapJobs = new Map(); // map name -> Promise<map data>
  const mapData = new Map(); // map name -> { width, height, data, source }
  const badPrograms = new Set();

  function initGl() {
    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    glState = { buffer, camTex: texture(gl), programs: new Map(), maps: new Map() };
    gl.viewport(0, 0, width, height);
  }

  if (gl) {
    initGl();
    canvas.addEventListener?.('webglcontextlost', (e) => {
      e.preventDefault();
      lost = true;
      glState = null;
      log?.warn?.('webgl context lost');
    });
    canvas.addEventListener?.('webglcontextrestored', () => {
      lost = false;
      initGl();
      log?.info?.('webgl context restored');
    });
  }

  function program(key) {
    if (badPrograms.has(key)) return null;
    let p = glState.programs.get(key);
    if (!p) {
      try {
        p = link(gl, FRAGMENTS[key]);
      } catch (err) {
        badPrograms.add(key);
        log?.error?.(`effect program ${key}: ${err.message}`);
        return null;
      }
      glState.programs.set(key, p);
    }
    return p;
  }

  function requestMap(name) {
    if (!mapJobs.has(name)) {
      const job = loadMap(name, { assets, fetch: fetchFn, log }).then((m) => {
        mapData.set(name, m);
        log?.debug?.(`effect map ${name}: ${m.source} ${m.width}x${m.height}`);
        return m;
      });
      mapJobs.set(name, job);
      job.catch((err) => log?.error?.(`effect map ${name}: ${err && err.message}`));
    }
    return mapJobs.get(name);
  }

  function mapTexture(name) {
    let t = glState.maps.get(name);
    if (t) return t;
    const m = mapData.get(name);
    if (!m) return null;
    const tex = texture(gl);
    unpackRaw(gl);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, m.width, m.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, m.data);
    t = { tex, width: m.width, height: m.height };
    glState.maps.set(name, t);
    return t;
  }

  function mirrored() {
    return !!camera && camera.facing !== 'environment';
  }

  function drawGl(video) {
    let effect = effectById(effectId) || EFFECTS[0];
    let mapTex = null;
    if (effect.kind === 'map') {
      mapTex = mapTexture(effect.map);
      if (!mapTex) effect = EFFECTS[0]; // map still loading
    }
    let p = program(programKey(effect));
    if (!p) { effect = EFFECTS[0]; mapTex = null; p = program('none'); }
    if (!p) return false;

    gl.useProgram(p.program);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, glState.camTex);
    unpackRaw(gl);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, video);
    gl.uniform1i(p.u.u_cam, 0);
    const c = feedCrop(video.videoWidth, video.videoHeight, { feed: effect.feed, mirror: mirrored() });
    gl.uniform4f(p.u.u_crop, c.u0, c.u1, c.v0, c.v1);
    if (mapTex) {
      gl.activeTexture(gl.TEXTURE1);
      gl.bindTexture(gl.TEXTURE_2D, mapTex.tex);
      gl.uniform1i(p.u.u_map, 1);
      gl.uniform2f(p.u.u_texel, 1 / mapTex.width, 1 / mapTex.height);
      gl.uniform1f(p.u.u_smooth, effect.smoothing);
    } else if (effect.kind === 'mirror') {
      gl.uniform1f(p.u.u_band, effect.smoothing);
    } else if (effect.kind === 'v2mirror') {
      gl.uniform2f(p.u.u_lines, effect.lines[0], effect.lines[1]);
    }
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    return true;
  }

  function draw2d(video) {
    const r = feedRect(video.videoWidth, video.videoHeight, { feed: 'portrait', mirror: mirrored() });
    ctx2d.save();
    if (r.mirror) {
      ctx2d.translate(width, 0);
      ctx2d.scale(-1, 1);
    }
    ctx2d.drawImage(video, r.sx, r.sy, r.sw, r.sh, 0, 0, width, height);
    ctx2d.restore();
    return true;
  }

  function blank() {
    if (gl) {
      gl.clearColor(0.12, 0.12, 0.12, 1);
      gl.clear(gl.COLOR_BUFFER_BIT);
    } else if (ctx2d) {
      ctx2d.fillStyle = '#1f1f1f';
      ctx2d.fillRect(0, 0, width, height);
    }
  }

  // Draws one frame; returns true when a camera frame was drawn.
  function render() {
    if (destroyed || lost) return false;
    const video = camera && camera.active ? camera.video : null;
    if (!hasFrame(video)) {
      blank();
      return false;
    }
    try {
      return gl ? drawGl(video) : draw2d(video);
    } catch (err) {
      log?.warn?.(`camera render: ${err && err.message}`);
      return false;
    }
  }

  function loop() {
    frameReq = null;
    if (!running) return;
    render();
    frameReq = raf ? raf(loop) : null;
  }

  const api = {
    canvas,
    get webgl() { return !!gl; },
    get effects() { return gl ? EFFECTS : EFFECTS.slice(0, 1); },
    get effect() { return effectId; },
    attach(cam) { camera = cam; },
    setEffect(id) {
      const e = effectById(Number(id));
      if (!e || (!gl && e.id !== 0)) return effectId;
      effectId = e.id;
      if (gl && e.kind === 'map') requestMap(e.map);
      return effectId;
    },
    // resolves when the current effect can be drawn (its map is loaded)
    async whenReady() {
      const e = effectById(effectId);
      if (gl && e && e.kind === 'map') await requestMap(e.map);
    },
    preload() {
      if (!gl) return Promise.resolve();
      return Promise.all(EFFECTS.filter((e) => e.kind === 'map').map((e) => requestMap(e.map))).then(() => {});
    },
    render,
    start() {
      if (running || destroyed) return;
      running = true;
      loop();
    },
    stop() {
      running = false;
      if (frameReq !== null && caf) caf(frameReq);
      frameReq = null;
    },
    async capture() {
      if (!render()) throw new Error('no camera frame to capture');
      const { jpeg, quality } = await encodeCanvasJpeg(canvas);
      return { jpeg, quality, usesFX: effectId !== 0 };
    },
    destroy() {
      api.stop();
      destroyed = true;
      if (gl && glState) {
        for (const p of glState.programs.values()) gl.deleteProgram(p.program);
        for (const t of glState.maps.values()) gl.deleteTexture(t.tex);
        gl.deleteTexture(glState.camTex);
        gl.deleteBuffer(glState.buffer);
        glState = null;
        gl.getExtension('WEBGL_lose_context')?.loseContext();
      }
      gl = null;
      canvas.remove?.();
    },
  };
  return api;
}
