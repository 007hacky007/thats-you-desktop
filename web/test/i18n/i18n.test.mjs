import test from 'node:test';
import assert from 'node:assert/strict';
import { pickLanguage, formatText, loadI18n, isRtl } from '../../js/i18n/i18n.js';
import { EN } from '../../js/i18n/en.js';

const CODES = ['en', 'en-GB', 'fr', 'fr-CA', 'de', 'it', 'es', 'es-MX', 'pt', 'pt-BR', 'pl', 'ru',
  'nl', 'da', 'fi', 'no', 'sv', 'tr', 'cs', 'hu', 'el', 'ar', 'GBR'];

test('every available code is picked by an exact match', () => {
  for (const code of CODES) assert.equal(pickLanguage([code], CODES), code, code);
});

test('exact match ignores case', () => {
  assert.equal(pickLanguage(['EN-gb'], CODES), 'en-GB');
  assert.equal(pickLanguage(['pt-br'], CODES), 'pt-BR');
  assert.equal(pickLanguage(['FR-ca'], CODES), 'fr-CA');
});

test('language prefix match', () => {
  assert.equal(pickLanguage(['de-AT'], CODES), 'de');
  assert.equal(pickLanguage(['fr-BE'], CODES), 'fr');
  assert.equal(pickLanguage(['en-US'], CODES), 'en');
  assert.equal(pickLanguage(['pt-PT'], CODES), 'pt');
  assert.equal(pickLanguage(['cs-CZ'], CODES), 'cs');
  assert.equal(pickLanguage(['ru-RU'], CODES), 'ru');
});

test('Norwegian Bokmal and Nynorsk map to no', () => {
  assert.equal(pickLanguage(['nb'], CODES), 'no');
  assert.equal(pickLanguage(['nb-NO'], CODES), 'no');
  assert.equal(pickLanguage(['nn-NO'], CODES), 'no');
  assert.equal(pickLanguage(['no-NO'], CODES), 'no');
});

test('Spanish variants other than es-ES go to es-MX', () => {
  assert.equal(pickLanguage(['es-ES'], CODES), 'es');
  assert.equal(pickLanguage(['es'], CODES), 'es');
  assert.equal(pickLanguage(['es-AR'], CODES), 'es-MX');
  assert.equal(pickLanguage(['es-419'], CODES), 'es-MX');
  assert.equal(pickLanguage(['es-US'], CODES), 'es-MX');
  assert.equal(pickLanguage(['es-mx'], CODES), 'es-MX');
  // without es-MX available the prefix wins
  assert.equal(pickLanguage(['es-AR'], ['en', 'es']), 'es');
});

test('preference order and default', () => {
  assert.equal(pickLanguage(['ja-JP', 'de-DE'], CODES), 'de');
  assert.equal(pickLanguage(['ja', 'zh-CN'], CODES), 'en');
  assert.equal(pickLanguage([], CODES), 'en');
  assert.equal(pickLanguage(undefined, CODES), 'en');
  assert.equal(pickLanguage(['ja'], ['fr', 'en'], 'fr'), 'fr');
  assert.equal(pickLanguage(['de'], []), 'en');
});

test('ar is right to left', () => {
  assert.equal(isRtl('ar'), true);
  assert.equal(isRtl('en'), false);
  assert.equal(isRtl('AR'), true);
});

test('placeholders', () => {
  assert.equal(formatText('JOKERS{n}REMAINING'), 'JOKERS\nREMAINING');
  assert.equal(formatText('{0} IS HERE', ['Bob']), 'Bob IS HERE');
  assert.equal(formatText('{0} and {1}{n}{0}', ['a', 'b']), 'a and b\na');
  assert.equal(formatText('{0} missing', []), ' missing');
  assert.equal(formatText('a{N}b'), 'a\nb');
  assert.equal(formatText('{x} stays'), '{x} stays');
  assert.equal(formatText('{0}', [3]), '3');
});

