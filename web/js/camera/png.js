// Minimal PNG decoder for effect maps: 8-bit greyscale, RGB, grey+alpha and
// RGBA, non-interlaced, filters 0-4. The browser image path premultiplies
// alpha and may colour-convert; the maps carry data in alpha, so they are
// decoded here instead. Output is always RGBA, rows top first.

const SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const CHANNELS = { 0: 1, 2: 3, 4: 2, 6: 4 };
const MAX_PIXELS = 4096 * 4096;

export async function inflateZlib(bytes) {
  if (typeof DecompressionStream !== 'function') throw new Error('DecompressionStream not available');
  const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('deflate'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

function u32(b, o) {
  return ((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]) >>> 0;
}

function parse(bytes) {
  if (bytes.length < 8 || SIGNATURE.some((v, i) => bytes[i] !== v)) throw new Error('not a png');
  let off = 8;
  let header = null;
  const idat = [];
  let idatLen = 0;
  for (;;) {
    if (off + 8 > bytes.length) throw new Error('png truncated');
    const len = u32(bytes, off);
    const type = String.fromCharCode(bytes[off + 4], bytes[off + 5], bytes[off + 6], bytes[off + 7]);
    const start = off + 8;
    if (start + len + 4 > bytes.length) throw new Error('png truncated');
    const data = bytes.subarray(start, start + len);
    if (type === 'IHDR') {
      if (len !== 13) throw new Error('bad png header');
      header = {
        width: u32(data, 0), height: u32(data, 4), depth: data[8],
        colourType: data[9], compression: data[10], filter: data[11], interlace: data[12],
      };
    } else if (type === 'IDAT') {
      idat.push(data);
      idatLen += len;
    } else if (type === 'IEND') {
      break;
    }
    off = start + len + 4;
  }
  if (!header) throw new Error('png without header');
  if (header.depth !== 8) throw new Error(`unsupported png bit depth ${header.depth}`);
  if (!(header.colourType in CHANNELS)) throw new Error(`unsupported png colour type ${header.colourType}`);
  if (header.interlace !== 0) throw new Error('interlaced png not supported');
  if (header.compression !== 0 || header.filter !== 0) throw new Error('unsupported png method');
  if (!header.width || !header.height || header.width * header.height > MAX_PIXELS) throw new Error('bad png size');
  const z = new Uint8Array(idatLen);
  let p = 0;
  for (const part of idat) { z.set(part, p); p += part.length; }
  return { header, z };
}

function paeth(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  return pb <= pc ? b : c;
}

function unfilter(raw, width, height, ch) {
  const stride = width * ch;
  if (raw.length < height * (stride + 1)) throw new Error('png data truncated');
  const out = new Uint8Array(height * stride);
  for (let y = 0; y < height; y++) {
    const f = raw[y * (stride + 1)];
    const src = y * (stride + 1) + 1;
    const row = y * stride;
    const prev = row - stride;
    for (let x = 0; x < stride; x++) {
      const v = raw[src + x];
      const a = x >= ch ? out[row + x - ch] : 0;
      const b = y > 0 ? out[prev + x] : 0;
      let pred;
      switch (f) {
        case 0: pred = 0; break;
        case 1: pred = a; break;
        case 2: pred = b; break;
        case 3: pred = (a + b) >> 1; break;
        case 4: pred = paeth(a, b, x >= ch && y > 0 ? out[prev + x - ch] : 0); break;
        default: throw new Error(`bad png filter ${f}`);
      }
      out[row + x] = (v + pred) & 0xff;
    }
  }
  return out;
}

function toRgba(px, n, ch) {
  if (ch === 4) return px;
  const out = new Uint8Array(n * 4);
  for (let i = 0; i < n; i++) {
    const o = i * 4;
    if (ch === 3) {
      out[o] = px[i * 3]; out[o + 1] = px[i * 3 + 1]; out[o + 2] = px[i * 3 + 2]; out[o + 3] = 255;
    } else if (ch === 1) {
      out[o] = out[o + 1] = out[o + 2] = px[i]; out[o + 3] = 255;
    } else {
      out[o] = out[o + 1] = out[o + 2] = px[i * 2]; out[o + 3] = px[i * 2 + 1];
    }
  }
  return out;
}

// bytes: Uint8Array of a PNG file -> { width, height, data: Uint8Array RGBA }
export async function decodePng(bytes, { inflate = inflateZlib } = {}) {
  const { header, z } = parse(bytes);
  const ch = CHANNELS[header.colourType];
  const raw = await inflate(z);
  const px = unfilter(raw, header.width, header.height, ch);
  return { width: header.width, height: header.height, data: toRgba(px, header.width * header.height, ch) };
}
