# iwyknet

The game side of the phone transport: a WebRTC Direct server in Go (pion) built as a C shared library
(`iwyknet.dylib` on macOS arm64, `iwyknet.dll` on Windows x64). One UDP port, ICE-lite with fixed
credentials per start, a persistent DTLS certificate and one negotiated data channel (id 0, label
"game") per phone. The phone page connects with the values in the join QR code alone; there is no
signaling server. Wire format and behaviour: the networking contract (sections 2 and 3) and
docs/notes/protocol.md section 8.

## Layout

| Path | What |
|---|---|
| `include/iwyknet.h` | the public C header; it is the ABI contract (hand-written) |
| `server/` | package server: the server as a Go API (used by `capi` and the mock console) |
| `client/` | package client: the page side in Go (tests, mock console bot, iwykbot) |
| `capi/` | `package main` with the `//export iwyk_*` wrappers, built with `-buildmode=c-shared` |
| `cmd/iwykbot/` | JSON-lines page emulator |
| `smoke/smoke.c` | tiny C host using only the header |
| `unity/` | PluginImporter `.meta` templates with fixed GUIDs |

## Build

```
python3 -m pipeline.build_native --platform all --bot     # -> work/native/{macos-arm64,win-x64}/
```

Each platform directory gets the library, a copy of `include/iwyknet.h` (the cgo-generated header is
replaced) and `build-info.json` (go version, sha256, commit, UTC time, compiler, DLL imports).
`--bot` adds `work/native/<host>/iwykbot`. The commands underneath, run in `native/iwyknet`:

```
# macOS arm64 (the linker ad-hoc signs the dylib)
CGO_ENABLED=1 GOOS=darwin GOARCH=arm64 go build -trimpath -buildmode=c-shared -ldflags "-s -w" -o iwyknet.dylib ./capi
# Windows x64, cross-compiled on the Mac (brew install mingw-w64)
GOOS=windows GOARCH=amd64 CGO_ENABLED=1 CC=x86_64-w64-mingw32-gcc go build -trimpath -buildmode=c-shared -ldflags "-s -w" -o iwyknet.dll ./capi
```

Windows compiler discovery: env `IWYK_WIN_CC`, then `x86_64-w64-mingw32-gcc` on PATH, then
`zig cc -target x86_64-windows-gnu`. Without one the Windows build is skipped with a message. On
Windows itself, with MSYS2 or winlibs mingw-w64 gcc on PATH, the same `go build` line without `CC`
works (or `python -m pipeline.build_native --platform win-x64`). Homebrew's mingw-w64 targets UCRT: the
DLL imports only `KERNEL32.dll` and `api-ms-win-crt-*` (checked with objdump by the build; no
libwinpthread or libgcc DLLs), which every supported Windows 10/11 has.

Tests: `go vet ./... && go test ./...` here (loopback WebRTC tests on 127.0.0.1, about 20 s), and
`python3 -m unittest tests.test_build_native` at the repo root (mocked build commands, a real dylib
build driven with ctypes, a real DLL cross build, and go vet/test).

## C ABI summary

See `include/iwyknet.h` for the exact declarations and codes.

- `iwyk_start(udp_port, lan_ip, cert_path)`: binds `0.0.0.0:<port>` udp4 (0 = any free port). `lan_ip`
  NULL/"" selects the LAN IPv4 automatically (route to 192.0.2.1, private ranges, tunnels and virtual
  adapters skipped); otherwise it is advertised as given (LOG warn if not local). `cert_path` holds an
  ECDSA P-256 certificate and its PKCS#8 key (PEM); it is created with mode 0600 and parent dirs when
  missing or unparsable, regenerated when it expires within 30 days, valid 10 years. Same file, same
  fingerprint, so phones that saved nothing still verify the QR's fingerprint. New ufrag, pwd and token on
  every start.
- `iwyk_join_url(base, out, cap)`: `<base>#h=..&p=..&u=..&w=..&f=..&t=..`; base must be http(s), any
  fragment is removed, at most 3800 bytes (4096 output bytes always suffice).
- `iwyk_qr(text, modules, cap, &side)`: level M, no quiet zone, 1 = dark. Works without start.
- `iwyk_poll(&ev, buf, cap)`: 1 = event, 0 = empty, E_SMALLBUF leaves the event queued (`ev.kind`
  and `ev.len` set). 131072 bytes always suffice.
- Per session: OPENED (payload "ip:port"), TEXT (the first is the hello, verbatim), TEXT/BINARY in
  arrival order (phone pings included, so the host can reset its death timer), exactly one CLOSED.
  Peers without a valid hello produce no events. Session ids grow by one and are never reused in the
  process, also across stop/start.
- `iwyk_send_text` (1..65536 bytes), `iwyk_send_binary` (1..131072 bytes, transfer id > 0, chunked
  16384 + 12-byte header): ordered per session, a transfer's chunks are never interleaved.
  E_FULL when the data channel buffer plus the new frames would exceed 8 MiB; `iwyk_buffered` gives
  that buffer for backpressure.
