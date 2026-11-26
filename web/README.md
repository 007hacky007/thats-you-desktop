# Phone page

Static page that replaces the discontinued phone app. Vanilla ES modules, no
build step, no dependencies. Works in iOS Safari 16.4+ and Android Chrome 120+.

## Serving locally

- Neutral mode (no art, built-in English): `python3 -m pipeline.web_serve`
  (or `python3 -m http.server -d web`), then open `http://127.0.0.1:8088/`.
- Art mode (the app's art, fonts and texts from your own APK):
  `python3 -m pipeline.build_web` then `python3 -m pipeline.web_serve --art`.
  The art is copied into `work/web-dist` at build time and is never committed.

`http://127.0.0.1` and `http://localhost` are secure contexts, so the camera and
the service worker work there. On a phone the page must be served over https.

## Fragment keys

- `#h=..&p=..&u=..&w=..&f=..&t=..` join parameters from the game's QR code
  (parsed by `js/net/params.js`; never sent anywhere).
- `#debug=1` shows the log overlay (can be combined with the join keys).
- `#demo` lists every demo screen; `#demo=<screen>[:<variant>]` renders one
  screen from its module's `demos` export without a connection.
  `&overlay=<id>[:<arg>]` adds an overlay: `popup:<reason>[:reconnect]`
  (reasons gamefull, version, onlyone, notallowed, failed, nogame, replaced,
  shutdown), `pause`, `leave`, `reconnecting`, `bad-connection`.
  Example: `#demo=holding:look-at-tv&overlay=pause`.

## Structure

- `index.html` (CSP, see the contract), `manifest.webmanifest`, `sw.js`,
  `icons/icon.svg`.
- `js/main.js` boot: i18n, assets, theme, service worker registration, then
  `game/game.js` and `camera/media.js` by dynamic import (a failure shows a
  plain error screen with `data-testid="boot-error"`).
- `js/ui/app.js` screen host: mounts the screen of `game.view`, keeps
  `document.body.dataset.screen / .variant / .overlay`, the nav bar (leave
  button on every screen except connect), the scenario background and the
  overlays. Overlay priority, highest first: popup, leave, reconnecting,
  pause, effects, bad-connection.
- `js/ui/registry.js` maps every screen id to a dynamic import, so one broken
  module only breaks its own screen (`data-testid="screen-error"`).
- Screens get `ctx = { el, game, media, i18n, assets, h, icon, log, sound, overlay }`.
  `ctx.overlay.open(id) / close(id) / isOpen(id)` is a small addition to the
  contract: camera screens use it for the `effects` overlay so the body
  dataset reflects it.
- Shared helpers: `ui/flick.js` (`attachFlick`), `ui/hold.js` (`attachHold`),
  `ui/icons.js` (our own SVG icons; in art mode the atlas sprite),
  `ui/sound.js` (optional, no-op without art, unlocked by the PLAY tap),
  `util/dom.js` (`h`, `clear`, `setText`, `clampCodePoints`, `byTestId`).
- CSS classes other screens may reuse (base.css): `stack`, `row`, `panel`,
  `card-panel`, `btn`, `btn-primary`, `btn-danger`, `btn-small`, `btn-icon`,
  `btn-big`, `title`, `text`, `small`, `game-text`, `pill`, `polaroid`,
  `photo`, `spinner`, `dots`, `badge-icon`, `text-field`, `toggle`.

## Safety rules

No `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`, `eval`
or `new Function` anywhere: `web/test/lint` fails on them. Game strings go
through `textContent` only and are shown with `white-space: pre-line`.

## Art versus neutral

`js/assets/assets.js` reads `art/manifest.json` (written by
`pipeline/build_web.py`). Missing or invalid -> neutral mode: system fonts,
high contrast dark theme, numbered card tiles, our own icons and English
texts. With the manifest `body.art` is set and CSS variables carry the fonts
(`--font-heading`, `--font-body`, `--font-text`) and images (`--art-table`,
`--art-polaroid`, ...). Sprite borders in the manifest are
`[left, right, top, bottom]` as in the atlas prefab.

`js/i18n/i18n.js` reads `i18n/index.json` and the picked language file; any
missing key falls back to our own English wording in `js/i18n/en.js`, then to
the key itself. Language pick: exact match, `es-*` (not `es-ES`) to `es-MX`,
`nb`/`nn` to `no`, language prefix, else English. Arabic switches to
right-to-left.

## Offline

`sw.js` precaches the files listed in `precache.json` (written by the build)
into `iwyk-<version>` and serves them cache-first; opening the page checks
`precache.json` in the background and swaps to a newer build for the next
load. Without `precache.json` (plain `web/`), it is network first with a
runtime cache fallback so development never serves stale files (a deliberate
deviation from the contract's cache-first rule, only in that mode).

## Test ids

connect `connect-play`; nav `leave`; leave overlay `leave-confirm`,
`leave-cancel`; popup `popup` (with `data-reason`), `popup-reconnect`,
`popup-close`; name `name-input`, `name-guest`, `name-next`; card `card-1` ..
`card-12`, `card-confirm`; confirm `profile-confirm`, `profile-edit-name`,
`profile-edit-card`, `profile-edit-photo`; continue `continue`, `start`
(press and hold 2 s). The play screens' ids are listed in the contract.

## Tests

`node --test web/test/` (U5 parts: `test/ui`, `test/i18n`, `test/assets`,
`test/lint`). `test/ui/fakedom.mjs` is a tiny DOM stand-in for screen tests.

## End-to-end runs

`python3 -m pipeline.web_e2e --all --loopback` (add `--art` for the
`build_web` output) drives headless Chrome against the mock console
(`tools/mockconsole`) over WebRTC Direct. Results go to
`work/reports/web-e2e.json`, screenshots and per-page logs to
`work/e2e/<UTC stamp>/<scenario>/`.

- Page-done signalling: for a step with page actions the harness starts it
  with `{"page_actions": true}` and calls `POST /api/steps/<i>/page-done`
  once the actions ran (also after a failed action), so the console's
  `none_ms` expectations start after the page is done. A console without the
  call (404, 400 or 405) is tolerated.
- Fake camera: by default Chrome gets
  `--use-file-for-fake-video-capture=<out>/test-pattern.y4m`, a 640x480,
  15 fps, 30 frame C420jpeg pattern (colour bars, grid, both diagonals, a
  circle and a moving marker) written in pure Python at run time, so camera
  effect warps are easy to judge in screenshots. `--camera default` keeps
  Chrome's built-in fake.
- Page errors: `log.error` and `exception` lines of every `page-<n>.log` are
  listed per scenario as `page_errors` in the report and counted at the end.
  They do not fail the run (neutral mode always has the `art/manifest.json`
  404).
