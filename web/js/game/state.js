// game.state (contract 5.4) and small value helpers.

export const NAME_MAX = 10;
export const TEXT_MAX = 80;
export const CHAT_MAX = 140;
export const CARD_IDS = Object.freeze(Array.from({ length: 12 }, (_, i) => 'CARD_' + (i + 1)));

// Cut a string to at most n code points.
export function clampText(s, n) {
  const str = s === undefined || s === null ? '' : String(s);
  const cps = Array.from(str);
  return cps.length <= n ? str : cps.slice(0, n).join('');
}

export function isCardId(id) {
  return CARD_IDS.includes(id);
}

export function createGameState() {
  return {
    phase: 'none', // 'none' | 'lobby' | 'game'
    playerId: -1,
    slotId: -1,
    jokers: 0,
    scenario: -1,
    roundType: 0,
    question: '',
    profile: { name: '', cardId: '', isGuest: false, portraitUrl: null },
    connected: false,
    online: false,
    ugcState: 0,
    gameId: '',
    // other players: [{ playerId, slotId, name }]
    players: [],
  };
}

export function rosterJoined(state, m) {
  const id = m.CurrentPlayerID;
  const old = Number.isInteger(m.OldPlayerID) ? m.OldPlayerID : id;
  const prev = state.players.find((p) => p.playerId === id || p.playerId === old);
  const name = prev ? prev.name : '';
  state.players = state.players.filter((p) => p.playerId !== id && p.playerId !== old);
  state.players.push({ playerId: id, slotId: m.SlotID, name });
  state.players.sort((a, b) => a.slotId - b.slotId);
}

export function rosterName(state, m) {
  const p = state.players.find((x) => x.playerId === m.PlayerID);
  if (p) p.name = m.PlayerName;
  else state.players.push({ playerId: m.PlayerID, slotId: -1, name: m.PlayerName });
}

export function rosterLeft(state, m) {
  state.players = state.players.filter((p) => p.playerId !== m.PlayerID);
}
