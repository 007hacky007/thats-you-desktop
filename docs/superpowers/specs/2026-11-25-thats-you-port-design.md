# That's You! - native PC/Mac port with a web phone controller

Date: 2026-11-25
Status: draft for approval (owner answers from builder check included)

## Intent

"That's You!" (PS4, CUSA08389, Wish Studios / Sony, a PlayLink party game) needs a companion phone app on every player's phone. Those apps are gone from the stores, so the game is unplayable for anyone without an old install. The owner wants the game preserved:

- the game runs natively on Windows x64 and macOS arm64,
- every player uses any modern phone browser instead of an app, with the phone camera and the live camera effects working as before,
- nothing has to be sideloaded onto phones.

Step 1 (this design) builds everything locally from the owner's own backup. Step 2 (later, out of scope here) publishes the work on GitHub as scripts that rebuild the game from a user-provided backup. No copyrighted game or app files may ever go into the repository. Step 2 shapes step 1: every transformation has to be scripted and reproducible.

## Goals

- A native, playable build for Windows x64 and macOS arm64 that plays the full game, including its question packs and modes, as on PS4 with the patch applied.
- A phone web app that reproduces the companion app's screens and functions: join, name/selfie, answering, drawing, photo-taking with live effects, and so on, on iOS Safari and Android Chrome.
- The phone page is hosted on HTTPS at a configurable URL (the owner's server or a Cloudflare Worker deployed with wrangler). The base URL is a game setting.
- After the first visit the phone page works without internet (service worker / installable web app).
- A fully scripted pipeline: inputs are the base dump, the patch dump and the APK. Outputs are the Unity project and the builds.

## Non-goals

- PSN features: trophies, online sharing/upload, grief reporting, NP invitation/friend/commerce dialogs, session WebAPI, telemetry.
- Wi-Fi Direct (the PS4 hotspot mode) and PlayStation Camera features (WebcamControl, libSceFace/Hand/Head trackers).
- Internet or remote play. Phones and game share a LAN.
- New content, remasters or gameplay changes.
- The GitHub publication itself (step 2).

## Anti-goals (would count as failure even if it works)

- Copyrighted material (game assets, decompiled code, APK content) in any public repo or published artifact. The deployed phone page carries original APK art but is each person's own private deployment (see "Phone page assets").
- Phones need a sideloaded app, a certificate profile, or a browser security warning click-through.
- The camera effects are only applied after the photo instead of live.
- A step that only works because someone edited files by hand in the Unity editor and never captured it as a script or patch.
- A dependency on a third-party service beyond static HTTPS hosting.
- Publishing any phone page address in the repo, README or docs. Each person deploys and protects (auth, firewall, private address, whatever they choose) their own page.

## What we start from (facts found)

- **Game:** Unity 5.3.6p6, Mono scripting backend (not IL2CPP). `Media/Managed/Assembly-CSharp.dll` (about 3 MB, full IL), `Assembly-CSharp-firstpass.dll`, `Assembly-UnityScript.dll`, PlayMaker, NGUI (`UICamera`, `UIPanel`, ...), Amplify Color/Occlusion, Boo. The patch (`CUSA08389-patch`) replaces the three Assembly DLLs, assets, levels (adds level14, sharedassets14) and audio. It has no eboot.bin, which is irrelevant because we don't use the eboot.
- **Game native plugins (PS4 only):** about 30 `.prx` in `Media/Plugins` (AkSoundEngine, OggOpus, NativeImageUtil, PS4* wrappers for NP, IME, dialogs, Rudp, NetCtl, WebAPI, PlayGo, JpegEnc/Dec, ...).
- **Audio:** Wwise 2015.1.4, PS4 soundbanks in `StreamingAssets/Audio/GeneratedSoundBanks/PS4`, 10 voice languages (patch adds Czech, Greek, Hungarian, ...). 2.4 GB base plus 1.5 GB patch.
- **Text:** `StreamingAssets/Localisation/*_text.loc`, 22 locales. Question images in `StreamingAssets/QuestionImages`, plus asset bundles in `StreamingAssets/AssetBundles`.
- **Networking (game side):** custom UDP plus Sony RUDP (`PS4Rudp`), discovery (`PS4Discovery`), JSON messages (`JSONRPCReceive/Send`, `ClientServerJSONReceive`, `ClientServerContentSend/Receive` for binary content such as photos and drawings, `ClientServerSendQueue`, `UDPCommsProviderBehaviour`, `Clients`, `ClientJoinRequestManager`, `ClientThumbnails`).
- **Phone app:** `com.playstation.iwyk` 1.6, Unity 2017.4.35f1, IL2CPP (arm64 and armv7), metadata v24, `SymbolMap-ARM64` present. All type, method and field names are recoverable (Il2CppDumper). Method bodies only exist as ARM code. Assets (UI art, fonts, camera effect shaders: Bulge, Grid, Hourglass, Kaleidoscope, Mirror, Shrink, Square, Stretch, Twist, V2Mirror, Warp) are extractable with AssetRipper.

## Spike result (done, throwaway, `spike/webrtc-direct/`)

A Go program (pion/webrtc) and a static page hosted on a Cloudflare Worker proved the connection approach. On an iPhone: live WebGL camera effects in Safari, a direct WebRTC data channel to the desktop over the LAN using only data from the QR code, and a photo sent to the desktop. Desktop Chrome also passed. Android Chrome is not tested yet and is the first item of the plan.

How it works ("WebRTC Direct", the same idea as libp2p webrtc-direct):

- The game listens on one UDP port as an ICE-lite, DTLS-server endpoint with a fixed per-session ICE ufrag/pwd and a self-signed DTLS certificate.
- The QR code on screen holds `<pageURL>#h=<lan ip>&p=<port>&u=<ufrag>&w=<pwd>&f=<sha256 cert fingerprint, base64url>&t=<join token>`. The fragment never leaves the phone.
- The page creates an offer and builds the game's answer locally from those values: ice-lite, setup:passive, a host candidate, and the fingerprint. The DTLS certificate is still verified against the fingerprint, so the connection is authenticated by the QR code.
- The game learns each phone's ICE ufrag from the USERNAME of its first STUN binding request. It demultiplexes all clients on the one UDP port by that ufrag/source address, synthesizes the phone's offer, and answers with forced DTLS server role and client fingerprint verification disabled. The phone's first data channel message must carry the join token.
- One negotiated data channel (id 0). Text messages are JSON, binary messages are chunks (16 KB) of content such as photos.

## Approaches considered

### Game

- **A. Rebuild as a Unity project from the extracted assets and decompiled scripts, upgrade to a modern Unity, build natively. Chosen.** Most work, but it gives real native apps with no emulator and full control over networking and audio.
- B. Run the PS4 build in the shadPS4 emulator with a bridge translating RUDP to the phones. Rejected: not a port, Mac runs under x86 translation, it depends on emulator compatibility, and the bridge must speak Sony's private RUDP.

### Phone

- **1. A hand-built web app (HTML/CSS/JS plus WebGL for camera effects). Chosen.** Small, fast, works in any phone browser.
- 2. Rebuild the phone Unity project and export to Unity WebGL. Rejected: tens of MB per phone, poor camera support on iOS, and the IL2CPP scripts would have to be rewritten anyway.

### Getting live camera on a LAN game without a trusted certificate

- **WebRTC Direct with the hosted HTTPS page (above). Chosen,** proven by the spike.
- The game serves the page over plain HTTP and uses the native camera capture input. Rejected: no live effects.
- Self-signed HTTPS from the game. Rejected: security warnings, unreliable camera access.
- A real certificate for LAN hosts. Rejected: the private key would have to ship in the game and would be revoked.
- A certificate profile on each phone. Rejected: effectively sideloading.

## Design

### 1. Pipeline (scripted, reproducible)

Inputs: path to the base dump, path to the patch dump, path to the APK. Steps, each a script:

1. **Merge:** overlay the patch onto the base into a working copy (patch files win), outside the repo.
2. **Extract game:** AssetRipper (pinned version, command line) exports a Unity project from the merged `Media` with the scripts decompiled from the managed DLLs.
3. **Extract phone app:** AssetRipper on the APK for UI art, fonts, shaders and layouts. Il2CppDumper for type/method/field metadata, used as reference while writing the web app.
4. **Normalize:** scripted fixups to make the exported project open in Unity 2022.3 LTS (API upgrades, removed PS4 assemblies, stubs).
5. **Apply changes (owner decision, option "1c"):** during step 1 we edit the decompiled C# directly, kept as patch files against the normalized export in a **local, unpublished** area. Our new code (networking, audio player, platform stubs) lives in the repo as our own source and is copied in.

   **WARNING - must happen before anything is published (step 2):** the patch files contain decompiled game code as context and removed lines. Before any publication they must be converted to option (a): keep the original game DLLs unchanged and apply our changes at build time with a script that edits the compiled assemblies (Mono.Cecil or Harmony-style method replacement), so the repo holds only programs that name and replace methods, never original code. Until that conversion is done, nothing from this project is pushed to any public remote. Keep the decompiled-source patches organized per change (one patch per concern, with a note on what it does and why) so each can be rewritten as a compiled-code edit later.
6. **Convert audio** (see 4).
7. **Build** with Unity in batch mode for Windows x64 and macOS arm64.

The repo holds only scripts, patches, our own source, the phone web app source, and docs. Generated and extracted material goes to a work directory that is git-ignored.

### 2. Engine upgrade

Target Unity 2022.3 LTS: the oldest LTS with native Windows x64 and Apple Silicon players. Unity Personal is free, so people building from the scripts need no paid license. Known work: NGUI and PlayMaker compatibility with 2022.3 (patch the decompiled versions or substitute the matching publicly available versions), replacing PS4 shaders with the original shader sources where AssetRipper recovers them, or rewriting them where it can't (PS4 compiled shader blobs cannot be used), the legacy `UnityEngine.Networking` (UNET) references, and Boo/UnityScript (`Assembly-UnityScript.dll`, small) converted to C#. Graphics: built-in render pipeline, as in the original.

### 3. PS4 plugin replacement

Every `PS4*`, `Sony*`, NP and system call goes behind a small platform interface with a PC implementation:

- **Save data (MemorySave):** local files in the per-user app data folder.
- **IME / on-screen keyboard dialogs:** the phone or the physical keyboard, as used by the calling flow.
- **Message/error dialogs:** in-game UI or a log.
- **JPEG encode/decode, NativeImageUtil:** Unity `ImageConversion`.
- **OggOpus:** check where it's used. If it's for audio, decode via the audio conversion step, otherwise a managed decoder.
- **Entitlements / add-on content:** no DLC is known to exist for this game (owner). `CheckingOnlineEntitlement` and the `addcont` path for `ClipDirector` are stubbed so that everything present in the inputs is available. `ClipDirector.unity3d` is loaded from `StreamingAssets/AssetBundles`.
- **PlayGo, trophies, NP, WebAPI, telemetry, invitations, Wi-Fi Direct, NetCtl AP mode:** removed or stubbed with no user-visible effect, along with the menus that lead to them.
- **Controller input:** DualShock 4 actions map to keyboard and gamepad via the Unity input system the game already uses.

### 4. Audio

Replace Wwise. Convert the PS4 soundbanks and streamed files to standard audio files (the PS4 encodings must be identified and decoded by script, most likely ATRAC9 or Opus/Vorbis) and extract event/switch/state metadata from the banks. A small C# audio layer replaces the `AkSoundEngine` API surface the game calls (PostEvent, SetSwitch, SetState, SetRTPCValue, bank loading, voice language) and plays the converted files through Unity audio. Voice language follows the game's language setting. Reason: shipping on Wwise would force every builder to register with Audiokinetic and fetch a 2015 SDK, and PS4 banks can't be used on PC anyway.

### 5. Networking

Remove `UDPCommsProviderBehaviour`, Rudp, discovery and the related PS4 code, and put a new comms provider behind the existing client/server message layer (`JSONRPC*`, `ClientServer*`, `Clients`, `ClientJoinRequestManager`), so the game logic sees the same messages as before.

- The WebRTC Direct server from the spike is reimplemented for production. Implementation choice is left to the builder between the pion code built as a native plugin (Go c-shared for win-x64 and mac-arm64) and libdatachannel. The constraint: it must support ICE-lite with fixed credentials, a forced DTLS server role, client fingerprint verification disabled, and demultiplexing many clients on one UDP port by STUN USERNAME.
- Per game session: random ICE ufrag/pwd and join token. The DTLS certificate is persisted per install.
- Join screen: a QR code with the configurable page base URL. The base URL setting is in a config file and, if practical, in the options menu. Default: none, and the game refuses to show a QR code until set (no hard-coded host of ours).
- Reconnection: a phone whose page reloads or briefly loses Wi-Fi must be able to rejoin as the same player. The original rejoin behavior is the reference. The phone keeps its player identity in local storage.
- LAN IP selection: pick the interface with the default route, and allow override in config.
- Firewall: Windows shows a firewall prompt on first run. Document it.

### 6. Phone web app

- Static files (HTML/CSS/JS, WebGL), no framework needed, no build step required but allowed.
- Screens and flows mirror the original app, reconstructed from the APK's scene/prefab layouts and Il2Cpp metadata (class names give the flows: camera, effects panel, drawing, answering, and so on) and from the game's side of each message.
- Camera: `getUserMedia` with a WebGL pipeline. All 11 effects (None, Bulge, Grid, Hourglass, Kaleidoscope, Mirror, Shrink, Square, Stretch, Twist, V2Mirror, Warp) are newly written GLSL ES shaders that reproduce each effect's look and motion, using the app's shaders only as visual reference (our call: keeps app code out of the repo). Front/back camera switching as in the app (`CameraChangeFacingDirectionRequest`).
- Content transfer: JSON over the data channel, binary chunks for photos/drawings in the format the game expects (JPEG and size as in the original).
- Offline after first visit: a service worker caches the page. An "add to home screen" manifest.
- **Phone page assets (owner decision):** the page uses the original app's art, fonts and look, extracted from the owner's APK by the pipeline and bundled into the local build of the page. The repo holds only the page code. The deploy script uploads the local build to the configured target. Each person deploys their own page from their own APK and is responsible for keeping it private (auth, firewall, unlisted address). The owner's deployed address is never written into the repo, README or docs. Config files holding it are git-ignored.
- Language: follows the phone's language where the game has the locale, otherwise the game's setting sent on join.

### 7. Hosting

A deploy script uploads the built phone page to the configured target: Cloudflare Worker static assets via wrangler (as in the spike) or a plain folder for the owner's own server. The game's config stores the resulting base URL.

## Testing

- Pipeline: run end to end from clean inputs and verify the outputs exist and the Unity project opens without compile errors.
- Networking: a headless test client (Chrome via automation and a small scripted client) joins, exchanges messages, and sends a photo.
- Game: a scripted smoke playthrough with N simulated phones driving a full round per mode, plus manual play with real phones (iPhone and Android) on Windows and Mac.
- Audio: a list of all events used by the game, each resolving to converted audio.
- Windows builds are tested in the Windows test VM. Mac builds locally.

## Left to the builder

- The WebRTC server implementation (pion native plugin or libdatachannel) within the constraints above.
- The exact ordering of work and how much of NGUI/PlayMaker gets patched versus replaced with matching public versions.
- The audio decoding toolchain for the PS4 formats once identified.
- Unity 2022.3 patch version (pin it).

## Open risks

- AssetRipper's output for a 5.3 PS4 build may need significant fixups (shaders, NGUI/PlayMaker serialization).
- The PS4 audio codec may need a decoder that is hard to obtain or script. Fallback: decode by other means and document.
- Android Chrome Local Network Access rules may show a permission prompt or block. To be tested first. A one-time permission prompt is acceptable (our call: it's a normal permission like the camera, not a security warning). If Chrome blocks outright, stop and bring options to the owner.
- How much phone app behavior lives only in IL2CPP code (logic not visible from names, layouts or the game's message handling) is unknown. Reading ARM code for specific flows may be needed.
