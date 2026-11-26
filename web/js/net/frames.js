// Data channel envelope (contract 2.1 and 2.2).

export const JSON_TRANSFER_ID = -7503;
export const MAX_TEXT = 65536;
export const CHUNK = 16384;
export const CHUNK_HEADER = 12;
export const MAX_CONTENT = 131072;
export const MAX_PARTIAL = 4;
export const MAX_TRANSFER_ID = 2147483647;

const encoder = new TextEncoder();

export function utf8Length(s) {
  return encoder.encode(s).length;
}

// ---- text frame builders (exact forms of 2.1) ----

export function helloFrame(token, uid) {
  return JSON.stringify({ t: 'hello', token: String(token), uid: String(uid) });
}

export function pingFrame() {
  return '{"t":"ping"}';
}

// `json` is the game message object (embedded as an object, not a string).
export function msgFrame(json) {
  return JSON.stringify({ t: 'msg', id: JSON_TRANSFER_ID, json });
}

// Builders for the game side (tests, fakes).
export function joinedFrame() { return '{"t":"joined"}'; }
export function pendingFrame() { return '{"t":"pending"}'; }
export function refusedFrame(reason) { return JSON.stringify({ t: 'refused', reason }); }
export function byeFrame(reason) { return JSON.stringify({ t: 'bye', reason }); }

// Parse one received text frame. Returns null for anything malformed or
// ignorable; otherwise { t, ... }:
//   { t: 'joined' } | { t: 'pending' } | { t: 'ping' }
//   { t: 'refused', reason: number } | { t: 'bye', reason: string }
//   { t: 'msg', json: object }
//   { t: <unknown>, unknown: true }   (ignored by the caller, still counts as traffic)
export function parseTextFrame(text) {
  if (typeof text !== 'string' || text.length === 0) return null;
  // UTF-8 is at least one byte per UTF-16 code unit and at most three.
  if (text.length > MAX_TEXT) return null;
  if (text.length * 3 > MAX_TEXT && utf8Length(text) > MAX_TEXT) return null;
  let o;
  try {
    o = JSON.parse(text);
  } catch {
    return null;
  }
  if (!o || typeof o !== 'object' || Array.isArray(o) || typeof o.t !== 'string') return null;
  switch (o.t) {
    case 'joined':
    case 'pending':
    case 'ping':
      return { t: o.t };
    case 'refused':
      return { t: 'refused', reason: Number.isInteger(o.reason) ? o.reason : 0 };
    case 'bye':
      return { t: 'bye', reason: typeof o.reason === 'string' ? o.reason : '' };
    case 'msg':
      if (o.id !== JSON_TRANSFER_ID) return null;
      if (!o.json || typeof o.json !== 'object' || Array.isArray(o.json)) return null;
      return { t: 'msg', json: o.json };
    default:
      return { t: o.t, unknown: true };
  }
}

// ---- binary frames ----

function toU8(bytes) {
  if (bytes instanceof Uint8Array) return bytes;
  if (bytes instanceof ArrayBuffer) return new Uint8Array(bytes);
  if (ArrayBuffer.isView(bytes)) return new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  throw new TypeError('bytes must be a Uint8Array or ArrayBuffer');
}

// Split one content transfer into chunk frames (each a fresh Uint8Array).
export function encodeChunks(transferId, bytes) {
  const data = toU8(bytes);
  if (!Number.isInteger(transferId) || transferId < 1 || transferId > MAX_TRANSFER_ID) {
    throw new RangeError('transfer id out of range');
  }
  if (data.length < 1 || data.length > MAX_CONTENT) throw new RangeError('content size out of range');
  const frames = [];
  for (let off = 0; off < data.length; off += CHUNK) {
    const n = Math.min(CHUNK, data.length - off);
    const f = new Uint8Array(CHUNK_HEADER + n);
    const dv = new DataView(f.buffer);
    dv.setInt32(0, transferId, true);
    dv.setUint32(4, data.length, true);
    dv.setUint32(8, off, true);
    f.set(data.subarray(off, off + n), CHUNK_HEADER);
    frames.push(f);
  }
  return frames;
}

export function decodeChunkHeader(frame) {
  const f = toU8(frame);
  if (f.length < CHUNK_HEADER) return null;
  const dv = new DataView(f.buffer, f.byteOffset, f.byteLength);
  return {
    transferId: dv.getInt32(0, true),
    total: dv.getUint32(4, true),
    offset: dv.getUint32(8, true),
    payload: f.subarray(CHUNK_HEADER),
  };
}

// Reassembler enforcing every rule of 2.2. push(frame) returns
//   { done: true, transferId, data }  when a transfer completed,
//   { done: false }                   when more chunks are needed,
//   { error: '<why>', transferId }    on a protocol error (that transfer is dropped).
// A sender sends each transfer back to back, so a partial transfer that is
// still open when maxPartial others exist is stale: when a new transfer
// starts at the cap, the oldest partial is abandoned (logged) instead of
// refusing every later transfer on the link.
export function createReassembler({ log, maxPartial = MAX_PARTIAL } = {}) {
  const partial = new Map(); // id -> { total, got, buf }

  function fail(why, id) {
    if (id !== undefined) partial.delete(id);
    if (log) log.warn('content: ' + why + (id !== undefined ? ' (transfer ' + id + ')' : ''));
    return { error: why, transferId: id };
  }

  function push(frame) {
    const hd = decodeChunkHeader(frame);
    if (!hd) return fail('short frame');
    const { transferId: id, total, offset, payload } = hd;
    const n = payload.length;
    if (id < 1) return fail('bad transfer id', id);
    if (total < 1 || total > MAX_CONTENT) return fail('bad total length', id);
    if (n < 1 || n > CHUNK) return fail('bad chunk size', id);
    if (offset + n > total) return fail('chunk past end', id);
    let st = partial.get(id);
    if (!st) {
      if (offset !== 0) return fail('offset gap', id);
      while (partial.size >= maxPartial) {
        const oldest = partial.keys().next().value;
        partial.delete(oldest);
        if (log) log.warn('content: too many partial transfers, abandoned transfer ' + oldest);
      }
      st = { total, got: 0, buf: new Uint8Array(total) };
      partial.set(id, st);
    }
    if (st.total !== total) return fail('total length changed', id);
    if (offset !== st.got) return fail('offset gap', id);
    st.buf.set(payload, offset);
    st.got += n;
    if (st.got === st.total) {
      partial.delete(id);
      return { done: true, transferId: id, data: st.buf };
    }
    return { done: false };
  }

  return {
    push,
    reset: () => partial.clear(),
    get partialCount() { return partial.size; },
  };
}
