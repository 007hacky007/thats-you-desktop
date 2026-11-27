# Builder app and self-hosted phone page

## Why

Players should not need a terminal, a domain, a server or a Cloudflare account. A release on
GitHub offers a builder app for Mac (DMG) and Windows (EXE), like horke-leto-remastered: the
player points it at their own copy, it builds a playable game on their computer, and the
release contains nothing from the game or the app.

## Goals

- One window, a few steps, a progress bar, resumable. Builds the game for the system it runs on.
- Game input: the PS4 base game and its update, each as a folder or a .pkg.
- App input (required): the Android APK; the phone page takes its art, fonts and translations from it.
- Phone page hosting without accounts by default, with a one-sign-in Cloudflare option.

## Non-goals

- Cross-building (the Mac builder makes the Mac game, the Windows builder the Windows game).
- Retail encrypted PSN .pkg files: only decrypted backup packages (fake PKG) can be read.
- A Unity-free build: the Unity editor stays a build dependency.
- Hosting anything from the game or the app publicly, or shipping the APK.

## Builder steps

1. **Game.** Pick base and update, folder or .pkg. Tested and supported: the European release
   EP9000-CUSA08389, base 01.00 and update 01.05. Other regions (the North American release is
   CUSA06348) are offered as "untested, try anyway"; the hash-guarded game edits stop with a
   clear message if their code differs. A .pkg is unpacked with a pinned
   PkgTool.Core release (LibOrbisPkg), downloaded on demand and checked by SHA-256. The
   title (CUSA08389) and the update version are checked before anything else runs.
2. **Phone app (required).** Drop the APK. "Find it" opens
   `https://www.google.com/search?q=site%3Awww.apkmirror.com+thats+you+1.6+apk+download`.
   The file must match version 1.6 (1623), SHA-256
   `6bf13282cd189e3a960e56fff0f8f5acd1e4460d831d0c21ee022c55889379d5`; anything else is
   rejected with a clear message.
3. **Phone page.** Two choices:
   - *Use the free Cloudflare tunnel* (default). Nothing to do now.
   - *I have a Cloudflare account.* The builder fetches a portable Node.js and wrangler
     (pinned, checked), runs `wrangler login` (browser sign-in), deploys the page to the
     player's `workers.dev` and stores the address in the game's config.
4. **Unity.** If Unity 6000.6.10f1 is missing, the builder installs Unity Hub and the editor
   (headless Hub install) and asks the player to sign in to a free Unity account once; it
   waits until the licence is active.
5. **Build.** Runs the existing pipeline (unpack, recipes, project, player, page) with a
   progress bar, a log behind "Show details", a free-space check (about 60 GB) and resume.
   Output: `That's You!.app` or a `ThatsYou` folder with `ThatsYou.exe`.

## Self-hosted page (game side)

- The build puts the phone page (with art when an APK was given) next to the game.
- At start, iwyknet serves it on `127.0.0.1:<port>` and starts the bundled `cloudflared`
  (Apache-2.0) with `tunnel --url`. The `https://*.trycloudflare.com` address it reports
  goes into the QR code. The game traffic itself stays on the LAN over WebRTC, as now.
- If a Worker address is configured (account option), no tunnel is started.
- If the tunnel fails (no internet), the QR overlay says so and retries; with no address
  phones cannot join.
- Known costs: internet needed at game start; a new address each launch, so phones ask for
  the camera again; quick tunnels have no uptime guarantee.

## Release

- GitHub Actions builds the builder for macOS arm64 and Windows x64 (Python + PySide6 frozen
  with PyInstaller), with prebuilt iwyknet and the pinned cloudflared inside, and attaches
  the DMG, the EXE, their `.sha256` files and a source archive to a release.
- Unsigned: the Mac app is ad hoc signed (right-click Open), the EXE triggers SmartScreen.
  The release notes say how to get past both.

## Pipeline changes

- Windows: replace APFS clones (`/bin/cp -c`) with plain copies (or block clones where the
  file system has them), Windows paths for Unity and AssetRipper, no POSIX-only calls.
- Inputs accept .pkg; config can come from the builder instead of `config/local.json`.
- Go, cmake and Node are no longer needed by players (prebuilt iwyknet, portable Node only
  for the account option).

## Testing

- Unit tests for every new piece (pkg detection, APK check, hosting choice, tunnel URL
  parsing, step resume).
- Mac: full builder run from an empty work folder, then a playthrough with bots through the
  tunnel address, plus the Android emulator through the tunnel.
- .pkg: a fake PKG made locally from the dumped folders with PkgTool.Core, then unpacked by
  the builder and compared with the folder input.
- Windows: builder run in the test VM up to and including the build; the owner signs in to
  Unity there once. Gameplay on Windows stays experimental (the VM renders at about 0.4 fps).

## Left to the builder (implementer)

Icon: original artwork for this project (no game imagery), used for the window, app, EXE and DMG.
Window layout and wording, PyInstaller specifics, CI runner details, exact pins.
