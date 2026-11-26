// GLSL ES 1.0 programs for the live camera effects (our own shaders).
// Output UV v_uv runs 0..1 over the 480x640 canvas, v = 0 at the bottom.
// cam(st) samples the camera at feed position st (0..1 over the cropped,
// possibly mirrored feed rectangle given by u_crop = (u0, u1, v0, v1)).

export const VERTEX = `
attribute vec2 a_pos;
varying vec2 v_uv;
void main() {
  v_uv = a_pos * 0.5 + 0.5;
  gl_Position = vec4(a_pos, 0.0, 1.0);
}
`;

const HEADER = `
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
varying vec2 v_uv;
uniform sampler2D u_cam;
uniform vec4 u_crop;
vec3 cam(vec2 st) {
  st = clamp(st, 0.0, 1.0);
  return texture2D(u_cam, vec2(mix(u_crop.x, u_crop.y, st.x), mix(u_crop.z, u_crop.w, st.y))).rgb;
}
`;

const NONE = `${HEADER}
void main() {
  gl_FragColor = vec4(cam(v_uv), 1.0);
}
`;

// Map lookup: X = (R + G) / 2, Y = (B + A) / 2. Extra samples half a map
// texel away hide the 8-bit steps of the map; `u_smooth` mixes their mean
// into the centre sample.
function mapShader(taps) {
  return `${HEADER}
uniform sampler2D u_map;
uniform vec2 u_texel;
uniform float u_smooth;
vec2 lookup(vec2 p) {
  vec4 m = texture2D(u_map, p);
  return vec2(m.r + m.g, m.b + m.a) * 0.5;
}
void main() {
  vec2 c = lookup(v_uv);
  vec2 h = vec2(0.5 * u_texel.x, 0.0);
  vec2 sum = lookup(v_uv + h) + lookup(v_uv - h);
${taps === 4 ? `  vec2 w = vec2(0.0, 0.5 * u_texel.y);
  sum += lookup(v_uv + w) + lookup(v_uv - w);
  vec2 mean = sum * 0.25;` : '  vec2 mean = sum * 0.5;'}
  gl_FragColor = vec4(cam(mix(c, mean, u_smooth)), 1.0);
}
`;
}

// Left half kept and reflected onto the right; the seam is cross-faded over
// a band of width u_band so the fold does not show a hard line.
const MIRROR = `${HEADER}
uniform float u_band;
void main() {
  float f = smoothstep(0.5 - 0.5 * u_band, 0.5 + 0.5 * u_band, v_uv.x);
  vec3 a = cam(v_uv);
  vec3 b = cam(vec2(1.0 - v_uv.x, v_uv.y));
  gl_FragColor = vec4(mix(a, b, f), 1.0);
}
`;

// Two mirror lines (u_lines.x < u_lines.y): the band between them is kept
// and reflected outwards on both sides.
const V2MIRROR = `${HEADER}
uniform vec2 u_lines;
void main() {
  float x = u_lines.x + abs(v_uv.x - u_lines.x);
  x = u_lines.y - abs(x - u_lines.y);
  gl_FragColor = vec4(cam(vec2(x, v_uv.y)), 1.0);
}
`;

export const FRAGMENTS = Object.freeze({
  none: NONE,
  map2: mapShader(2),
  map4: mapShader(4),
  mirror: MIRROR,
  v2mirror: V2MIRROR,
});

export function programKey(effect) {
  if (!effect || effect.kind === 'none') return 'none';
  if (effect.kind === 'map') return effect.taps === 4 ? 'map4' : 'map2';
  return effect.kind;
}

// JS twin of the V2Mirror fold, used by tests and kept next to the shader.
export function v2mirrorX(x, a, b) {
  const x1 = a + Math.abs(x - a);
  return b - Math.abs(x1 - b);
}
