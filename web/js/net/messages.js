// Game message catalogue (contract 5.5): builders for every phone -> game
// message (exact field order) and validators for every game -> phone message.

export const INTERFACE_VERSION = '2016-07-12-1645';

export const ACTION = Object.freeze({
  NotSet: -1, Idle: 1, JokerScreen: 2, AnswerQuestion: 3, ContinueButton: 4, HoldingScreen: 5,
  Drawing: 7, TextEntry: 8, ScenarioTransition: 9, RoleCall: 11, TakeDrawingCanvasPhoto: 12,
  WishMessage: 13, StartGameButtonPrompt: 14, EndOfGameMessage: 15, SendAnswer: 16, SendJoker: 17,
  SendTextEntry: 18, SendDrawing: 19, UGCSelection: 20, MessageCollection: 21, ActivityMessage: 22,
  MimicThis: 23, SendMimicThisPhoto: 24, QuickDrawTakePhoto: 25, QuickDrawDrawing: 27,
  SendQuickDrawDrawing: 28, Pause: 29, Unpause: 30, EditProfile: 31,
});

export const HOLDING = Object.freeze({
  NONE: 0, BLANK: 1, OTHER_PLAYER_CHOOSING_UGC: 2, OTHER_PLAYER_TAKING_PHOTO: 3, LOOK_AT_TV: 4,
  QUICK_DRAW: 5, CUSTOM_MESSAGE: 6, QUICK_DRAW_MESSAGE: 7, REJOIN: 8, STRING_HACKS: 99,
});

export const ROUND = Object.freeze({
  TopDrawer: 1, ThatsYou: 2, PictureThis: 4, CanvasOpinion: 8, ThatsYouGC: 16, BlankYou: 32,
  MimicThis: 64, WouldYouRather: 128, QuickDraw: 256, NoType: 512,
});

export const DISCONNECT = Object.freeze({
  Default: 0, GameFull: 2, ConnectionFailed: 3, ConnectionRejected: 4, VersionDisparity: 5,
  OnlyOnePlayer: 6, ConnectionNotAllowed: 7,
});

export const IMG = Object.freeze({
  Unspecified: 0, PlayerDrawing: 1, PlayerPortrait: 2, DrawingCanvas: 3, MimicPhoto: 4, QuickDraw: 5,
});

export const REQUIREMENT = Object.freeze({ NoRequirement: 0, Required: 1, SafeToUnload: 2 });

export const RESPONSE = Object.freeze({
  NotSet: 0, AnswerIndex: 1, JokerConsumed: 2, ContinuePressed: 3, UsePicPressed: 4, TextAnswer: 5,
  PlayerName: 6, UGCDeclined: 7, PlayerCard: 8, StartButtonPressed: 9, DeviceInfo: 10,
  TopDrawerPhotoTaken: 11,
});

export const SENTINEL = Object.freeze({
  playerName: '***NOTSETPLAYERNAME',
  answerText: '***NOTSETANSWERTEXT',
  wyrText: '---NOTSETTEXT---',
  holdingText: '***NOTSETHOLDINGSCREENTEXT',
  textEntryTitle: '***NOTSETTEXTENTRYTITLE',
  questionBar: '****NOTSETQUESTIONBARTEXT',
});

// ---------------------------------------------------------------- builders

const int = (v) => Math.trunc(Number(v)) || 0;
const str = (v) => (v === undefined || v === null ? '' : String(v));

