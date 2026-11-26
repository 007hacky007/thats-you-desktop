// Images in both directions (contract 5.4 "Uploads" and "Downloads").

import { createEmitter } from '../util/emitter.js';
import { nullLog } from '../util/log.js';
import { build, IMG } from './messages.js';

export const MAX_UPLOAD = 128000;
export const ORPHAN_KEEP_MS = 10000;
export const HEADER_KEEP_MS = 60000;
export const STORE_CAPACITY = 120;
// Hard caps against a broken game: required entries may push the store past
// its capacity, but never past REQUIRED_SLACK times it; unpaired download
// headers are kept at most MAX_HEADERS at a time (oldest dropped first).
export const REQUIRED_SLACK = 2;
export const MAX_HEADERS = 64;
export const MAX_ORPHANS = 8;

function defaultMakeUrl(bytes) {
  const U = globalThis.URL;
  if (!U || typeof U.createObjectURL !== 'function' || typeof globalThis.Blob !== 'function') return null;
  return U.createObjectURL(new globalThis.Blob([bytes], { type: 'image/jpeg' }));
}

function defaultRevokeUrl(url) {
  const U = globalThis.URL;
  if (url && U && typeof U.revokeObjectURL === 'function') U.revokeObjectURL(url);
}

export function defaultUuid() {
  const c = globalThis.crypto;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID();
  // RFC 4122 v4 from getRandomValues or Math.random as a last resort
  const b = new Uint8Array(16);
  if (c && typeof c.getRandomValues === 'function') c.getRandomValues(b);
  else for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256);
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  const h = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
  return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
}

// Blob URL store keyed by GUID (or ClientUID). Least recently used entries
// that are not required are revoked first once over capacity.
export function createBlobStore({ capacity = STORE_CAPACITY, makeUrl = defaultMakeUrl, revokeUrl = defaultRevokeUrl, log = nullLog } = {}) {
  const em = createEmitter({ onError: (e) => log.error('store listener: ' + (e && e.message)) });
  const entries = new Map(); // insertion order = LRU order (oldest first)
  let required = new Set();

  function touch(key) {
    const e = entries.get(key);
    if (!e) return null;
    entries.delete(key);
    entries.set(key, e);
    return e;
  }

  function evict(keep) {
    if (entries.size <= capacity) return;
    for (const key of [...entries.keys()]) {
      if (entries.size <= capacity) break;
      if (required.has(key) || key === keep) continue;
      remove(key);
    }
    const hard = capacity * REQUIRED_SLACK;
    if (entries.size <= hard) return;
    log.warn('blob store over ' + hard + ' entries, evicting required images');
    for (const key of [...entries.keys()]) {
      if (entries.size <= hard) break;
      if (key === keep) continue;
      remove(key);
    }
  }

  function put(key, bytes, { own = false } = {}) {
    if (!key) return null;
    const old = entries.get(key);
    if (old) {
      entries.delete(key);
      revokeUrl(old.url);
    }
    let url = null;
    try {
      url = makeUrl(bytes);
    } catch (e) {
      log.warn('blob url failed: ' + e.message);
    }
    entries.set(key, { url, own, size: bytes ? bytes.length : 0 });
    evict(key);
    em.emit('added', key);
    return url;
  }

  function remove(key) {
    const e = entries.get(key);
    if (!e) return false;
    entries.delete(key);
    revokeUrl(e.url);
    em.emit('removed', key);
    return true;
  }

  return {
    put,
    remove,
    has: (key) => entries.has(key),
    url: (key) => {
      const e = touch(key);
      return e ? e.url : null;
    },
    isOwn: (key) => !!(entries.get(key) && entries.get(key).own),
    setRequired(keys) {
      required = new Set(keys);
      evict();
    },
    keys: () => [...entries.keys()],
    get size() { return entries.size; },
    clear() {
      for (const key of [...entries.keys()]) remove(key);
    },
    on: (ev, fn) => em.on(ev, fn),
  };
}

