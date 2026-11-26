// QR fragment parsing (contract 2.4 and 5.4).
// "#h=<ipv4>&p=<port>&u=<ufrag>&w=<pwd>&f=<b64url sha-256>&t=<token>"
// Extra keys (for example debug=1) are ignored.

const IPV4 = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;
const ICE_CHARS = /^[A-Za-z0-9+/]+$/; // RFC 8839 ice-char
const TOKEN_CHARS = /^[\x21-\x7e]+$/;
const FP_B64URL = /^[A-Za-z0-9_-]{43}$/;

function splitFragment(hash) {
  let s = String(hash || '');
  const i = s.indexOf('#');
  if (i >= 0) s = s.slice(i + 1);
  const out = new Map();
  for (const part of s.split('&')) {
    if (!part) continue;
    const eq = part.indexOf('=');
    const k = eq < 0 ? part : part.slice(0, eq);
    let v = eq < 0 ? '' : part.slice(eq + 1);
    try {
      v = decodeURIComponent(v);
    } catch {
      return null;
    }
    if (!out.has(k)) out.set(k, v);
  }
  return out;
}

export function isIPv4(s) {
  return typeof s === 'string' && IPV4.test(s);
}

// Returns { h, p, u, w, f, t } (p a number) or null when anything is invalid.
export function parseJoinParams(hash) {
  const m = splitFragment(hash);
  if (!m) return null;
  const h = m.get('h');
  const pStr = m.get('p');
  const u = m.get('u');
  const w = m.get('w');
  const f = m.get('f');
  const t = m.get('t');
  if (!isIPv4(h)) return null;
  if (typeof pStr !== 'string' || !/^\d{1,5}$/.test(pStr)) return null;
  const p = Number(pStr);
  if (p < 1 || p > 65535) return null;
  if (typeof u !== 'string' || u.length > 256 || !ICE_CHARS.test(u)) return null;
  if (typeof w !== 'string' || w.length > 256 || !ICE_CHARS.test(w)) return null;
  if (typeof t !== 'string' || t.length > 256 || !TOKEN_CHARS.test(t)) return null;
  if (typeof f !== 'string' || !FP_B64URL.test(f)) return null;
  return { h, p, u, w, f, t };
}

// True when the fragment asks for the debug log overlay (#debug=1).
export function wantsDebug(hash) {
  const m = splitFragment(hash);
  return !!m && m.get('debug') === '1';
}

// Same game instance and same QR run: h, p, u and t equal.
export function sameJoin(a, b) {
  return !!a && !!b && a.h === b.h && Number(a.p) === Number(b.p) && a.u === b.u && a.t === b.t;
}
