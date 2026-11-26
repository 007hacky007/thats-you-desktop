// Answer SDP synthesis for WebRTC Direct (contract 2.5).

const B64URL = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';

// base64url (no padding required) -> Uint8Array; null on bad input.
export function base64UrlDecode(s) {
  if (typeof s !== 'string') return null;
  const clean = s.replace(/=+$/, '');
  if (clean.length % 4 === 1) return null;
  const out = [];
  let acc = 0;
  let bits = 0;
  for (const ch of clean) {
    const v = B64URL.indexOf(ch);
    if (v < 0) return null;
    acc = (acc << 6) | v;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      out.push((acc >> bits) & 0xff);
    }
  }
  return Uint8Array.from(out);
}

// 43-char base64url sha-256 -> "AB:CD:..." (32 uppercase pairs); null if not 32 bytes.
export function fingerprintHex(b64url) {
  const bytes = base64UrlDecode(b64url);
  if (!bytes || bytes.length !== 32) return null;
  return Array.from(bytes, (b) => b.toString(16).toUpperCase().padStart(2, '0')).join(':');
}

// The a=mid of the application m-line of an offer; "0" when not found.
export function extractMid(offerSdp) {
  const lines = String(offerSdp || '').split(/\r?\n/);
  let inApp = false;
  for (const line of lines) {
    if (line.startsWith('m=')) {
      inApp = line.startsWith('m=application ');
      continue;
    }
    if (inApp && line.startsWith('a=mid:')) {
      const mid = line.slice(6).trim();
      if (mid && /^[\x21-\x7e]+$/.test(mid)) return mid;
    }
  }
  return '0';
}

export function buildAnswer(params, offerSdp) {
  const { h, p, u, w, f } = params;
  const fp = fingerprintHex(f);
  if (!fp) throw new Error('bad fingerprint');
  const mid = extractMid(offerSdp);
  const lines = [
    'v=0',
    'o=- 0 0 IN IP4 ' + h,
    's=-',
    't=0 0',
    'a=ice-lite',
    'a=group:BUNDLE ' + mid,
    'm=application ' + p + ' UDP/DTLS/SCTP webrtc-datachannel',
    'c=IN IP4 ' + h,
    'a=mid:' + mid,
    'a=ice-ufrag:' + u,
    'a=ice-pwd:' + w,
    'a=fingerprint:sha-256 ' + fp,
    'a=setup:passive',
    'a=sctp-port:5000',
    'a=max-message-size:262144',
    'a=candidate:1 1 udp 2130706431 ' + h + ' ' + p + ' typ host',
    'a=end-of-candidates',
  ];
  return lines.join('\r\n') + '\r\n';
}
