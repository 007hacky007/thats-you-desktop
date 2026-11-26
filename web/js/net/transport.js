// Browser-only WebRTC Direct transport (contract 2.5). Nothing here touches
// browser globals at import time, so the module still imports in Node.
// This is the only module of the logic layer that uses browser APIs.

import { buildAnswer } from './sdp.js';
import { nullLog } from '../util/log.js';

export function createRtcTransport(params, { log = nullLog } = {}) {
  let pc = null;
  let dc = null;
  let opened = false;
  let closed = false;
  let rejectOpen = null;

  const t = {
    onText: null,
    onBinary: null,
    onClose: null,
    get bufferedAmount() {
      try {
        return dc ? dc.bufferedAmount : 0;
      } catch {
        return 0;
      }
    },
    open,
    sendText,
    sendBinary,
    close,
  };

  function lost(why) {
    if (closed) return;
    if (!opened) {
      if (rejectOpen) rejectOpen(new Error(why));
      return;
    }
    closed = true;
    log.info('transport lost: ' + why);
    shutdown();
    if (t.onClose) t.onClose(why);
  }

  function shutdown() {
    try {
      if (dc) dc.close();
    } catch {
      // ignore
    }
    try {
      if (pc) pc.close();
    } catch {
      // ignore
    }
  }

  async function open() {
    const RTC = globalThis.RTCPeerConnection;
    if (typeof RTC !== 'function') throw new Error('WebRTC is not available');
    pc = new RTC({ iceServers: [] });
    dc = pc.createDataChannel('game', { negotiated: true, id: 0 });
    dc.binaryType = 'arraybuffer';
    const openPromise = new Promise((resolve, reject) => {
      rejectOpen = reject;
      dc.onopen = () => {
        if (closed) return;
        opened = true;
        rejectOpen = null;
        resolve();
      };
    });
    dc.onclose = () => lost('channel closed');
    dc.onerror = (e) => log.warn('channel error: ' + ((e && e.error && e.error.message) || 'unknown'));
    dc.onmessage = (e) => {
      if (closed) return;
      const d = e.data;
      if (typeof d === 'string') {
        if (t.onText) t.onText(d);
      } else if (d instanceof ArrayBuffer) {
        if (t.onBinary) t.onBinary(new Uint8Array(d));
      } else if (ArrayBuffer.isView(d)) {
        if (t.onBinary) t.onBinary(new Uint8Array(d.buffer, d.byteOffset, d.byteLength));
      } else {
        log.warn('unexpected data channel payload');
      }
    };
    pc.onconnectionstatechange = () => {
      const s = pc.connectionState;
      log.debug('pc ' + s);
      if (s === 'failed' || s === 'closed') lost('connection ' + s);
    };
    pc.oniceconnectionstatechange = () => log.debug('ice ' + pc.iceConnectionState);
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    await pc.setRemoteDescription({ type: 'answer', sdp: buildAnswer(params, offer.sdp) });
    await openPromise;
  }

  function sendText(s) {
    if (closed || !dc || dc.readyState !== 'open') return false;
    try {
      dc.send(s);
      return true;
    } catch (e) {
      log.warn('send failed: ' + e.message);
      return false;
    }
  }

  function sendBinary(bytes) {
    if (closed || !dc || dc.readyState !== 'open') return false;
    try {
      dc.send(bytes);
      return true;
    } catch (e) {
      log.warn('send failed: ' + e.message);
      return false;
    }
  }

  function close() {
    if (closed) return;
    closed = true;
    if (rejectOpen) rejectOpen(new Error('closed'));
    shutdown();
  }

  return t;
}

// Calls fn('visible' | 'pageshow') when the page comes back to the
// foreground. Returns an unsubscribe function. No-op outside a browser.
export function watchVisibility(fn) {
  const doc = globalThis.document;
  const win = globalThis.window;
  if (!doc || typeof doc.addEventListener !== 'function') return () => {};
  const onVis = () => {
    if (doc.visibilityState === 'visible') fn('visible');
  };
  const onShow = () => fn('pageshow');
  doc.addEventListener('visibilitychange', onVis);
  if (win && typeof win.addEventListener === 'function') win.addEventListener('pageshow', onShow);
  return () => {
    doc.removeEventListener('visibilitychange', onVis);
    if (win && typeof win.removeEventListener === 'function') win.removeEventListener('pageshow', onShow);
  };
}

// What device.js needs to know about this browser, read at call time.
export function browserEnvironment() {
  const nav = globalThis.navigator || {};
  const scr = globalThis.screen || {};
  return {
    ua: String(nav.userAgent || ''),
    maxTouchPoints: Number(nav.maxTouchPoints) || 0,
    screenWidth: Number(scr.width) || 0,
    screenHeight: Number(scr.height) || 0,
    language: String(nav.language || 'en-US'),
  };
}
