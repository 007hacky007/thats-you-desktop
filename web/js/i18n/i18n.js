// Localisation loader. The app's own strings are produced at build time into
// i18n/<code>.json (from the person's own APK); without them the page uses
// our built-in English wording from en.js.

import { EN } from './en.js';

const RTL = new Set(['ar']);

export function isRtl(code) {
  return RTL.has(String(code || '').toLowerCase().split('-')[0]);
}

// Pick one of `available` for the ordered preference list `languages`.
// Per preference: exact (case-insensitive), es-* other than es-ES -> es-MX,
// nb/nn -> no, then the language prefix. Nothing matched -> `dflt`
// (when available) or 'en'.
export function pickLanguage(languages, available, dflt = 'en') {
  const avail = Array.isArray(available) ? available : [];
  const lower = new Map(avail.map((c) => [String(c).toLowerCase(), c]));
  const prefs = Array.isArray(languages) ? languages : languages ? [languages] : [];
  for (const raw of prefs) {
    if (typeof raw !== 'string' || !raw) continue;
    const pref = raw.toLowerCase().replace(/_/g, '-');
    if (lower.has(pref)) return lower.get(pref);
    const [prim, region] = pref.split('-');
    if (prim === 'es' && region && region !== 'es' && lower.has('es-mx')) return lower.get('es-mx');
    if ((prim === 'nb' || prim === 'nn') && lower.has('no')) return lower.get('no');
    if (lower.has(prim)) return lower.get(prim);
  }
  if (dflt && lower.has(String(dflt).toLowerCase())) return lower.get(String(dflt).toLowerCase());
  return 'en';
}

// {n} -> newline, {0}.. -> args. Unknown braces stay as they are.
export function formatText(text, args = []) {
  return String(text).replace(/\{(n|N|\d+)\}/g, (_, k) => {
    if (k === 'n' || k === 'N') return '\n';
    const v = args[Number(k)];
    return v === undefined || v === null ? '' : String(v);
  });
}

function isSafeRelative(path) {
  return typeof path === 'string' && path.length > 0 && !/^[a-z][a-z0-9+.-]*:/i.test(path)
    && !path.startsWith('/') && !path.startsWith('\\') && !path.split('/').includes('..');
}

async function fetchJson(fetchFn, url) {
  try {
    const res = await fetchFn(url, { cache: 'no-cache' });
    if (!res || !res.ok) return null;
    const v = await res.json();
    return v && typeof v === 'object' ? v : null;
  } catch {
    return null;
  }
}

function defaultLanguages() {
  const nav = globalThis.navigator;
  if (nav && Array.isArray(nav.languages) && nav.languages.length) return nav.languages;
  if (nav && nav.language) return [nav.language];
  return ['en'];
}

export async function loadI18n({ languages, fetch: fetchFn, locale, base = '' } = {}) {
  const langs = languages || defaultLanguages();
  const f = fetchFn || globalThis.fetch?.bind(globalThis);
  const loc = locale || globalThis.navigator?.language || (typeof langs[0] === 'string' ? langs[0] : 'en-US');
  let lang = 'en';
  let table = null;
  if (f) {
    const index = await fetchJson(f, `${base}i18n/index.json`);
    const entries = index && index.languages && typeof index.languages === 'object' ? index.languages : null;
    if (entries) {
      const codes = Object.keys(entries).filter((c) => isSafeRelative(entries[c]));
      const picked = pickLanguage(langs, codes, typeof index.default === 'string' ? index.default : 'en');
      if (codes.includes(picked)) {
        const data = await fetchJson(f, `${base}${entries[picked]}`);
        if (data) {
          table = data;
          lang = picked;
        }
      }
    }
  }
  const fallbacks = [EN];
  const lookup = (key) => {
    if (table && typeof table[key] === 'string') return table[key];
    for (const fb of fallbacks) if (typeof fb[key] === 'string') return fb[key];
    return null;
  };
  return {
    lang,
    locale: loc,
    dir: isRtl(lang) ? 'rtl' : 'ltr',
    hasAppText: !!table,
    t(key, ...args) {
      const s = lookup(key);
      return s === null ? String(key) : formatText(s, args);
    },
    has(key) {
      return lookup(key) !== null;
    },
    addFallback(map) {
      if (map && typeof map === 'object') fallbacks.push(map);
    },
  };
}