// Pairs game -> phone content headers with their binary transfers, in
// either order. onImage(guid, bytes) / onThumbnail(clientUid, bytes).
export function createDownloads({ clock, log = nullLog, onImage, onThumbnail }) {
  const headers = new Map(); // tid -> { kind, key, timer }
  const orphans = new Map(); // tid -> { bytes, timer }

  function deliver(kind, key, bytes) {
    if (kind === 'image') onImage(key, bytes);
    else onThumbnail(key, bytes);
  }

  function header(msg) {
    let kind;
    let key;
    if (msg.TypeString === 'ImageResourceContentTransferMessage') {
      kind = 'image';
      key = msg.ImageGUID;
    } else if (msg.TypeString === 'PortraitThumbnailContentTransferMessage') {
      kind = 'thumb';
      key = msg.ClientUID;
    } else {
      return false;
    }
    const tid = msg.TransferID;
    const orphan = orphans.get(tid);
    if (orphan) {
      clock.clearTimeout(orphan.timer);
      orphans.delete(tid);
      deliver(kind, key, orphan.bytes);
      return true;
    }
    const old = headers.get(tid);
    if (old) {
      clock.clearTimeout(old.timer);
      headers.delete(tid);
    }
    while (headers.size >= MAX_HEADERS) {
      const [firstTid, first] = headers.entries().next().value;
      clock.clearTimeout(first.timer);
      headers.delete(firstTid);
      log.warn('content header ' + firstTid + ' dropped: too many waiting headers');
    }
    const timer = clock.setTimeout(() => {
      headers.delete(tid);
      log.warn('content header ' + tid + ' never got its data');
    }, HEADER_KEEP_MS);
    headers.set(tid, { kind, key, timer });
    return true;
  }

  function binary(tid, bytes) {
    const hd = headers.get(tid);
    if (hd) {
      clock.clearTimeout(hd.timer);
      headers.delete(tid);
      deliver(hd.kind, hd.key, bytes);
      return;
    }
    const old = orphans.get(tid);
    if (old) clock.clearTimeout(old.timer);
    if (orphans.size >= MAX_ORPHANS && !old) {
      const [firstTid, first] = orphans.entries().next().value;
      clock.clearTimeout(first.timer);
      orphans.delete(firstTid);
    }
    const timer = clock.setTimeout(() => {
      orphans.delete(tid);
      log.warn('content ' + tid + ' arrived without a header');
    }, ORPHAN_KEEP_MS);
    orphans.set(tid, { bytes, timer });
  }

  function reset() {
    for (const h of headers.values()) clock.clearTimeout(h.timer);
    for (const o of orphans.values()) clock.clearTimeout(o.timer);
    headers.clear();
    orphans.clear();
  }

  return { header, binary, reset, get pending() { return headers.size + orphans.size; } };
}

// Upload sequences. Transfer ids start at a random integer in
// [1000, 1000000) per page load and increase by one per upload.
// getLink() returns the current link (or null).
export function createUploads({ getLink, store, log = nullLog, random = Math.random, uuid = defaultUuid }) {
  let nextId = 1000 + Math.floor(random() * 999000);

  function image(jpeg, imgType, guid) {
    if (!(jpeg instanceof Uint8Array) || jpeg.length < 1) {
      log.error('upload rejected: no image');
      return null;
    }
    if (jpeg.length > MAX_UPLOAD) {
      log.error('upload rejected: ' + jpeg.length + ' bytes is over ' + MAX_UPLOAD);
      return null;
    }
    const link = getLink();
    if (!link || link.state !== 'joined') {
      log.warn('upload dropped: not connected');
      return null;
    }
    const g = guid || uuid();
    const tid = nextId++;
    if (nextId > 2147483647) nextId = 1000;
    store.put(g, jpeg, { own: true });
    link.sendMsg(build.imageTransfer(tid, g, imgType));
    const done = link.sendContent(tid, jpeg);
    return { link, guid: g, transferId: tid, done };
  }

  // Photo: header -> binary -> TrophyPhotoFXInfoMessage -> (usePic) UsePicPressedResponseMessage.
  function photo({ jpeg, usesFX, imgType, usePic = false }) {
    const up = image(jpeg, imgType);
    if (!up) return null;
    up.link.sendMsg(build.trophyPhotoFX(up.guid, usesFX));
    if (usePic) up.link.sendMsg(build.usePicPressed());
    return up;
  }

  // Drawing: header -> binary -> TrophyDrawingColouringInfoMessage.
  function drawing({ jpeg, coloursAvailable, coloursUsed, imgType = IMG.PlayerDrawing }) {
    const up = image(jpeg, imgType);
    if (!up) return null;
    up.link.sendMsg(build.trophyDrawingColouring(up.guid, coloursAvailable, coloursUsed));
    return up;
  }

  // Profile: portrait (ImgType 2) -> TrophyPhotoFXInfoMessage -> ClientPlayerProfileMessage.
  function portrait({ jpeg, usesFX, profile }) {
    const up = image(jpeg, IMG.PlayerPortrait);
    if (!up) return null;
    up.link.sendMsg(build.trophyPhotoFX(up.guid, usesFX));
    up.link.sendMsg(profile);
    return up;
  }

  return {
    photo,
    drawing,
    portrait,
    peekTransferId: () => nextId,
  };
}
