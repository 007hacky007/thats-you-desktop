// Fake data channel transport for tests (same surface as net/transport.js).

export function createFakeTransportFactory({ autoOpen = false } = {}) {
  const created = [];
  const factory = (params, opts) => {
    const t = createFakeTransport(params, opts);
    created.push(t);
    if (autoOpen) t.accept();
    if (typeof factory.onCreate === 'function') factory.onCreate(t);
    return t;
  };
  factory.created = created;
  factory.last = () => created[created.length - 1];
  factory.onCreate = null;
  return factory;
}

export function createFakeTransport(params) {
  let resolveOpen = null;
  let rejectOpen = null;
  let pendingOpen = null; // 'accept' | Error queued before open() was called
  const t = {
    params,
    sent: [],
    bufferedAmount: 0,
    closed: false,
    opened: false,
    onText: null,
    onBinary: null,
    onClose: null,
    open() {
      return new Promise((res, rej) => {
        resolveOpen = res;
        rejectOpen = rej;
        if (pendingOpen === 'accept') t.accept();
        else if (pendingOpen) t.fail(pendingOpen);
      });
    },
    sendText(s) {
      if (t.closed) return false;
      t.sent.push(s);
      return true;
    },
    sendBinary(b) {
      if (t.closed) return false;
      t.sent.push(new Uint8Array(b));
      return true;
    },
    close() {
      t.closed = true;
    },
    // ---- test controls ----
    accept() {
      if (!resolveOpen) {
        pendingOpen = 'accept';
        return;
      }
      t.opened = true;
      resolveOpen();
    },
    fail(err = new Error('ice failed')) {
      if (!rejectOpen) {
        pendingOpen = err;
        return;
      }
      rejectOpen(err);
    },
    recvText(s) {
      if (t.onText) t.onText(typeof s === 'string' ? s : JSON.stringify(s));
    },
    recvBinary(b) {
      if (t.onBinary) t.onBinary(b);
    },
    remoteClose() {
      if (t.onClose) t.onClose();
    },
    texts() {
      return t.sent.filter((x) => typeof x === 'string');
    },
    frames() {
      return t.texts().map((s) => JSON.parse(s));
    },
    // game messages sent (the json of msg frames), optionally only new ones
    msgs() {
      return t.frames().filter((f) => f.t === 'msg').map((f) => f.json);
    },
    types() {
      return t.msgs().map((m) => m.TypeString);
    },
    binaries() {
      return t.sent.filter((x) => typeof x !== 'string');
    },
  };
  return t;
}
