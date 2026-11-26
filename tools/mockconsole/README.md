# mockconsole

A stand-in for the game when testing the phone page. It runs the same WebRTC
Direct server as the game (iwyknet's `server` package), shows the join QR code,
emulates the game's join and resource handshakes, sends the real game JSON
messages from scenario files and validates everything the page sends against
`protocol/messages.json`.

```
tools/mockconsole/
  main.go           CLI, iwyknet wiring
  console/          game emulation, step runner, matchers, catalogue validation,
                    JPEG synthesis and checks, HTTP API (no iwyknet imports)
  bot/              Go page emulator (iwyknet/client) for self-tests
  scenarios/*.json  the scenario files
```

`console` talks to the network only through a small `Transport` interface
(`Next`, `SendText`, `SendBinary`, `Close`, `JoinURL`), so its tests run against
an in-memory fake.

## Build and test

```
cd tools/mockconsole
go vet ./... && go test ./...          # includes every scenario played by the bot over 127.0.0.1
go build -o ../../work/mockconsole/mockconsole .
python3 -m unittest tests.test_go_mockconsole   # from the repo root
```

`go test -short ./...` skips the bot scenarios.

## CLI

```
mockconsole -http 127.0.0.1:0 -udp 0 -ip "" -page URL -cert PATH [-scenario FILE] [-auto] [-bot] [-report FILE] [-serve-page DIR] [-catalogue FILE] [-v]
mockconsole -check FILE...
```

- `-http` status page and API address (default `127.0.0.1:0`, loopback only unless you pass another address).
- `-udp`, `-ip` the WebRTC server's UDP port and the advertised LAN IPv4 (empty = automatic selection, as the game does; `127.0.0.1` for local tests).
- `-page` base URL of the phone page (default `http://127.0.0.1:8088/`). Never commit a deployed address anywhere; pass it on the command line.
- `-cert` DTLS certificate PEM (created when missing; default in the temp directory).
- `-scenario` scenario file; without it the console only emulates the join handshake.
- `-auto` runs every step in order and exits (0 = all passed, 1 = a step or validation failed, 2 = bad arguments or files).
- `-bot` plays the page side with in-process Go clients (one per scenario player). Scenarios marked `"bot": false` are skipped with a note (exit 0).
- `-report` writes `{ "scenario", "pass", "steps": [{ "index", "id", "pass", "errors", "received" }], "catalogue_errors" }` on exit.
- `-serve-page DIR` also serves DIR at `/page/`.
- `-catalogue` path of `protocol/messages.json` (default: searched upwards from the working directory and from the executable).
- `-check` validates scenario files against the schema and the catalogue and exits 1 on errors.

On start it prints exactly one JSON line to stdout:

```
{"ready":true,"http":"127.0.0.1:53211","udp":53212,"join_url":"http://127.0.0.1:8088/#h=..."}
```

Self-test without a browser: `mockconsole -auto -bot -ip 127.0.0.1 -scenario scenarios/top-drawer.json`.

## Game emulation

- Join (`auto.join`, default on): on `hello` it replies `joined` (or `refused{reason}` when `auto.refuse` is not 0, or `pending` and `auto.pending_ms` later `joined`), then sends InterfaceVersionMessage and SessionStateMessage{`auto.session_id`}. A second session with the same uid gets the old session `bye{"replaced"}` and closed. A seventh uid while six hold slots is refused with reason 2.
- On ClientRequestPlayerIDMessage: PlayerID per uid starting at 2 (kept across sessions), first free slot 0..5 (kept), then AssignPlayerIDAndSlotMessage, SyncJokersQuizStateMessage{`auto.jokers`}, ClientOnlineStateMessage{false}, ClientUGCStateMessage{0} and an empty ResourceRequirementsMessage.
- Resources (`auto.resources`, default on): RequestResourceMessage for a scenario image or a page upload is answered with ImageResourceContentTransferMessage + JPEG.
- Players are numbered by the order in which their uids first say hello.
- Everything received is validated: frame shape, `id` -7503, catalogue type and field checks (unknown TypeString, wrong direction, missing field, wrong JSON type, failed validity rule), the hello uid against ClientRequestPlayerIDMessage.UID, header/binary pairing within 2 s, JPEG structure (SOI, EOI, segments, SOF dimensions, full decode), the size per ImgType (1: 384x512, 2/3/4: 480x640, 5: either) and the 128000-byte upload cap. Errors go to the running step and to `catalogue_errors`.

## Scenario format (schema 1)

```json
{ "schema": 1, "name": "top-drawer", "description": "...", "players": 1, "bot": true,
  "auto": { "join": true, "resources": true, "session_id": 4242, "jokers": 2, "refuse": 0, "pending_ms": 0 },
  "images": { "co1": { "w": 480, "h": 640, "color": "#cc6633", "label": "CO" } },
  "steps": [
    { "id": "td-photo", "doc": "...", "player": 0, "auto": { "refuse": 0 },
      "send": [ { "msg": { "TypeString": "ClientTopDrawerTakePhotoCommandMessage", "action": 12, "CommandText": "Take a photo of {0}", "PlayerName": "Bob" } } ],
      "screen": "photo", "variant": "topdrawer",
      "page": [ { "do": "tap", "target": "shutter" }, { "do": "tap", "target": "use" } ],
      "expect": [ { "msg": "TopDrawerPhotoTakenResponseMessage", "fields": { "Response": 11 } },
                  { "upload": { "ImgType": 3, "width": 480, "height": 640, "maxBytes": 128000 }, "capture": "td_photo" } ],
      "order": "any", "timeout_ms": 20000, "screenshot": true } ] }
```

- The file name is `<name>.json`. Unknown keys are errors.
- `screen`/`variant`: the view the page must show after the step's sends (the e2e harness waits for it before the page actions). `page`: actions for the e2e harness (tap, hold, type, draw, flick, wait, wait_screen, reload, navigate, expect_visible, screenshot, set_storage); targets are the testids of the contract. The console itself ignores `page`.
- Step `auto` overrides the emulation settings from that step on.
- Send items: `{"msg": {...}}` (missing fields are filled with the catalogue defaults and the message is written in catalogue field order; `"raw": true` sends it as is), `{"content": {"header": {..., "TransferID": "$tid"}, "image": "key"}}`, `{"resource": "key"}`, `{"manifest": [{"image": "key", "r": 1} | {"guid": "$cap:x", "r": 1}]}`, `{"frame": {...}}`, `{"close": true}`, `{"wait_ms": n}`. Each may carry `"player": n`.
- Placeholders (whole string values): `$guid:<image key>`, `$uid:<player>`, `$pid:<player>`, `$slot:<player>`, `$cap:<name>`, `$tid`.
- Expect items: `{"msg": "<TypeString>", "fields": {...}, "capture": {"<name>": "<field>"}}`, `{"upload": {"ImgType", "width", "height", "maxBytes"}, "capture": "<name>"}` (captures the ImageGUID), `{"frame": "hello", "uid": "$uid:0"}`, `{"closed": true}`, `{"none_ms": n}`. Each may carry `"player": n` (default: the step's player) to match frames of another player, for example when a second browser context reuses a uid.
- Field values are literals or matchers: `eq`, `ne`, `gt`, `ge`, `lt`, `le`, `in`, `type` (string, int, bool, double, array, object), `nonempty`, `regex`, `any`.
- `order`: `strict` (default; expectations matched in sequence) or `any`. Valid messages that match no expectation are recorded in `received` and are not errors. `none_ms` passes when the player sends nothing but pings for that long.
- The first step of every player taps `connect-play` and expects the hello frame (checked by `-check`). Every join expects ClientRequestPlayerIDMessage and the AllResourcesReceivedMessage answering the empty join manifest, so that reply never lands in a later step.

Scenarios: join, profile, thats-you, would-you-rather, blank-you, top-drawer, canvas-opinion, mimic-this, quickdraw, pause-joker, end-of-game, rejoin-reload, reconnect, replaced (bot false: needs two browser contexts), refusals, pending, disconnect-reasons.

## HTTP API (JSON)

- `GET /` status page: QR code, join URL, live sessions, the step list with Run buttons and the last result. `GET /qr.png`.
- `GET /api/info` -> `{ "join_url", "ip", "udp", "scenario", "players", "steps": [ids] }`.
- `GET /api/state` -> `{ "sessions": [{ "session", "uid", "player", "state", "player_id", "slot", "addr" }], "step": { "index", "id", "state" }, "players": [...], "captures": {...} }`. Session states: open, hello, pending, joined, assigned, refused, replaced, closing, closed. Step states: idle, sent, waiting, passed, failed.
- `POST /api/steps/<i>/start` with an optional body `{ "page_actions": true }` -> `{ "ok": true }` (409 while another step runs, 400 for a bad body). Without the flag (empty body or `{}`; the bot and the Run buttons) none_ms windows start when the sends are done. With it, a none item still becomes active at the same moment (in strict order: when the items before it matched), and any frame of its player from then on is a violation, including frames the page sends while the page actions run. Only its success clock waits: the item is satisfied none_ms after `page-done` (the deadline is extended to fit). When `page-done` never arrives the step fails at its timeout with "page-done never arrived", so a crashed driver cannot produce a passing step.
- `POST /api/steps/<i>/page-done` -> `{ "ok": true, "running": bool }`: the driver (the e2e harness) finished step i's page actions. `running` is false when the step had already finished, which is not an error. 409 when step i is not the current step, 400 for a bad index.
- `GET /api/steps/<i>/result?timeout_ms=N` blocks up to N ms -> `{ "index", "id", "pass", "done", "state", "errors", "received": [{ "type", "at_ms", "summary", "matched", "player" }] }`.
- `GET /api/received?since=SEQ` -> `{ "next", "items": [{ "seq", "session", "player", "kind", "frame", "transfer_id", "len", "valid", "errors" }] }` (pings are not listed).
- `POST /api/send` with `{ "player": 0, "msg" | "frame" | "content" | "resource" | "manifest" | "close": ... }` -> `{ "ok": true }`.
- `POST /api/quit`.

## Scenario conventions

- Drawings (phone-app.md, DRAWING 7): the page uploads a drawing when the player confirms it (`draw-confirm`), so a draw step that taps `draw-confirm` expects the upload (ImgType 1, or 5 for QuickDraw, 384x512, maxBytes 128000, captured) plus TrophyDrawingColouringInfoMessage. The following SendDrawing (19) or SendQuickDrawDrawing (28) step expects only `none_ms` and the `holding`/`look-at-tv` screen. A 19/28 after a drawing that was not confirmed forces it out and expects the upload: quickdraw's `draw-unconfirmed` + `forced-send` steps cover that path. `TestDrawStepSemantics` (main_test.go) enforces these rules for every scenario.
- Camera effects: profile's `selfie` step opens the effects panel (`effects`), picks `effect-N` for N = 0..11 (the panel closes on pick, so it is reopened each time), waits 1500 ms and takes a `screenshot` named `effect-N`, then takes the photo with the last effect (11, Hourglass), so the `confirm` step expects `usesFX: true`. `TestProfileExercisesCameraEffects` checks the sequence and that expectation.

Checks before committing a scenario change:

```
cd tools/mockconsole
go run . -check scenarios/*.json      # schema and catalogue
go vet ./... && go test ./...          # semantics tests and every scenario played by the bot
```

## Manual play with a real phone

1. Serve the page on the LAN or use your own deployment, for example `python3 -m pipeline.web_serve --host 0.0.0.0` (camera access needs https or localhost on the phone, so a deployed copy is usually easier).
2. `go run . -page <your page base URL> -scenario scenarios/profile.json -v` (the automatic LAN IP is advertised).
3. Open the printed `http` address in a desktop browser, scan the QR code with the phone and press Run on each step in order, doing on the phone what the step's `page` list says. Results appear next to each step.

The phone and the computer must be on the same network, and UDP to the printed port must not be blocked by the firewall.

## Deviations and notes

- Expect items accept an optional `"player"` and steps an optional `"auto"`; send items accept `"player"`, `"raw"` and manifest `"guid"` entries. These are additions to contract 7.4, not changes.
- `protocol/messages.json` adds `default` (bot synthesis), `optional`, `page`, `unused`, `content`, `inheritValid`, `replyTo`, `frames` and `limits` to the 7.6 format. `ClientQuickDrawHoldingScreenCommandMessage` does not inherit the holding-text rule (its sentinel text must be accepted).
