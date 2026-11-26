import test from 'node:test';
import assert from 'node:assert/strict';
import { createRtcTransport } from '../../js/net/transport.js';

// A peer connection whose createOffer waits until the test lets it go.
function installFakeRtc() {
  let release;
  const gate = new Promise((r) => { release = r; });
  class FakePc {
    createDataChannel() { return { readyState: 'connecting', close() {} }; }
    async createOffer() { await gate; return { type: 'offer', sdp: 'v=0' }; }
    async setLocalDescription() {}
    async setRemoteDescription() {}
    close() {}
  }
  const prev = globalThis.RTCPeerConnection;
  globalThis.RTCPeerConnection = FakePc;
  return { release, restore: () => { globalThis.RTCPeerConnection = prev; } };
}

test('closing while the offer is still being made leaves no unhandled rejection', async () => {
  const rtc = installFakeRtc();
  const unhandled = [];
  const onUnhandled = (e) => unhandled.push(e);
  process.on('unhandledRejection', onUnhandled);
  try {
    const t = createRtcTransport({ h: '127.0.0.1', p: 1, u: 'u', w: 'w', f: 'AA', t: 't' });
    const opening = t.open();
    opening.catch(() => {});
    await new Promise((r) => setTimeout(r, 0));
    t.close(); // the link's open timeout fires before the offer is done
    await new Promise((r) => setTimeout(r, 10));
    assert.deepEqual(unhandled.map((e) => e && e.message), []);
  } finally {
    process.off('unhandledRejection', onUnhandled);
    rtc.restore();
  }
});
