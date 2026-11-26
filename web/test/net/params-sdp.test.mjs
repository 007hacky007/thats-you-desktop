import { test } from 'node:test';
import assert from 'node:assert/strict';

import { parseJoinParams, isIPv4, sameJoin, wantsDebug } from '../../js/net/params.js';
import { buildAnswer, fingerprintHex, extractMid, base64UrlDecode } from '../../js/net/sdp.js';

// 32 bytes 0x00..0x1f in base64url without padding.
const FP = 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8';
const GOOD = '#h=192.168.1.23&p=47000&u=AbCd1234&w=abcdefghijklmnopqrstuvwx&f=' + FP + '&t=TOKEN0123456789';

test('parseJoinParams accepts the QR fragment', () => {
  assert.deepEqual(parseJoinParams(GOOD), {
    h: '192.168.1.23', p: 47000, u: 'AbCd1234', w: 'abcdefghijklmnopqrstuvwx', f: FP, t: 'TOKEN0123456789',
  });
  // without the leading '#', with a full URL, with extra keys
  assert.equal(parseJoinParams(GOOD.slice(1)).p, 47000);
  assert.equal(parseJoinParams('https://example.invalid/iwyk/' + GOOD + '&debug=1').h, '192.168.1.23');
});

test('parseJoinParams rejects every invalid field', () => {
  const bad = [
    ['h', '192.168.1'], ['h', '256.1.1.1'], ['h', '01.2.3.4'], ['h', 'example.invalid'], ['h', ''],
    ['p', '0'], ['p', '65536'], ['p', '-1'], ['p', '47a'], ['p', ''],
    ['u', ''], ['u', 'a b'], ['w', ''], ['w', 'x\ny'], ['t', ''], ['t', 'a b'],
    ['f', FP.slice(1)], ['f', FP + 'A'], ['f', FP.slice(0, 42) + '='], ['f', FP.slice(0, 42) + '+'],
  ];
  for (const [k, v] of bad) {
    const frag = GOOD.replace(new RegExp('([#&]' + k + '=)[^&]*'), (m, pre) => pre + encodeURIComponent(v));
    assert.equal(parseJoinParams(frag), null, k + '=' + JSON.stringify(v));
  }
  for (const k of ['h', 'p', 'u', 'w', 'f', 't']) {
    const frag = GOOD.replace(new RegExp('[#&]' + k + '=[^&]*'), '');
    assert.equal(parseJoinParams('#' + frag.replace(/^#?&?/, '')), null, 'missing ' + k);
  }
  assert.equal(parseJoinParams(''), null);
  assert.equal(parseJoinParams(null), null);
  assert.equal(parseJoinParams('#h=%E0%A4%A'), null);
});

test('isIPv4, sameJoin, wantsDebug', () => {
  assert.ok(isIPv4('10.0.0.1'));
  assert.ok(isIPv4('255.255.255.255'));
  assert.ok(!isIPv4('1.2.3.4.5'));
  const a = parseJoinParams(GOOD);
  assert.ok(sameJoin(a, { ...a, w: 'other', f: 'other' }));
  assert.ok(sameJoin(a, { ...a, p: '47000' }));
  assert.ok(!sameJoin(a, { ...a, t: 'x' }));
  assert.ok(!sameJoin(a, null));
  assert.ok(wantsDebug('#debug=1&h=1'));
  assert.ok(!wantsDebug(GOOD));
});

test('fingerprintHex decodes base64url to uppercase hex pairs', () => {
  const hex = fingerprintHex(FP);
  assert.equal(hex, Array.from({ length: 32 }, (_, i) => i.toString(16).toUpperCase().padStart(2, '0')).join(':'));
  assert.equal(fingerprintHex('-_' + 'A'.repeat(41)).slice(0, 5), 'FB:F0');
  assert.equal(fingerprintHex('AAAA'), null);
  assert.equal(fingerprintHex('*'.repeat(43)), null);
  assert.deepEqual([...base64UrlDecode('_w')], [255]);
  assert.equal(base64UrlDecode('A'), null);
});

const CHROME_OFFER = [
  'v=0', 'o=- 4611 2 IN IP4 127.0.0.1', 's=-', 't=0 0', 'a=group:BUNDLE 0', 'a=extmap-allow-mixed',
  'm=application 9 UDP/DTLS/SCTP webrtc-datachannel', 'c=IN IP4 0.0.0.0', 'a=ice-ufrag:x', 'a=ice-pwd:y',
  'a=mid:0', 'a=sctp-port:5000', '',
].join('\r\n');

test('extractMid finds the application m-line mid', () => {
  assert.equal(extractMid(CHROME_OFFER), '0');
  const multi = 'v=0\nm=audio 9 RTP 0\na=mid:audio\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\na=mid:data\n';
  assert.equal(extractMid(multi), 'data');
  assert.equal(extractMid('v=0\r\n'), '0');
  assert.equal(extractMid(''), '0');
});

test('buildAnswer produces the exact SDP of contract 2.5', () => {
  const params = parseJoinParams(GOOD);
  const sdp = buildAnswer(params, CHROME_OFFER.replace('a=mid:0', 'a=mid:dc'));
  const expected = [
    'v=0',
    'o=- 0 0 IN IP4 192.168.1.23',
    's=-',
    't=0 0',
    'a=ice-lite',
    'a=group:BUNDLE dc',
    'm=application 47000 UDP/DTLS/SCTP webrtc-datachannel',
    'c=IN IP4 192.168.1.23',
    'a=mid:dc',
    'a=ice-ufrag:AbCd1234',
    'a=ice-pwd:abcdefghijklmnopqrstuvwx',
    'a=fingerprint:sha-256 ' + fingerprintHex(FP),
    'a=setup:passive',
    'a=sctp-port:5000',
    'a=max-message-size:262144',
    'a=candidate:1 1 udp 2130706431 192.168.1.23 47000 typ host',
    'a=end-of-candidates',
  ].join('\r\n') + '\r\n';
  assert.equal(sdp, expected);
  assert.throws(() => buildAnswer({ ...params, f: 'AAAA' }, CHROME_OFFER));
});