export const build = {
  clientRequestPlayerId: (uid) => ({ TypeString: 'ClientRequestPlayerIDMessage', UID: str(uid) }),

  deviceInfo: (d) => ({
    TypeString: 'DeviceInfoMessage',
    Response: RESPONSE.DeviceInfo,
    DeviceSize: int(d.DeviceSize),
    DeviceOS: int(d.DeviceOS),
    DeviceModel: str(d.DeviceModel),
    DeviceType: str(d.DeviceType),
    DeviceUID: str(d.DeviceUID),
    DeviceOperatingSystem: str(d.DeviceOperatingSystem),
  }),

  clientPlayerProfile: ({ name, locale, isGuest, cardId }) => {
    const n = str(name);
    let upper;
    try {
      upper = n.toLocaleUpperCase(locale || undefined);
    } catch {
      upper = n.toUpperCase();
    }
    return {
      TypeString: 'ClientPlayerProfileMessage',
      playerName: n,
      uppercasePlayerName: upper,
      deviceCultureName: str(locale),
      ugcInfo: { scenarioInt: -1, scenario: -100, challengeID: '', creationID: '', set: false },
      isGuest: isGuest ? 1 : 0,
      playerCardId: str(cardId),
    };
  },

  clientDisconnect: () => ({ TypeString: 'ClientDisconnectMessage' }),

  answerIndex: (index) => ({ TypeString: 'AnswerIndexResponseMessage', AnswerIndex: int(index) }),

  jokerConsumed: (consumed) => ({
    TypeString: 'JokerConsumedResponseMessage', Response: RESPONSE.JokerConsumed, jokerConsumed: !!consumed,
  }),

  continuePressed: () => ({ TypeString: 'ContinuePressedResponseMessage', Response: RESPONSE.ContinuePressed }),

  usePicPressed: () => ({ TypeString: 'UsePicPressedResponseMessage', Response: RESPONSE.UsePicPressed }),

  textAnswer: (text) => ({ TypeString: 'TextAnswerReponseMessage', Response: RESPONSE.TextAnswer, Text: str(text) }),

  startGameButtonPressed: () => ({
    TypeString: 'StartGameButtonPressedResponseMessage', Response: RESPONSE.StartButtonPressed,
  }),

  topDrawerPhotoTaken: () => ({
    TypeString: 'TopDrawerPhotoTakenResponseMessage', Response: RESPONSE.TopDrawerPhotoTaken,
  }),

  trophyPhotoFX: (guid, usesFX) => ({ TypeString: 'TrophyPhotoFXInfoMessage', ImageGUID: str(guid), usesFX: !!usesFX }),

  trophyDrawingColouring: (guid, available, used) => ({
    TypeString: 'TrophyDrawingColouringInfoMessage',
    ImageGUID: str(guid),
    ColoursAvailable: int(available),
    ColoursUsed: int(used),
  }),

  imageTransfer: (transferId, guid, imgType) => ({
    TypeString: 'ClientImageResourceContentTransferMessage',
    TransferID: int(transferId),
    ImageGUID: str(guid),
    ImgType: int(imgType),
  }),

  requestResource: (guid) => ({ TypeString: 'RequestResourceMessage', ResourceGUID: str(guid) }),

  allResourcesReceived: (requirements) => ({
    TypeString: 'AllResourcesReceivedMessage',
    Requirements: requirements.map((r) => ({ GUID: str(r.GUID), Rqrmnt: int(r.Rqrmnt) })),
  }),

  clientSendChatLog: (playerId, message, tick) => ({
    TypeString: 'ClientSendChatLogMessage', PlayerId: int(playerId), Message: str(message), TickTimestamp: int(tick),
  }),
};

// TypeString of every builder (for cross-checks).
export const PHONE_TO_GAME = Object.freeze([
  'ClientRequestPlayerIDMessage', 'DeviceInfoMessage', 'ClientPlayerProfileMessage', 'ClientDisconnectMessage',
  'AnswerIndexResponseMessage', 'JokerConsumedResponseMessage', 'ContinuePressedResponseMessage',
  'UsePicPressedResponseMessage', 'TextAnswerReponseMessage', 'StartGameButtonPressedResponseMessage',
  'TopDrawerPhotoTakenResponseMessage', 'TrophyPhotoFXInfoMessage', 'TrophyDrawingColouringInfoMessage',
  'ClientImageResourceContentTransferMessage', 'RequestResourceMessage', 'AllResourcesReceivedMessage',
  'ClientSendChatLogMessage',
]);

// ---------------------------------------------------------------- validators

const isInt = (v) => Number.isInteger(v);
const isStr = (v) => typeof v === 'string';
const nonEmpty = (v) => typeof v === 'string' && v.length > 0;
const strOrNull = (v) => v === undefined || v === null || typeof v === 'string';

function command(action, extra) {
  return (m) => {
    if (!isInt(m.action) || m.action === -1) return 'action';
    if (action !== null && m.action !== action) return 'action ' + m.action + ' != ' + action;
    return extra ? extra(m) : null;
  };
}

