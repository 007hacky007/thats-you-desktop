// One shared camera (getUserMedia) with front/back switching and a delayed
// release. Everything browser-related is injectable so this runs in Node.

export const RELEASE_MS = 10000;

export function cameraConstraints(facing) {
  return {
    video: {
      facingMode: { ideal: facing },
      width: { ideal: 640 },
      height: { ideal: 480 },
      frameRate: { ideal: facing === 'environment' ? 30 : 15 },
    },
    audio: false,
  };
}

function namedError(name, message) {
  const e = new Error(message);
  e.name = name;
  return e;
}

function stopStream(stream) {
  if (!stream) return;
  for (const t of stream.getTracks()) {
    try { t.stop(); } catch { /* already stopped */ }
  }
}

export function createCamera({
  mediaDevices = globalThis.navigator?.mediaDevices,
  document = globalThis.document,
  clock = globalThis,
  log = null,
  releaseMs = RELEASE_MS,
} = {}) {
  const supported = !!(mediaDevices && typeof mediaDevices.getUserMedia === 'function');
  let video = null;
  let stream = null;
  let facing = 'user';
  let active = false;
  let canFlip = false;
  let releaseTimer = null;
  let gen = 0; // bumped by stopNow so in-flight starts give up
  let chain = Promise.resolve();

  function ensureVideo() {
    if (video || !document) return video;
    video = document.createElement('video');
    video.muted = true;
    video.playsInline = true;
    video.autoplay = true;
    video.setAttribute('playsinline', '');
    video.setAttribute('muted', '');
    video.setAttribute('autoplay', '');
    video.setAttribute('aria-hidden', 'true');
    // hidden but rendered: some browsers do not decode frames of display:none videos
    Object.assign(video.style, {
      position: 'fixed', left: '0', top: '0', width: '1px', height: '1px',
      opacity: '0', pointerEvents: 'none', zIndex: '-1',
    });
    document.body?.appendChild(video);
    return video;
  }

  function cancelRelease() {
    if (releaseTimer !== null) {
      clock.clearTimeout(releaseTimer);
      releaseTimer = null;
    }
  }

  function detach() {
    stopStream(stream);
    stream = null;
    active = false;
    if (video) video.srcObject = null;
  }

  async function open(want) {
    const attempts = [
      cameraConstraints(want),
      { video: { facingMode: { ideal: want } }, audio: false },
      { video: true, audio: false },
    ];
    let lastErr = null;
    for (const c of attempts) {
      try {
        return await mediaDevices.getUserMedia(c);
      } catch (err) {
        lastErr = err;
        if (!err || err.name !== 'OverconstrainedError') throw err;
        log?.warn?.(`camera constraints refused (${err.constraint || '?'}), retrying simpler`);
      }
    }
    throw lastErr;
  }

  async function doStart(want, myGen) {
    if (!supported) throw namedError('NotSupportedError', 'camera not available in this browser');
    if (myGen !== gen) throw namedError('AbortError', 'camera stopped before starting');
    if (active && stream && facing === want) return;
    detach();
    const s = await open(want);
    if (myGen !== gen) {
      stopStream(s);
      throw namedError('AbortError', 'camera stopped while starting');
    }
    stream = s;
    const track = s.getVideoTracks ? s.getVideoTracks()[0] : s.getTracks()[0];
    const reported = track && track.getSettings ? track.getSettings().facingMode : undefined;
    facing = reported === 'user' || reported === 'environment' ? reported : want;
    track?.addEventListener?.('ended', () => {
      if (stream === s) {
        log?.warn?.('camera track ended');
        detach();
      }
    });
    active = true;
    const v = ensureVideo();
    if (v) {
      v.srcObject = s;
      try { await v.play(); } catch (err) { log?.warn?.(`video play: ${err && err.message}`); }
    }
    try {
      const devices = await mediaDevices.enumerateDevices();
      canFlip = devices.filter((d) => d.kind === 'videoinput').length > 1;
    } catch {
      canFlip = false;
    }
  }

  function enqueue(fn) {
    const p = chain.then(fn, fn);
    chain = p.catch(() => {});
    return p;
  }

  return {
    get supported() { return supported; },
    get facing() { return facing; },
    get active() { return active; },
    get canFlip() { return canFlip; },
    get video() { return ensureVideo(); },
    get stream() { return stream; },

    start(want = 'user') {
      if (want !== 'user' && want !== 'environment') want = 'user';
      cancelRelease();
      const g = gen;
      return enqueue(() => doStart(want, g));
    },

    flip() {
      cancelRelease();
      const g = gen;
      return enqueue(() => doStart(facing === 'user' ? 'environment' : 'user', g));
    },

    release() {
      cancelRelease();
      if (!active) return;
      releaseTimer = clock.setTimeout(() => {
        releaseTimer = null;
        gen++;
        detach();
      }, releaseMs);
    },

    stopNow() {
      cancelRelease();
      gen++;
      detach();
    },

    async requestPermission() {
      if (!supported) return false;
      if (active) return true;
      try {
        const s = await mediaDevices.getUserMedia({ video: true, audio: false });
        stopStream(s);
        return true;
      } catch (err) {
        log?.info?.(`camera permission: ${err && err.name}`);
        return false;
      }
    },
  };
}
