// One connection attempt over the data channel: hello handshake, liveness,
// the message envelope and content chunking (contract 2.1 - 2.3).
//
// A link is single use. It always ends with exactly one 'end' event:
//   { cause, reason, wasJoined }
// cause: 'open-timeout' | 'open-failed' | 'hello-timeout' | 'pending-timeout'
//        | 'refused' (reason: number) | 'bye' (reason: string)
//        | 'transport' | 'death' | 'local'

import { createEmitter } from '../util/emitter.js';
import { nullLog } from '../util/log.js';
import {
  helloFrame, pingFrame, msgFrame, parseTextFrame, encodeChunks, createReassembler, utf8Length, MAX_TEXT,
} from './frames.js';

export const LINK_TIMING = Object.freeze({
  openTimeout: 17000,
  helloTimeout: 17000,
  pendingTimeout: 30000,
  ping: 2000,
  death: 17000,
  highWater: 1 << 20,
  drainPoll: 20,
});

export function createLink({ params, uid, transportFactory, clock, log = nullLog, timing = LINK_TIMING }) {
  const em = createEmitter({ onError: (e) => log.error('link listener: ' + (e && e.message)) });
  let state = 'new';
  let transport = null;
  let ended = false;
  let wasJoined = false;
  let lastRx = 0;
  const timers = { open: null, hello: null, pending: null, death: null, ping: null, pump: null };
  const queue = [];
  let pumping = false;
  const reasm = createReassembler({ log });

  function setState(s) {
    if (state === s) return;
    state = s;
    em.emit('state', s);
  }

  function clearTimer(name) {
    if (timers[name] === null) return;
    if (name === 'ping') clock.clearInterval(timers[name]);
    else clock.clearTimeout(timers[name]);
    timers[name] = null;
  }

  function startTimer(name, ms, fn) {
    clearTimer(name);
    timers[name] = clock.setTimeout(() => {
      timers[name] = null;
      fn();
    }, ms);
  }

  function end(cause, reason) {
    if (ended) return;
    ended = true;
    for (const k of Object.keys(timers)) clearTimer(k);
    for (const item of queue.splice(0)) if (item.resolve) item.resolve(false);
    reasm.reset();
    const tr = transport;
    if (tr) {
      tr.onText = null;
      tr.onBinary = null;
      tr.onClose = null;
      try {
        tr.close();
      } catch (e) {
        log.debug('transport close: ' + e.message);
      }
    }
    setState('closed');
    log.info('link end: ' + cause + (reason !== undefined ? ' ' + reason : ''));
    em.emit('end', { cause, reason, wasJoined });
  }

  function touch() {
    lastRx = clock.now();
    startTimer('death', timing.death, () => end('death'));
  }

  // ---- outgoing queue: one writer, binary transfers never interleaved ----

  function pump() {
    if (pumping || timers.pump !== null || ended) return;
    pumping = true;
    try {
      while (queue.length && !ended) {
        const item = queue[0];
        if (item.kind === 'text') {
          if (!transport.sendText(item.text)) log.warn('send text failed');
          queue.shift();
          continue;
        }
        while (item.i < item.chunks.length) {
          if (transport.bufferedAmount > timing.highWater) {
            timers.pump = clock.setTimeout(() => {
              timers.pump = null;
              pump();
            }, timing.drainPoll);
            return;
          }
          if (!transport.sendBinary(item.chunks[item.i])) log.warn('send chunk failed');
          item.i++;
        }
        queue.shift();
        item.resolve(true);
      }
    } finally {
      pumping = false;
    }
  }

  function enqueueText(text) {
    queue.push({ kind: 'text', text });
    pump();
  }

  // ---- incoming ----

  function onText(text) {
    if (ended) return;
    touch();
    const fr = parseTextFrame(text);
    if (!fr) {
      log.debug('dropped malformed text frame');
      return;
    }
    switch (fr.t) {
      case 'ping':
        return;
      case 'joined':
        if (state !== 'hello' && state !== 'pending') return;
        clearTimer('hello');
        clearTimer('pending');
        wasJoined = true;
        setState('joined');
        em.emit('joined');
        return;
      case 'pending':
        if (state !== 'hello') return;
        clearTimer('hello');
        setState('pending');
        startTimer('pending', timing.pendingTimeout, () => end('pending-timeout'));
        em.emit('pending');
        return;
      case 'refused':
        end('refused', fr.reason);
        return;
      case 'bye':
        end('bye', fr.reason);
        return;
      case 'msg':
        if (state !== 'joined') {
          log.debug('msg before joined ignored');
          return;
        }
        em.emit('msg', fr.json);
        return;
      default:
        log.debug('unknown frame type ' + fr.t);
    }
  }

  function onBinary(bytes) {
    if (ended) return;
    touch();
    if (state !== 'joined') {
      log.debug('binary before joined ignored');
      return;
    }
    const r = reasm.push(bytes);
    if (r.done) em.emit('content', r.transferId, r.data);
  }

  function onOpen() {
    if (ended) return;
    clearTimer('open');
    setState('hello');
    transport.sendText(helloFrame(params.t, uid));
    touch();
    startTimer('hello', timing.helloTimeout, () => end('hello-timeout'));
    timers.ping = clock.setInterval(() => {
      if (!queue.some((q) => q.kind === 'text' && q.ping)) {
        queue.push({ kind: 'text', text: pingFrame(), ping: true });
        pump();
      }
    }, timing.ping);
    em.emit('open');
  }

  // ---- API ----

  function connect() {
    if (state !== 'new') throw new Error('link already used');
    setState('opening');
    try {
      transport = transportFactory(params, { log });
    } catch (e) {
      log.warn('transport create failed: ' + e.message);
      end('open-failed');
      return;
    }
    transport.onText = onText;
    transport.onBinary = onBinary;
    transport.onClose = () => end('transport');
    startTimer('open', timing.openTimeout, () => end('open-timeout'));
    let p;
    try {
      p = Promise.resolve(transport.open());
    } catch (e) {
      p = Promise.reject(e);
    }
    p.then(onOpen, (e) => {
      log.warn('open failed: ' + (e && e.message));
      end('open-failed');
    });
  }

  function sendMsg(obj) {
    if (state !== 'joined' || ended) return false;
    const text = msgFrame(obj);
    if (text.length > MAX_TEXT / 4 && utf8Length(text) > MAX_TEXT) {
      log.error('message too big: ' + obj.TypeString);
      return false;
    }
    enqueueText(text);
    return true;
  }

  function sendContent(transferId, bytes) {
    if (state !== 'joined' || ended) return Promise.resolve(false);
    let chunks;
    try {
      chunks = encodeChunks(transferId, bytes);
    } catch (e) {
      log.error('content rejected: ' + e.message);
      return Promise.resolve(false);
    }
    return new Promise((resolve) => {
      queue.push({ kind: 'bin', chunks, i: 0, resolve });
      pump();
    });
  }

  // Resolves true once everything queued is on the wire (bufferedAmount 0),
  // false after maxMs or when the link ended.
  function drain(maxMs = 500) {
    return new Promise((resolve) => {
      const deadline = clock.now() + maxMs;
      const check = () => {
        if (ended || !transport) return resolve(false);
        if (queue.length === 0 && transport.bufferedAmount === 0) return resolve(true);
        if (clock.now() >= deadline) return resolve(false);
        clock.setTimeout(check, timing.drainPoll);
      };
      check();
    });
  }

  return {
    get state() { return state; },
    get ended() { return ended; },
    get wasJoined() { return wasJoined; },
    lastRxAgo: () => (state === 'joined' ? Math.max(0, clock.now() - lastRx) : 0),
    silentFor: () => (lastRx ? Math.max(0, clock.now() - lastRx) : 0),
    connect,
    sendMsg,
    sendContent,
    drain,
    close: (cause = 'local') => end(cause),
    on: (ev, fn) => em.on(ev, fn),
  };
}