function fakeFetch(files) {
  const calls = [];
  const fn = async (url) => {
    calls.push(url);
    if (!(url in files)) return { ok: false, status: 404, json: async () => { throw new Error('404'); } };
    const body = files[url];
    return { ok: true, status: 200, json: async () => (typeof body === 'string' ? JSON.parse(body) : body) };
  };
  fn.calls = calls;
  return fn;
}

test('loadI18n with no files falls back to built-in English', async () => {
  const i18n = await loadI18n({ languages: ['fr-FR'], fetch: fakeFetch({}), locale: 'fr-FR' });
  assert.equal(i18n.lang, 'en');
  assert.equal(i18n.dir, 'ltr');
  assert.equal(i18n.locale, 'fr-FR');
  assert.equal(i18n.t('PausedTitle'), EN.PausedTitle);
  assert.equal(i18n.t('no.such.key'), 'no.such.key');
  assert.equal(i18n.has('PausedTitle'), true);
  assert.equal(i18n.has('no.such.key'), false);
});

test('loadI18n picks a language from index.json and falls back per key', async () => {
  const fetch = fakeFetch({
    'i18n/index.json': { default: 'en', languages: { en: 'i18n/en.json', de: 'i18n/de.json', ar: 'i18n/ar.json' } },
    'i18n/de.json': { PausedTitle: 'PAUSE', OtherPersonTakingPhotoMessage: '{0} FOTO', JokerCounterLabel: 'J{n}R' },
    'i18n/en.json': { PausedTitle: 'APP EN' },
  });
  const i18n = await loadI18n({ languages: ['de-CH', 'en'], fetch, locale: 'de-CH' });
  assert.equal(i18n.lang, 'de');
  assert.equal(i18n.t('PausedTitle'), 'PAUSE');
  assert.equal(i18n.t('OtherPersonTakingPhotoMessage', 'Eva'), 'Eva FOTO');
  assert.equal(i18n.t('JokerCounterLabel'), 'J\nR');
  // missing in de -> en.js, not the app's en.json
  assert.equal(i18n.t('PausedMessage'), EN.PausedMessage);
  assert.equal(i18n.t('port.reconnecting'), EN['port.reconnecting']);
  assert.ok(!fetch.calls.includes('i18n/en.json'));
});

test('addFallback adds keys below the language and en.js', async () => {
  const fetch = fakeFetch({
    'i18n/index.json': { default: 'en', languages: { en: 'i18n/en.json' } },
    'i18n/en.json': { X1: 'from app' },
  });
  const i18n = await loadI18n({ languages: ['en-US'], fetch });
  i18n.addFallback({ X1: 'fallback', X2: 'fallback two {0}', PausedTitle: 'not used' });
  assert.equal(i18n.t('X1'), 'from app');
  assert.equal(i18n.t('X2', 'z'), 'fallback two z');
  assert.equal(i18n.t('PausedTitle'), EN.PausedTitle);
  assert.equal(i18n.has('X2'), true);
});

test('arabic sets rtl', async () => {
  const fetch = fakeFetch({
    'i18n/index.json': { default: 'en', languages: { en: 'i18n/en.json', ar: 'i18n/ar.json' } },
    'i18n/ar.json': { PausedTitle: '\u0645' },
  });
  const i18n = await loadI18n({ languages: ['ar-EG'], fetch });
  assert.equal(i18n.lang, 'ar');
  assert.equal(i18n.dir, 'rtl');
  assert.equal(i18n.t('PausedTitle'), '\u0645');
});

test('a missing language file falls back to English left to right', async () => {
  const fetch = fakeFetch({
    'i18n/index.json': { default: 'en', languages: { en: 'i18n/en.json', ar: 'i18n/ar.json' } },
  });
  const i18n = await loadI18n({ languages: ['ar-EG'], fetch });
  assert.equal(i18n.lang, 'en');
  assert.equal(i18n.dir, 'ltr');
  assert.equal(i18n.t('PausedTitle'), EN.PausedTitle);
});

