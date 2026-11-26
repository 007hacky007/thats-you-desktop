// JPEG encoding with the size rule shared by photos and drawings, and the
// placeholder portrait for phones without a camera. Import-safe in Node.

export const JPEG_RULE = Object.freeze({
  quality: 0.75,
  reencodeAt: 63489, // original app: re-encode at 0.20 when the JPEG has this many bytes or more
  lowQuality: 0.2,
  cap: 128000, // page upload cap (port addition)
  lastQuality: 0.1,
});

// encodeAt(quality) -> Promise<Uint8Array>
export async function encodeJpegRule(encodeAt) {
  const run = async (q) => {
    const bytes = await encodeAt(q);
    if (!bytes || !bytes.length) throw new Error('jpeg encode returned empty data');
    return bytes;
  };
  let quality = JPEG_RULE.quality;
  let jpeg = await run(quality);
  if (jpeg.length < JPEG_RULE.reencodeAt) return { jpeg, quality };
  quality = JPEG_RULE.lowQuality;
  jpeg = await run(quality);
  if (jpeg.length <= JPEG_RULE.cap) return { jpeg, quality };
  quality = JPEG_RULE.lastQuality;
  jpeg = await run(quality);
  if (jpeg.length <= JPEG_RULE.cap) return { jpeg, quality };
  throw new Error(`jpeg too big: ${jpeg.length} bytes at quality ${quality}`);
}

async function blobBytes(blob) {
  return new Uint8Array(await blob.arrayBuffer());
}

// Encoder for a canvas (HTMLCanvasElement or OffscreenCanvas).
export function canvasJpegEncoder(canvas) {
  return async (quality) => {
    let blob;
    if (typeof canvas.convertToBlob === 'function') {
      blob = await canvas.convertToBlob({ type: 'image/jpeg', quality });
    } else {
      blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    }
    if (!blob) throw new Error('canvas could not encode a jpeg');
    return blobBytes(blob);
  };
}

export function encodeCanvasJpeg(canvas) {
  return encodeJpegRule(canvasJpegEncoder(canvas));
}

// Background colours for placeholder portraits (from the drawing palette,
// light and dark enough for a white initial).
const PLACEHOLDER_COLOURS = [
  [243, 60, 43], [32, 63, 230], [114, 75, 43], [183, 100, 167],
  [0, 164, 81], [103, 45, 135], [245, 146, 27], [162, 119, 75],
];

export function placeholderStyle(name) {
  const s = typeof name === 'string' ? name.trim() : '';
  const first = s ? String.fromCodePoint(s.codePointAt(0)) : '';
  const initial = first ? first.toLocaleUpperCase() : '?';
  let hash = 2166136261;
  for (const ch of s) {
    hash ^= ch.codePointAt(0);
    hash = Math.imul(hash, 16777619) >>> 0;
  }
  const [r, g, b] = PLACEHOLDER_COLOURS[hash % PLACEHOLDER_COLOURS.length];
  return { initial, background: `rgb(${r}, ${g}, ${b})` };
}

export function drawPlaceholder(ctx, name, width = 480, height = 640) {
  const { initial, background } = placeholderStyle(name);
  ctx.fillStyle = background;
  ctx.fillRect(0, 0, width, height);
  ctx.fillStyle = 'rgba(255, 255, 255, 0.12)';
  ctx.beginPath();
  ctx.arc(width / 2, height * 0.45, width * 0.36, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = '#ffffff';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.font = `bold ${Math.round(width * 0.5)}px sans-serif`;
  ctx.fillText(initial, width / 2, height * 0.47);
}
