import { test } from 'node:test';
import assert from 'node:assert/strict';

import { detectDevice, deviceInfoFields } from '../../js/game/device.js';
import { createProfile, bytesToBase64, base64ToBytes } from '../../js/game/profile.js';
import { clampText, isCardId } from '../../js/game/state.js';
import { createStorage, createMemoryBackend } from '../../js/util/storage.js';
import { createFakeClock } from '../../js/util/clock.js';

const UA = {
  iphone: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1',
  ipad: 'Mozilla/5.0 (iPad; CPU OS 17_5_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1',
  ipadDesktop: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.2 Safari/605.1.15',
  android: 'Mozilla/5.0 (Linux; Android 15; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
  winphone: 'Mozilla/5.0 (Windows Phone 10.0; Android 6.0.1; Microsoft; Lumia 950) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/52.0 Mobile Safari/537.36 Edge/15.14977',
  windows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
  mac: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
  linux: 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
};

test('DeviceOS, model, type and OS name from the user agent', () => {
  const cases = [
    [{ ua: UA.iphone }, 2, 'iPhone', 'Handheld', 'iOS 18.1'],
    [{ ua: UA.ipad }, 2, 'iPad', 'Handheld', 'iOS 17.5.1'],
    [{ ua: UA.ipadDesktop, maxTouchPoints: 5 }, 2, 'iPad', 'Handheld', 'iOS 18.2'],
    [{ ua: UA.ipadDesktop, maxTouchPoints: 0 }, 4, 'Desktop', 'Desktop', 'Web'],
    [{ ua: UA.android }, 1, 'Android', 'Handheld', 'Android 15'],
    [{ ua: UA.winphone }, 6, 'Desktop', 'Handheld', 'Web'],
    [{ ua: UA.windows }, 3, 'Desktop', 'Desktop', 'Web'],
    [{ ua: UA.mac }, 4, 'Desktop', 'Desktop', 'Web'],
    [{ ua: UA.linux }, 5, 'Desktop', 'Desktop', 'Web'],
    [{ ua: 'curl/8' }, 5, 'Desktop', 'Desktop', 'Web'],
    [{}, 5, 'Desktop', 'Desktop', 'Web'],
  ];
  for (const [env, os, model, type, osName] of cases) {
    const d = detectDevice(env);
    assert.deepEqual([d.DeviceOS, d.DeviceModel, d.DeviceType, d.DeviceOperatingSystem], [os, model, type, osName], env.ua);
  }
});

test('DeviceSize by the smaller screen side', () => {
  assert.equal(detectDevice({ screenWidth: 390, screenHeight: 844 }).DeviceSize, 1);
  assert.equal(detectDevice({ screenWidth: 1024, screenHeight: 599 }).DeviceSize, 1);
  assert.equal(detectDevice({ screenWidth: 820, screenHeight: 1180 }).DeviceSize, 2);
  assert.equal(detectDevice({ screenWidth: 1920, screenHeight: 1080 }).DeviceSize, 3);
  assert.equal(detectDevice({ screenWidth: 900, screenHeight: 900 }).DeviceSize, 3);
  assert.equal(detectDevice({}).DeviceSize, 1);
});

test('deviceInfoFields has the field order of DeviceInfoMessage', () => {
  const f = deviceInfoFields({ ua: UA.iphone, screenWidth: 390, screenHeight: 844 }, 'uid-9');
  assert.equal(JSON.stringify(f),
    '{"DeviceSize":1,"DeviceOS":2,"DeviceModel":"iPhone","DeviceType":"Handheld","DeviceUID":"uid-9","DeviceOperatingSystem":"iOS 18.1"}');
});

test('base64 round trip', () => {
  for (let n = 0; n < 8; n++) {
    const b = Uint8Array.from({ length: n }, (_, i) => (i * 97 + 3) & 0xff);
    const s = bytesToBase64(b);
    assert.equal(s, Buffer.from(b).toString('base64'));
    assert.deepEqual(base64ToBytes(s), b);
  }
  assert.equal(base64ToBytes('*'), null);
});

function mk() {
  const backend = createMemoryBackend();
  const storage = createStorage(backend);
  const clock = createFakeClock(5000);
  let n = 0;
  const profile = createProfile({ storage, clock, uuid: () => 'id-' + (++n) });
  return { backend, storage, clock, profile };
}

test('uid is created once and kept', () => {
  const { backend, profile } = mk();
  assert.equal(profile.uid(), 'id-1');
  assert.equal(profile.uid(), 'id-1');
  assert.equal(backend.getItem('iwyk.uid'), 'id-1');
  backend.setItem('iwyk.uid', 'bad\nuid');
  assert.equal(profile.uid(), 'id-2');
});

test('lastJoin save, activate, touch', () => {
  const { clock, profile, backend } = mk();
  assert.equal(profile.lastJoin(), null);
  profile.saveLastJoin({ h: '1.2.3.4', p: 47000, u: 'u', w: 'w', f: 'f', t: 't' }, false);
  assert.deepEqual(JSON.parse(backend.getItem('iwyk.lastJoin')),
    { h: '1.2.3.4', p: 47000, u: 'u', w: 'w', f: 'f', t: 't', at: 5000, active: false });
  clock.advance(100);
  profile.touchJoin(); // inactive: unchanged
  assert.equal(profile.lastJoin().at, 5000);
  profile.setActive(true);
  assert.equal(profile.lastJoin().at, 5100);
  clock.advance(100);
  profile.touchJoin();
  assert.equal(profile.lastJoin().at, 5200);
  profile.setActive(false);
  assert.equal(profile.lastJoin().active, false);
  backend.setItem('iwyk.lastJoin', '{"h":1}');
  assert.equal(profile.lastJoin(), null);
});

test('session restore keeps or clears the saved profile', () => {
  const { profile, backend } = mk();
  const jpeg = new Uint8Array([0xff, 0xd8, 1, 2, 0xff, 0xd9]);
  assert.equal(profile.applySession(42), false);
  assert.equal(backend.getItem('iwyk.session'), '42');
  profile.saveProfile({ name: 'Ann', cardId: 'CARD_2', isGuest: true, portraitId: 'pg', jpeg });
  profile.setPlayerId(3);
  assert.equal(profile.applySession(42), true);
  let s = profile.saved();
  assert.deepEqual({ ...s, portrait: [...s.portrait] },
    { name: 'Ann', cardId: 'CARD_2', isGuest: true, playerId: 3, portraitId: 'pg', portrait: [...jpeg], profileDone: true });
  assert.equal(profile.applySession(43), false);
  s = profile.saved();
  assert.deepEqual(s, { name: 'Ann', cardId: 'CARD_2', isGuest: true, playerId: -1, portraitId: '', portrait: null, profileDone: false });
  assert.equal(backend.getItem('iwyk.session'), '43');
});

test('clampText counts code points; card ids', () => {
  assert.equal(clampText('abcdefghijkl', 10), 'abcdefghij');
  assert.equal(clampText('\u{1F600}'.repeat(12), 10), '\u{1F600}'.repeat(10));
  assert.equal(clampText(null, 3), '');
  assert.ok(isCardId('CARD_12'));
  assert.ok(!isCardId('CARD_13'));
  assert.ok(!isCardId('CARD_0'));
});
