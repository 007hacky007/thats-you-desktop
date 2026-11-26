import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = fileURLToPath(new URL('../../', import.meta.url));

// Patterns that build HTML from strings or run strings as code.
export const FORBIDDEN = [
  ['innerHTML', /\binnerHTML\b/],
  ['outerHTML', /\bouterHTML\b/],
  ['insertAdjacentHTML', /\binsertAdjacentHTML\b/],
  ['document.write', /\bdocument\s*\.\s*write(ln)?\b/],
  ['eval(', /(^|[^.\w$])eval\s*\(/],
  ['new Function', /\bnew\s+Function\b/],
  ['Function(', /(^|[^.\w$])Function\s*\(/],
  ['createContextualFragment', /\bcreateContextualFragment\b/],
  ['DOMParser', /\bDOMParser\b/],
  ['srcdoc', /\bsrcdoc\b/],
  ['string timer', /\bset(Timeout|Interval)\s*\(\s*['"`]/],
];

function walk(dir) {
  const out = [];
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) out.push(...walk(p));
    else if (/\.(m?js)$/.test(name)) out.push(p);
  }
  return out;
}

export function findViolations(text) {
  const hits = [];
  text.split('\n').forEach((line, i) => {
    for (const [label, re] of FORBIDDEN) if (re.test(line)) hits.push({ line: i + 1, label });
  });
  return hits;
}

test('the scanner catches each pattern', () => {
  const samples = ['el.innerHTML = x', 'el.outerHTML', 'el.insertAdjacentHTML("a", b)', 'document.write(x)',
    'eval("1")', 'x = new Function("a")', 'Function("return 1")()', 'range.createContextualFragment(s)',
    'new DOMParser()', 'f.srcdoc = s', "setTimeout('x()', 1)"];
  for (const s of samples) assert.ok(findViolations(s).length > 0, s);
  for (const s of ['el.textContent = x', 'retrieval(1)', 'obj.eval(1)', 'myFunction(1)', 'setTimeout(fn, 1)']) {
    assert.equal(findViolations(s).length, 0, s);
  }
});

test('no unsafe DOM or code-from-string APIs anywhere under web/js', () => {
  const files = [...walk(join(WEB, 'js')), join(WEB, 'sw.js')];
  const problems = [];
  for (const f of files) {
    for (const v of findViolations(readFileSync(f, 'utf8'))) problems.push(`${relative(WEB, f)}:${v.line} ${v.label}`);
  }
  assert.deepEqual(problems, []);
});

test('web sources are ASCII only', () => {
  const files = [...walk(join(WEB, 'js')), join(WEB, 'sw.js')];
  for (const sub of ['css']) {
    try { for (const n of readdirSync(join(WEB, sub))) files.push(join(WEB, sub, n)); } catch { /* missing */ }
  }
  for (const n of ['index.html', 'manifest.webmanifest']) files.push(join(WEB, n));
  const bad = [];
  for (const f of files) {
    let text;
    try { text = readFileSync(f, 'utf8'); } catch { continue; }
    if (/[^\x09\x0a\x0d\x20-\x7e]/.test(text)) bad.push(relative(WEB, f));
  }
  assert.deepEqual(bad, []);
});
