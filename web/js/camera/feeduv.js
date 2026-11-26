// Feed crop: which part of the camera frame the 480x640 output shows.
// Coordinates are texture UVs with v = 0 at the bottom of the frame (the
// camera texture is uploaded with UNPACK_FLIP_Y). A mirrored crop has
// u0 > u1, so mapping feed space (s, t) linearly flips it horizontally.

function cropSize(w, h, aspect) {
  // largest centred rectangle with width / height == aspect
  if (w / h > aspect) return { cw: h * aspect, ch: h };
  return { cw: w, ch: w / aspect };
}

export function feedCrop(videoWidth, videoHeight, { feed = 'portrait', mirror = false } = {}) {
  let u0 = 0, u1 = 1, v0 = 0, v1 = 1;
  if (videoWidth > 0 && videoHeight > 0) {
    const { cw, ch } = cropSize(videoWidth, videoHeight, feed === 'square' ? 1 : 3 / 4);
    u0 = (videoWidth - cw) / 2 / videoWidth;
    u1 = 1 - u0;
    v0 = (videoHeight - ch) / 2 / videoHeight;
    v1 = 1 - v0;
  }
  if (mirror) [u0, u1] = [u1, u0];
  return { u0, u1, v0, v1 };
}

// Pixel rectangle in the video frame (top-left origin) for drawImage.
export function feedRect(videoWidth, videoHeight, { feed = 'portrait', mirror = false } = {}) {
  if (!(videoWidth > 0 && videoHeight > 0)) return { sx: 0, sy: 0, sw: 0, sh: 0, mirror: !!mirror };
  const { cw, ch } = cropSize(videoWidth, videoHeight, feed === 'square' ? 1 : 3 / 4);
  return {
    sx: Math.round((videoWidth - cw) / 2),
    sy: Math.round((videoHeight - ch) / 2),
    sw: Math.round(cw),
    sh: Math.round(ch),
    mirror: !!mirror,
  };
}

// Feed space (s, t in 0..1, t up) to texture UV, the same mapping the shaders use.
export function feedPoint(crop, s, t) {
  return [crop.u0 + (crop.u1 - crop.u0) * s, crop.v0 + (crop.v1 - crop.v0) * t];
}
