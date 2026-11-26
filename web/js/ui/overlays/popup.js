// Popups for refusals, disconnects and the page's own end states.
// data: { reason, reconnect }

export const REASONS = ['gamefull', 'version', 'onlyone', 'notallowed', 'failed', 'nogame', 'replaced', 'shutdown'];

// Localisation keys for one popup: { title, body, close, reconnect }.
export function popupContent(reason, reconnect) {
  switch (reason) {
    case 'gamefull':
      return { title: null, body: 'GameFullPopupMessage', close: 'GameFullPopupDismissButtonLabel', reconnect: null };
    case 'version':
      return { title: null, body: 'VersionDisparityPopupMessage', close: 'VersionDisparityPopupDismissButtonLabel', reconnect: null };
    case 'onlyone':
      return { title: null, body: 'OnlyOnePlayerOnlineMessage', close: 'OnlyOnePlayerOnlineButtonLabel', reconnect: null };
    case 'notallowed':
      return { title: null, body: 'GameStartedPopupMessage', close: 'GameStartedPopupDismissButtonLabel', reconnect: null };
    case 'nogame':
      return { title: 'ConnectionWarning_NoGameMessage', body: 'ConnectionWarning_NoGameInstruction', close: 'ConnectionWarning_CloseButtonLabel', reconnect: null };
    case 'replaced':
      return { title: null, body: 'port.replaced', close: 'port.close', reconnect: 'port.reconnect' };
    case 'shutdown':
      return { title: null, body: 'port.shutdown', close: 'port.ok', reconnect: null };
    case 'failed':
    default:
      if (reconnect) {
        return { title: 'DisconnectedPopup_Heading', body: 'DisconnectedPopup_Explanation', close: 'DisconnectedPopup_Close', reconnect: 'DisconnectedPopup_Reconnect' };
      }
      return { title: null, body: 'ConnectionFailedPopupMessage', close: 'ConnectionFailedPopupDismissButtonLabel', reconnect: null };
  }
}

export function mount(ctx, data) {
  const { h, i18n, el, game, sound, icon } = ctx;
  const t = (k) => i18n.t(k);
  const panel = h('div', { class: 'panel popup', testid: 'popup', attrs: { role: 'alertdialog', 'aria-modal': 'true' } });
  el.appendChild(h('div', { class: 'scrim' }, panel));

  function render(d) {
    const reason = (d && d.reason) || 'failed';
    const c = popupContent(reason, !!(d && d.reconnect));
    panel.setAttribute('data-reason', reason);
    while (panel.firstChild) panel.removeChild(panel.firstChild);
    panel.appendChild(h('div', { class: 'badge-icon' }, icon('warning')));
    if (c.title) panel.appendChild(h('h2', { class: 'title', text: t(c.title) }));
    panel.appendChild(h('p', { class: 'text', text: t(c.body) }));
    const row = h('div', { class: 'row center wrap' });
    if (c.reconnect) {
      const rb = h('button', { class: 'btn btn-primary', testid: 'popup-reconnect', type: 'button', text: t(c.reconnect) });
      rb.addEventListener('click', () => {
        sound.play('GENERIC_BUTTON_CLICKED');
        game.actions.reconnect();
      });
      row.appendChild(rb);
    }
    const cb = h('button', { class: c.reconnect ? 'btn' : 'btn btn-primary', testid: 'popup-close', type: 'button', text: t(c.close) });
    cb.addEventListener('click', () => {
      sound.play('GENERIC_BUTTON_CLICKED');
      game.actions.dismissPopup();
    });
    row.appendChild(cb);
    panel.appendChild(row);
  }
  render(data);
  return { update: render, unmount() {} };
}
