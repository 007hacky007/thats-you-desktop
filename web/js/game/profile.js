// Persistent identity and profile (contract 5.4, protocol.md 3.5).
// Keys (prefixed "iwyk." by the storage): uid, lastJoin, session, playerId,
// portraitId, portrait (base64 JPEG), card, name, guest, profileDone.

import { defaultUuid } from '../net/content.js';

export const REJOIN_MAX_AGE_MS = 6 * 60 * 60 * 1000;

const B64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

export function bytesToBase64(bytes) {
  let out = '';
  let i = 0;
  for (; i + 2 < bytes.length; i += 3) {
    const n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
    out += B64[(n >> 18) & 63] + B64[(n >> 12) & 63] + B64[(n >> 6) & 63] + B64[n & 63];
  }
  const rest = bytes.length - i;
  if (rest === 1) {
    const n = bytes[i] << 16;
    out += B64[(n >> 18) & 63] + B64[(n >> 12) & 63] + '==';
  } else if (rest === 2) {
    const n = (bytes[i] << 16) | (bytes[i + 1] << 8);
    out += B64[(n >> 18) & 63] + B64[(n >> 12) & 63] + B64[(n >> 6) & 63] + '=';
  }
  return out;
}

export function base64ToBytes(s) {
  if (typeof s !== 'string') return null;
  const clean = s.replace(/=+$/, '');
  if (clean.length % 4 === 1) return null;
  const out = new Uint8Array(Math.floor((clean.length * 3) / 4));
  let acc = 0;
  let bits = 0;
  let j = 0;
  for (let i = 0; i < clean.length; i++) {
    const v = B64.indexOf(clean[i]);
    if (v < 0) return null;
    acc = ((acc << 6) | v) & 0xffffff;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      out[j++] = (acc >> bits) & 0xff;
    }
  }
  return out.subarray(0, j);
}

const VALID_LAST_JOIN = (o) => o && typeof o === 'object' && typeof o.h === 'string' && Number.isInteger(Number(o.p))
  && typeof o.u === 'string' && typeof o.w === 'string' && typeof o.f === 'string' && typeof o.t === 'string';

export function createProfile({ storage, uuid = defaultUuid, clock }) {
  function uid() {
    let id = storage.get('uid');
    if (!id || id.length > 128 || /[\x00-\x1f\x7f]/.test(id)) {
      id = uuid();
      storage.set('uid', id);
    }
    return id;
  }

  function lastJoin() {
    const o = storage.getJSON('lastJoin', null);
    return VALID_LAST_JOIN(o) ? o : null;
  }

  function saveLastJoin(params, active) {
    const o = {
      h: params.h, p: Number(params.p), u: params.u, w: params.w, f: params.f, t: params.t,
      at: clock.now(), active: !!active,
    };
    storage.setJSON('lastJoin', o);
    return o;
  }

  function setActive(active) {
    const o = lastJoin();
    if (!o) return;
    o.active = !!active;
    if (active) o.at = clock.now();
    storage.setJSON('lastJoin', o);
  }

  function touchJoin() {
    const o = lastJoin();
    if (!o || !o.active) return;
    o.at = clock.now();
    storage.setJSON('lastJoin', o);
  }

  // Session restore: same SessionID (as a string) keeps the saved profile;
  // otherwise forget the player id, portrait and "profile done", keep the
  // name/card/guest as suggestions and remember the new session.
  function applySession(sessionId) {
    const s = String(sessionId);
    if (storage.get('session') === s) return true;
    for (const k of ['playerId', 'portraitId', 'portrait', 'profileDone']) storage.remove(k);
    storage.set('session', s);
    return false;
  }

  function saved() {
    const portraitB64 = storage.get('portrait');
    return {
      name: storage.get('name') || '',
      cardId: storage.get('card') || '',
      isGuest: storage.get('guest') === '1',
      playerId: storage.get('playerId') === null ? -1 : Number(storage.get('playerId')),
      portraitId: storage.get('portraitId') || '',
      portrait: portraitB64 ? base64ToBytes(portraitB64) : null,
      profileDone: storage.get('profileDone') === '1',
    };
  }

  function saveProfile({ name, cardId, isGuest, portraitId, jpeg }) {
    storage.set('name', name);
    storage.set('card', cardId);
    storage.set('guest', isGuest ? '1' : '0');
    if (portraitId) storage.set('portraitId', portraitId);
    if (jpeg) storage.set('portrait', bytesToBase64(jpeg));
    storage.set('profileDone', '1');
  }

  function setPlayerId(id) {
    storage.set('playerId', String(id));
  }

  return { uid, lastJoin, saveLastJoin, setActive, touchJoin, applySession, saved, saveProfile, setPlayerId };
}