test('base path is prefixed to every fetch', async () => {
  const fetch = fakeFetch({
    'x/i18n/index.json': { default: 'en', languages: { fr: 'i18n/fr.json' } },
    'x/i18n/fr.json': { PausedTitle: 'P' },
  });
  const i18n = await loadI18n({ languages: ['fr'], fetch, base: 'x/' });
  assert.equal(i18n.t('PausedTitle'), 'P');
});

test('index paths outside the page are refused', async () => {
  const fetch = fakeFetch({
    'i18n/index.json': { default: 'en', languages: { fr: 'https://evil.invalid/fr.json' } },
  });
  const i18n = await loadI18n({ languages: ['fr'], fetch });
  assert.equal(i18n.lang, 'en');
  assert.ok(!fetch.calls.some((u) => u.startsWith('https:')));
});

test('en.js covers every key the page uses', () => {
  const required = [
    'JoinPS4ButtonLabel', 'JoinedGameMessage', 'WaitForGameStartInstruction', 'EnterNameTitle',
    'SelectCardTitle', 'ConfirmProfileTitle', 'TakePhotoTitle', 'RetakePhotoButtonLabel',
    'PopupConfirmButtonLabel', 'PopupCancelButtonLabel', 'PopupLeaveButtonLabel',
    'JokerAddedMessage', 'JokerAddedPluralMessage', 'JokerSwitchTitle', 'JokerSwitchInstruction',
    'JokerCounterLabel', 'LeavePartyGamePopupMessage', 'LookAtTvInstruction', 'GetReadyToDrawInstruction',
    'OtherPersonTakingPhotoMessage', 'OtherPersonTakingPhotoInstruction', 'WaitForNextQuestionInstruction',
    'PausedTitle', 'PausedMessage', 'PartyContinueButtonLabel', 'PartyReadyToStartMessage',
    'DrawingsToGoMessage1', 'DrawingsToGoMessage2', 'DrawingsToGoMessage3', 'DrawingsToGoMessage4',
    'DrawingsToGoMessage5', 'GameFullPopupMessage', 'VersionDisparityPopupMessage', 'OnlyOnePlayerOnlineMessage',
    'GameStartedPopupMessage', 'ConnectionFailedPopupMessage', 'ConnectionWarning_NoGameMessage',
    'ConnectionWarning_NoGameInstruction', 'DisconnectedPopup_Heading', 'DisconnectedPopup_Explanation',
    'DisconnectedPopup_Reconnect', 'DisconnectedPopup_Close',
    'port.scanQr', 'port.connecting', 'port.waiting', 'port.reconnecting', 'port.replaced',
    'port.shutdown', 'port.noCamera', 'port.confirm',
  ];
  for (const n of ['None', 'Mirror', 'Warp', 'Shrink', 'Stretch', 'Kaleidoscope', 'Bulge', 'Twist',
    'V2Mirror', 'Square', 'Grid', 'Hourglass']) required.push(`CameraEffectName_${n}`);
  for (const k of required) assert.equal(typeof EN[k], 'string', k);
});

test('en.js is ASCII only', () => {
  for (const [k, v] of Object.entries(EN)) {
    assert.match(k, /^[\x20-\x7e]+$/);
    assert.match(v, /^[\x20-\x7e]*$/, k);
  }
});

test('app keys in en.js exist in the extracted localisation (skipped without the export)', async (t) => {
  const { readFile } = await import('node:fs/promises');
  const path = new URL('../../../work/export/app/ExportedProject/Assets/Resources/localisation/appText_en.loc.loc', import.meta.url);
  let text;
  try { text = await readFile(path, 'utf8'); } catch { t.skip('no export'); return; }
  const keys = new Set(text.split(/\r?\n/).map((l) => l.split('\t')[0]));
  for (const k of Object.keys(EN)) {
    if (k.startsWith('port.')) continue;
    assert.ok(keys.has(k), `unknown app key ${k}`);
  }
});