- `iwyk_close`: flush up to 2 s, close, CLOSED(HOST). Later sends and a second close: E_NO_SESSION.
- `iwyk_stop`: `{"t":"bye","reason":"shutdown"}` to every session, up to 500 ms flush, close all,
  discard the event queue. Idempotent.
- `iwyk_last_error`: message of the last failing call (a too-small buffer for it returns E_SMALLBUF
  and keeps the message).

Server behaviour: demux by STUN USERNAME `<our ufrag>:<remote ufrag>` and by remote address (roaming
maps a new address to the existing peer), at most 8 pending handshakes, hello within 10 s of channel
open and 30 s of the first STUN, at most 16 sessions, a 2 s ping to every session, ICE timeouts 5 s /
25 s / 2 s, close on failed/closed only. Incoming text over 65536 bytes, any SCTP message over 262144
bytes, a bad chunk or more than 4 partial transfers close the session with PROTOCOL. The event queue is
capped at 64 MiB / 100000 events; a session whose frame does not fit is closed with PROTOCOL (its CLOSED
is always queued).

## Threading and memory rules

- Every function may be called from any thread; all are serialized by one mutex. Only `iwyk_start`
  (about 1 s at most) and `iwyk_stop` (about 0.8 s at most) block. The intended host is one thread (the
  Unity main thread) that polls every frame and sends.
- No callbacks into the host. Inputs are copied before the call returns; outputs go into caller buffers.
  Nothing allocated by the library crosses the boundary, so there is nothing to free.
- Poll at least every few seconds: the queue is bounded and an overflowing session is closed.
- The library contains a Go runtime and must never be unloaded (Unity does not unload native plugins
  in players; in the editor a domain reload keeps it loaded, which is fine, but call `iwyk_stop` before
  a reload or on play mode exit so the UDP port is released).
- C# P/Invoke: `[DllImport("iwyknet", CallingConvention = Cdecl)]`, `int` for `int32_t`, `byte[]` or
  pinned pointers for buffers, a 5 x int struct (`LayoutKind.Sequential`) for `iwyk_event`.

## Unity placement

`python3 -m pipeline.build_native --install <project>` copies (refusing while Unity holds the project):

- `Assets/PortNative/iwyknet/macOS/iwyknet.dylib` with `unity/iwyknet.dylib.meta` (Editor OSX ARM64 and
  Standalone OSXUniversal ARM64 enabled)
- `Assets/PortNative/iwyknet/Windows/x86_64/iwyknet.dll` with `unity/iwyknet.dll.meta` (Standalone
  Win64 x86_64 only)

Not under `Assets/Port` (the build sync deletes unknown files there) and not under `Assets/Plugins`.
An existing `.meta` with the template's GUID is kept, so Unity may normalise its YAML. Binaries are
never committed.

Risk to verify in Unity: `[DllImport("iwyknet")]` is expected to resolve to the plain `.dylib` in the
macOS editor and player. If it does not, the installer gets a `--mac-bundle` option that wraps it as
`iwyknet.bundle` (`Contents/MacOS/iwyknet` + `Info.plist`) with the same meta settings.

## Windows notes

- First run: Windows Defender Firewall asks whether the game may accept connections on private
  networks. The phones can only connect after "Allow" (private network). If the prompt was dismissed,
  the rule must be added in the firewall settings; the game should say so on its join screen.
- The network profile of the Wi-Fi must be Private, or the firewall blocks inbound UDP by default.
- Checked in the Windows 11 test VM (2026-11-26): `smoke.exe` cross-compiled with mingw-w64 against the
  cross-compiled `iwyknet.dll`, run twice from `C:\bench\iwyk`: `OK abi=1` both times; the first run
  created the certificate, the second reused it.

## iwykbot

```
iwykbot -url '<join url>' [-uid U] [-hello=true] [-ping 2s] [-timeout 17s]
```

Stdout lines: `{"ev":"open"}`, `{"ev":"text","frame":{...}}`, `{"ev":"binary","id":N,"len":L,"sha256":"..."}`,
`{"ev":"closed"}`, `{"ev":"error","msg":"..."}`. Stdin lines: `{"cmd":"hello","uid":"..."}`,
`{"cmd":"text","frame":{...}}`, `{"cmd":"msg","json":{...}}`, `{"cmd":"binary","id":N,"hex":"..."}`,
`{"cmd":"binary","id":N,"len":L}`, `{"cmd":"close"}`. Pings are sent automatically and not printed. Exit
0 after the connection closed (end of stdin alone does not close it; SIGINT/SIGTERM do), 1 when it could
not connect, 2 on bad flags.

## Go API additions beyond the contract

`server.Config` has optional test knobs (`StunTimeout`, `CloseFlush`, `StopFlush`, `QueueMaxBytes`,
`QueueMaxEvents`; zero = contract defaults). `Server.PollLimit(max)` and `Server.SetLogLevel` exist for
the C wrappers. `client.Client` also has `HelloToken`, `SendBinary` (raw frame), `Buffered`, `Done` and
`PingsReceived` for tests.
