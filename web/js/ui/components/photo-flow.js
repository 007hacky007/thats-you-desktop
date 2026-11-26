// Photo flow state machine shared by the selfie, photo and mimic screens.
// States: live -> capturing -> captured -> used; retake: captured -> live
// (or back to nocamera after a placeholder). No DOM, imports in Node.

const ACTIONS = {
  selfie: 'profilePhoto',
  topdrawer: 'submitPhoto',
  quickdraw: 'submitPhoto',
  mimic: 'submitPhoto',
};

export function photoAction(mode) {
  const a = ACTIONS[mode];
  if (!a) throw new Error(`unknown photo mode ${mode}`);
  return a;
}

function validShot(r) {
  return !!(r && r.jpeg && r.jpeg.length > 0);
}

// capture() -> Promise<{ jpeg, usesFX }>; actions = game.actions
export function createPhotoFlow({ mode, actions, capture, onError }) {
  const actionName = photoAction(mode);
  const listeners = new Set();
  let destroyed = false;
  let photoTakenSent = false;

  const flow = {
    mode,
    state: 'live',
    still: null,
    placeholder: false,
    on(fn) { listeners.add(fn); return () => listeners.delete(fn); },

    async shutter() {
      if (destroyed || flow.state !== 'live') return false;
      sendPhotoTaken();
      set('capturing');
      let shot;
      try {
        shot = await capture();
      } catch (e) {
        if (destroyed) return false;
        if (onError) onError(e);
        set('live');
        return false;
      }
      if (destroyed) return false;
      if (!validShot(shot)) {
        if (onError) onError(new Error('capture returned no image'));
        set('live');
        return false;
      }
      flow.still = { jpeg: shot.jpeg, usesFX: !!shot.usesFX };
      flow.placeholder = false;
      set('captured');
      return true;
    },

    retake() {
      if (destroyed || flow.state !== 'captured') return false;
      const back = flow.placeholder ? 'nocamera' : 'live';
      flow.still = null;
      flow.placeholder = false;
      set(back);
      return true;
    },

    use() {
      if (destroyed || flow.state !== 'captured' || !flow.still) return false;
      set('used');
      actions[actionName]({ jpeg: flow.still.jpeg, usesFX: flow.still.usesFX });
      return true;
    },

    noCamera() {
      if (destroyed || flow.state === 'used') return;
      flow.still = null;
      flow.placeholder = false;
      set('nocamera');
    },

    usePlaceholder(jpeg) {
      if (destroyed || !jpeg || !jpeg.length) return false;
      if (flow.state !== 'nocamera' && flow.state !== 'live') return false;
      sendPhotoTaken();
      flow.still = { jpeg, usesFX: false };
      flow.placeholder = true;
      set('captured');
      return true;
    },

    // draft provider body: the captured (or used) still, else a capture of the live view
    async draft(kind) {
      if (kind !== 'photo' || destroyed) return null;
      if ((flow.state === 'captured' || flow.state === 'used') && flow.still) {
        return { jpeg: flow.still.jpeg, usesFX: flow.still.usesFX };
      }
      if (flow.state !== 'live' && flow.state !== 'capturing') return null;
      try {
        const shot = await capture();
        return validShot(shot) ? { jpeg: shot.jpeg, usesFX: !!shot.usesFX } : null;
      } catch (e) {
        if (onError) onError(e);
        return null;
      }
    },

    controls() {
      const s = flow.state;
      return {
        shutter: s === 'live',
        effects: s === 'live',
        flip: s === 'live',
        retake: s === 'captured',
        use: s === 'captured',
        noCamera: s === 'nocamera',
      };
    },

    destroy() {
      destroyed = true;
      listeners.clear();
    },
  };

  // Top Drawer: TV flash once, at the first shutter press or placeholder use
  function sendPhotoTaken() {
    if (mode !== 'topdrawer' || photoTakenSent) return;
    photoTakenSent = true;
    actions.photoTaken();
  }

  function set(state) {
    flow.state = state;
    for (const fn of [...listeners]) fn(state, flow);
  }

  return flow;
}