// Each rule returns null when valid, else a short reason.
const RULES = {
  InterfaceVersionMessage: (m) => (isStr(m.InterfaceVersion) ? null : 'InterfaceVersion null'),
  SessionStateMessage: (m) => (isInt(m.SessionID) && m.SessionID > 0 ? null : 'SessionID'),
  AssignPlayerIDAndSlotMessage: (m) =>
    (isInt(m.PlayerID) && m.PlayerID >= 0 && isInt(m.SlotID) && m.SlotID >= 0 && isInt(m.UDPPortOffset) && m.UDPPortOffset >= 0
      ? null : 'ids'),
  DisconnectCommandMessage: () => null,
  ClientOnlineStateMessage: () => null,
  ClientUGCStateMessage: () => null,
  JoinLeaveMessage: () => null,
  PlayerJoinedMessage: (m) =>
    (isInt(m.CurrentPlayerID) && m.CurrentPlayerID !== -1 && isInt(m.SlotID) && m.SlotID !== -1 ? null : 'ids'),
  PlayerNameQuizStateMessage: (m) =>
    (isStr(m.PlayerName) && m.PlayerName !== SENTINEL.playerName && isInt(m.PlayerID) && m.PlayerID >= 0 ? null : 'name'),
  PlayerLeftMessage: (m) => (isInt(m.PlayerID) && m.PlayerID !== -1 ? null : 'PlayerID'),
  RejoiningClientOwnProfileMessage: (m) => (nonEmpty(m.Name) && nonEmpty(m.CardID) && strOrNull(m.PortraitGUID) ? null : 'profile'),
  ClientGameIDMessage: (m) => (nonEmpty(m.GameID) ? null : 'GameID'),
  SyncJokersQuizStateMessage: (m) => (isInt(m.NumJokers) && m.NumJokers >= 0 ? null : 'NumJokers'),
  ClientReceiveChatLogMessage: () => null,
  QuestionDataMessage: () => null,
  ResourceRequirementsMessage: () => null,
  ImageResourceContentTransferMessage: (m) =>
    (isInt(m.TransferID) && m.TransferID >= 1 && nonEmpty(m.ImageGUID) ? null : 'transfer'),
  PortraitThumbnailContentTransferMessage: (m) =>
    (isInt(m.TransferID) && m.TransferID >= 1 && nonEmpty(m.ClientUID) ? null : 'transfer'),
  AnswerTextQuizStateMessage: (m) =>
    (isStr(m.AnswerText) && m.AnswerText !== SENTINEL.answerText && isInt(m.PlayerID) && m.PlayerID >= 0 ? null : 'answer'),
  WouldYouRatherAnswerTextQuizMessage: (m) =>
    (isStr(m.AnswerText) && m.AnswerText !== SENTINEL.wyrText && isInt(m.AnswerId) && m.AnswerId >= 0 ? null : 'answer'),

  ClientQuizCommandMessage: command(null),
  ClientHoldingScreenCommandMessage: command(ACTION.HoldingScreen, (m) => {
    if (!strOrNull(m.HoldingScreenText) || m.HoldingScreenText === SENTINEL.holdingText) return 'text';
    if (!isInt(m.HoldingScreenType) || m.HoldingScreenType === HOLDING.NONE) return 'type';
    if ((m.HoldingScreenType === 2 || m.HoldingScreenType === 3) && !(isInt(m.OtherPlayerIndex) && m.OtherPlayerIndex > 0)) {
      return 'OtherPlayerIndex';
    }
    return null;
  }),
  // The QuickDraw holding message keeps the holding-text sentinel: allowed.
  ClientQuickDrawHoldingScreenCommandMessage: command(ACTION.HoldingScreen, (m) =>
    (m.HoldingScreenType === HOLDING.QUICK_DRAW ? null : 'type')),
  ClientTextEntryCommandMessage: command(ACTION.TextEntry, (m) =>
    (strOrNull(m.TitleText) && m.TitleText !== SENTINEL.textEntryTitle ? null : 'TitleText')),
  ClientAnswerCommandMessage: command(null, (m) => {
    if (!strOrNull(m.questionBarText) || m.questionBarText === SENTINEL.questionBar) return 'questionBarText';
    if (!isInt(m.roundID) || m.roundID === ROUND.NoType) return 'roundID';
    if (m.ImageGUIDs !== undefined && m.ImageGUIDs !== null && !Array.isArray(m.ImageGUIDs)) return 'ImageGUIDs';
    return null;
  }),
  ClientStartDrawingCommandMessage: command(ACTION.Drawing),
  ClientTopDrawerTakePhotoCommandMessage: command(ACTION.TakeDrawingCanvasPhoto),
  ClientMimicThisCommandMessage: command(ACTION.MimicThis),
  ClientQuickDrawTakePhotoCommandMessage: command(ACTION.QuickDrawTakePhoto),
  ClientScenarioTransitionCommandMessage: command(ACTION.ScenarioTransition),
  ClientJokerCommandMessage: command(ACTION.JokerScreen, (m) =>
    (isInt(m.PreNumJokers) && m.PreNumJokers >= 0 && isInt(m.CurNumJokers) && m.CurNumJokers >= 0 ? null : 'jokers')),
  ClientSelectedUGCRequestMessage: command(ACTION.UGCSelection),
};

export const GAME_TO_PHONE = Object.freeze(Object.keys(RULES));

// null when the message is valid and known, else the reason it is dropped.
export function checkGameMessage(m) {
  if (!m || typeof m !== 'object' || Array.isArray(m)) return 'not an object';
  if (typeof m.TypeString !== 'string') return 'no TypeString';
  const rule = RULES[m.TypeString];
  if (!rule) return 'unknown type ' + m.TypeString;
  const why = rule(m);
  return why ? m.TypeString + ': ' + why : null;
}

export function isValidGameMessage(m) {
  return checkGameMessage(m) === null;
}
