# That's You! - PC/Mac port with a web phone controller

Scripts that rebuild the PS4 PlayLink game "That's You!" (CUSA08389) from your own backup as a native macOS (Apple silicon) and Windows x64 game, and replace the discontinued phone app with a web page that runs in any modern phone browser, live camera effects included. Phones join by scanning a QR code on the TV and talk to the game directly over WebRTC on your local network.

This repository contains no game or app files and no code taken from them. Everything game specific is produced on your machine from your own copies.

## Status

| Part | State |
|------|-------|
| macOS (Apple silicon) build | Works. Full games tested with automated players. |
| Phone page, iPhone (Safari) | Works, tested on a real phone. |
| Phone page, Android (Chrome) | Works, tested with a full game on the Android emulator. |
| Windows x64 build | Experimental, untested on real hardware. Boots, networks and accepts phones in a VM. |
| PC gamepads (DualShock, Xbox) | Experimental, untested. The keyboard works (see Controls). |

## What you need

- Your own dumps of the game: the base game `CUSA08389` and its patch `CUSA08389-patch` (decrypted folders).
- The Android APK of the companion app (`com.playstation.iwyk`), for the phone page's original art, fonts and texts. Without it the page still works with plain styling.
- A Mac with Apple silicon for building (the Windows game is built on the Mac too).
- Unity 6000.6.10f1 (install it with Unity Hub; add "Windows Build Support (Mono)" for the Windows build).
- Python 3.11 or newer, Go 1.27 or newer, cmake, the .NET runtime (`dotnet`), Node.js 20 or newer.
- For the Windows build: a mingw-w64 cross compiler (`brew install mingw-w64`) or `zig`.
- Google Chrome for the automated tests.
- Somewhere to host the phone page over HTTPS: a Cloudflare account (free tier is enough), or your own web server.

Third-party tools (AssetRipper, Il2CppDumper, vgmstream) are downloaded into `work/tools` at pinned versions and checked against sha256 sums.

## Setup

1. Copy `config/local.example.json` to `config/local.json` and fill it in:
   - `base_dump`, `patch_dump`, `apk`: paths to your copies.
   - `unity_editor`: the editor binary, for example `/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity`.
   - `work_dir`: where everything generated goes (default `work/` in the repository; it needs about 60 GB).
   - `phone_page_url`: the address where you will host the phone page (see Phone page).
   - `web_deploy`: how to deploy the phone page (see Phone page).
2. Run `python3 -m pipeline.install_hooks`. It installs git hooks that stop private data (game files, your page address) from being committed.
3. Run `git config iwyk.privateUrl <host of your page>` so the hooks know what to look for.

`config/local.json` and `work/` are ignored by git and never leave your machine.

## Build

```sh
python3 -m pipeline.build_all                    # dumps -> Unity project (about an hour the first time)
python3 -m pipeline.build_native                 # network library for macOS and Windows
python3 -m pipeline.build_player                 # macOS game: work/builds/mac/ThatsYou.app
python3 -m pipeline.build_player --target win    # Windows game: work/builds/win/ThatsYou.exe (experimental)
python3 -m pipeline.build_web                    # phone page: work/web-dist
python3 -m pipeline.deploy_web                   # upload the phone page
```

`build_all` merges the base game and patch, extracts the game and the app, converts the audio, creates the Unity project, applies the port's edits to the game code and imports everything into Unity. Run it again after changing your dumps or updating this repository; it only redoes what changed. Reports land in `work/reports/` (`compile.md` lists any compile errors).

`build_player` copies the port's own code (`unity/Port`) and the network library into the project, builds the player and writes `iwyk-net.json` (your page address and network settings) next to it.

Copy the whole build folder when moving the game to another computer: `ThatsYou.app`, or on Windows `ThatsYou.exe` together with `ThatsYou_Data`, `iwyknet.dll` and `iwyk-net.json`.

## Playing

Start the game, pick a language and get to the main menu. The QR code in the top right corner shows the join address and how many phones are connected; `J` hides or shows it. Each player scans it, taps PLAY, allows the camera and follows the TV. Phones and the computer must be on the same network. The first start on Windows asks to allow network access: allow it for private networks.

The game window keeps the TV's 16:9 shape when resized.

### Controls

The game expects a PS4 controller; on the computer the keyboard stands in for it, and the on-screen button hints show the keys:

| PS4 | Key |
|-----|-----|
| Cross | Enter |
| Circle | Esc |
| Square | H |
| Triangle | P |
| L1 / R1 | K / L |
| Options | Tab |
| D-pad | Arrow keys |

## Phone page

The phone controller is a static web page (source in `web/`). Host it anywhere that serves files over HTTPS and put its address into `phone_page_url` before building the game, because the address goes into the QR code.

Keep the address to yourself. It is never written into this repository, and the hooks refuse commits that contain it.

`python3 -m pipeline.build_web` copies `web/` into `work/web-dist` and adds the original art, fonts and texts from your app export. `--no-art` builds the neutral page, which works the same with plain styling. `--sounds` also copies the app's interface sounds.

`python3 -m pipeline.deploy_web` uploads the page (`--build` builds first, `--dry-run` only prints the plan). Set `web_deploy` in `config/local.json` to one of:

- `{"target": "cloudflare", "worker_name": "<name>", "compatibility_date": "2026-11-01"}` deploys it as Cloudflare static assets with wrangler (wrangler on PATH, or Node.js for npx). The full output goes to `work/web-deploy/deploy.log`; addresses are left out of what is printed.
- `{"target": "folder", "path": "/absolute/folder"}` copies it into a folder served by your own web server.
- `{"target": "rsync", "dest": "<user@host:/folder/>", "delete": false}` runs `rsync -a` (with `--delete` only when `delete` is true).

The page works offline once loaded, keeps the phone screen on while playing, and reconnects by itself after a short network drop.

## Tests

```sh
python3 -m unittest discover -s tests            # pipeline
node --test web/test                             # phone page logic
cd native/iwyknet && go test ./...               # network library
python3 -m pipeline.web_fit --art                # every phone screen fits small and large phones, no scrolling
python3 -m pipeline.web_e2e --all                # phone page against a mock console, headless Chrome
python3 -m pipeline.playthrough --phones 3 --minutes 30
```

`playthrough` runs the real macOS game with several headless Chrome phones (fake camera) that join, take selfies, answer, draw and type through whole games, muted. `--android` adds an Android Chrome (the emulator or a phone with USB debugging) as one of the players; it needs the Android SDK's `adb`. The report lists every screen each phone saw, stuck phones and errors: `work/runs/<name>/playthrough.json`, screenshots next to it.

`python3 -m pipeline.run_player --seconds 60` runs the game alone with screenshots and a summary; `--sound` turns the sound on.

## Repository layout

| Path | Contents |
|------|----------|
| `pipeline/` | Build, deploy and test scripts (Python). |
| `unity/Port/` | The port's own Unity code: desktop platform layer, WebRTC networking, audio, keyboard, window. |
| `native/iwyknet/` | Network library (Go, WebRTC Direct server) used by the game. |
| `web/` | The phone page (plain JavaScript, no build step). |
| `tools/` | Mock console for testing the phone page. |
| `protocol/` | The game's phone protocol messages (names and fields). |
| `config/` | Example configuration. |

## Legal

This project is not affiliated with or endorsed by Sony Interactive Entertainment or Supermassive Games. "That's You!" and PlayLink are their trademarks. Use it only with copies of the game and app that you own.
