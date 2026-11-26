// An image from game.resources by GUID, with a placeholder that fills in when the
// resource arrives (resources 'added' event). Browser only.

export function createResourceImage(ctx, guid, { cls = '', alt = '' } = {}) {
  const { h, game } = ctx;
  const el = h('div', { class: `play-resimg is-loading ${cls}`.trim() });
  let current = null;
  let off = null;
  let img = null;

  function lookup(g) {
    try {
      return g && game && game.resources ? game.resources.url(g) : null;
    } catch {
      return null;
    }
  }

  function show(url) {
    if (!img) {
      img = h('img', { attrs: { alt, draggable: 'false', decoding: 'async' } });
      img.addEventListener('error', () => { el.classList.add('is-broken'); });
      el.appendChild(img);
    }
    img.src = url;
    el.classList.remove('is-loading', 'is-broken');
  }

  function setGuid(g) {
    current = g ? String(g) : null;
    if (off) { off(); off = null; }
    if (img) { img.remove(); img = null; }
    el.classList.add('is-loading');
    el.classList.toggle('is-empty', !current);
    if (!current) return;
    const url = lookup(current);
    if (url) { show(url); return; }
    if (game && game.resources && typeof game.resources.on === 'function') {
      off = game.resources.on('added', (added) => {
        if (added !== current) return;
        const u = lookup(current);
        if (u) {
          show(u);
          if (off) { off(); off = null; }
        }
      });
    }
  }

  setGuid(guid);
  return {
    el,
    get guid() { return current; },
    setGuid,
    destroy() { if (off) { off(); off = null; } },
  };
}
