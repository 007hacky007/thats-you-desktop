// Test helper: writes small PNG files with chosen filters (node:zlib).
import zlib from 'node:zlib';

// --- tiny PNG writer for fixtures ---------------------------------------
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
export function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
export function chunk(type, data) {
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'ascii');
  Buffer.from(data).copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}
function paeth(a, b, c) {
  const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}
function filterRows(px, w, h, ch, filterFor) {
  const stride = w * ch;
  const out = Buffer.alloc(h * (stride + 1));
  for (let y = 0; y < h; y++) {
    const f = filterFor(y);
    out[y * (stride + 1)] = f;
    for (let x = 0; x < stride; x++) {
      const cur = px[y * stride + x];
      const a = x >= ch ? px[y * stride + x - ch] : 0;
      const b = y > 0 ? px[(y - 1) * stride + x] : 0;
      const c = x >= ch && y > 0 ? px[(y - 1) * stride + x - ch] : 0;
      const pred = [0, a, b, (a + b) >> 1, paeth(a, b, c)][f];
      out[y * (stride + 1) + 1 + x] = (cur - pred) & 0xff;
    }
  }
  return out;
}
export function makePng({ w, h, colorType, px, filterFor = () => 0, idatSplit = 1, extra = [] }) {
  const ch = { 0: 1, 2: 3, 4: 2, 6: 4 }[colorType];
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = colorType; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const z = zlib.deflateSync(filterRows(px, w, h, ch, filterFor));
  const parts = [];
  const step = Math.ceil(z.length / idatSplit);
  for (let i = 0; i < z.length; i += step) parts.push(chunk('IDAT', z.subarray(i, i + step)));
  return new Uint8Array(Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), ...extra, ...parts, chunk('IEND', Buffer.alloc(0)),
  ]));
}
export function pixels(w, h, ch, seed = 7) {
  const px = new Uint8Array(w * h * ch);
  let s = seed;
  for (let i = 0; i < px.length; i++) { s = (s * 1103515245 + 12345) >>> 0; px[i] = (s >>> 16) & 0xff; }
  return px;
}
// -------------------------------------------------------------------------
