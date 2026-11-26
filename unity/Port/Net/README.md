# PortNet: phones over WebRTC instead of the PS4 UDP layer

The original game talked to the PlayLink phone app over its own acknowledged-UDP protocol
(`UDPCommsProvider` + `UDPDataComms`). The port replaces the phone app with a web page that
connects straight to the game over a WebRTC data channel ("WebRTC Direct": the QR code carries
the address, ICE credentials, DTLS fingerprint and a join token). This folder is the C# side.
The behaviour sources are `docs/notes/protocol.md` (section 8) and the networking contract
(sections 2 and 4); nothing here is derived from game code beyond names, signatures and the
documented wire constants.

## Architecture

```
phone page --data channel--> iwyknet (Go, pion; native/iwyknet)  -- C ABI, polled -->
  Native/NativeTransport (P/Invoke)  -->  Core/NetEngine  -->  Game/WebRtcDataComms
  -->  the game's UDPCommsProvider (unchanged join policy and consumers)
```

- `Core/` has no UnityEngine and no game types (.NET Standard 2.1 API, C# 9), so it is unit
  tested outside Unity (`tests/test_port_net_cs.py`, tests in `tests/cs_net/`).
  - `NetEngine`: sessions, synthetic peer addresses, 6 slots, join handshake (hello ->
    joined / refused / pending with 1.5 s retries), stale-session replacement (`bye
    replaced`), batch splitting, msg wrap/unwrap without re-serialising, received data
    claimable for 2 s, send completions on the next Update, death timer and bad-connection
    indicator, backpressure (`CanStartSend` false at 1 MiB queued), size caps.
  - `Envelope`, `NetJson`, `GameBatch`, `JoinProbe`, `NetAddr`, `ReceiveStore`, `Liveness`,
    `NetConfig`, `NetLimits`, `INetTransport`.
- `Native/` is the P/Invoke layer (`[DllImport("iwyknet")]`, cdecl, NUL-terminated UTF-8 byte
  arrays, a 5-int event struct, one 131072-byte poll buffer). A missing library is reported
  as the problem text "iwyknet native library not found", never thrown. The constants are
  checked against `native/iwyknet/include/iwyknet.h` by the Python test.
- `Game/` uses UnityEngine and game types and is only compiled together with the game
  (`python3 -m pipeline.csc_check --json work/reports/net-csc.json`).
  - `WebRtcDataComms`: the drop-in for the `UDPDataComms` surface that `UDPCommsProvider`
    uses (same events with the game's delegate types, same methods).
  - `PortNetHost`: config files, certificate path, `[PortNet]` log sink, `Application.quitting`.
  - `PortNetJoin`: static API for the join screen (join URL, QR texture, problem text).

### How the game's join policy is reused

The game decides joins in `UDPCommsProvider.OnRawDataReceive` (online-only refusal, lobby
closed, no free slot, `OnSocketAddressJoined`, the conditional join response). When a phone
says hello, the engine asks `WebRtcDataComms`, which builds the original join datagram in
memory (join magic, nonce 0, UID length, UID) and raises `OnDataReceiveActivity` with it for
the session's synthetic address. The provider runs its checks and answers through
`ConnectClient` and `TrySendRawData`; the adapter decodes a join response as Joined, a refusal
as Refused(reason) and no answer as Pending (retried every 1.5 s). The engine then sends
`joined`, `refused` or `pending` to the page. Normal traffic raises `OnDataReceiveActivity`
with an empty array, which can never match the join magic.

### Addresses and slots

Every session gets a fresh `198.18.x.y` address (benchmark range, little-endian uint as the
game stores IPv4, port offset 0), so a page reload is a new peer, as protocol.md 8.2.2 asks.
When the same device UID says hello again, the old session gets `bye{"replaced"}`, is closed
and its heartbeat is stopped before the new join is decided, so the game marks the old client
disconnected at once instead of waiting 17 s.

## The later wiring patch (not part of this unit)

One small local patch, one concern: in the game's `UDPCommsProvider`, change the declared
type of the private engine field `m_udpDataComms` from `UDPDataComms` to
`PortNet.WebRtcDataComms`, and change its two constructions (the field initializer and the
one in `DEBUG_RecreateObjects`) to construct `PortNet.WebRtcDataComms`. Nothing else in the
game changes: every consumer keeps using `UDPCommsProvider` / `IDataCommsProvider`. This was
verified with a throwaway compile of a patched copy against the game (0 errors).

Notes for that step:

- Enable `PlayerSettings.runInBackground`, otherwise phones stall while the game window is
  not focused (the engine is polled from the main thread).
- Write `iwyk-net.json` next to the build with `python3 -m pipeline.net_config --out <dir>`
  (not wired into `build_player` yet).
- `PS4Wifi.Active` must stay false.
- Recommended separate robustness patches (protocol.md 8.2.12): a timeout on the UGC wait at
  game start, a size check before `Player.SetImageData`, ignoring ImgType 3 outside Top Drawer.
- `UDPCommsProvider.OnDestroy` never reaches the engine, so `PortNetHost` stops it on
  `Application.quitting` (the native side then sends `bye{"shutdown"}`).
- The native library goes to `Assets/PortNative/iwyknet/...` (`pipeline.build_native
  --install`); see native/iwyknet/README.md for the `.bundle` fallback on macOS.

## Configuration

`iwyk-net.json` (UTF-8 JSON object, unknown keys ignored):

```json
{ "phone_page_url": "https://example.invalid/iwyk/", "udp_port": 47000, "lan_ip": "", "log_level": "info" }
```

Later sources win per key:

1. defaults (`phone_page_url` unset, `udp_port` 47000, `lan_ip` "" = automatic, `log_level` info)
2. `<game dir>/iwyk-net.json`: the folder of the executable on Windows, the project folder in
   the editor, the folder that contains the `.app` on macOS
3. `<Application.persistentDataPath>/iwyk-net.json` (written by `PortNetJoin.SetPageUrl`)
4. environment: `IWYK_PAGE_URL`, `IWYK_UDP_PORT`, `IWYK_LAN_IP`, `IWYK_LOG_LEVEL`

Invalid values are ignored with a warning (warnings never echo the page URL). An empty
`phone_page_url` in a file clears it; an empty environment variable is ignored. `udp_port` 0
means any free port; when the configured port is taken the engine tries the next nine.

The DTLS certificate lives in `<Application.persistentDataPath>/iwyknet/dtls.pem` and is
created on first start; keeping the file keeps the fingerprint stable across runs.

## Firewall

The game listens on UDP (default 47000, all interfaces, IPv4). Phones must reach that port
over the LAN: on Windows the first start triggers the Defender Firewall prompt ("private
networks" is enough); on macOS the application firewall may ask to accept incoming
connections. Guest Wi-Fi networks with client isolation block phones entirely.

## Open risks

- Unity resolving `iwyknet.dylib` in the editor and the macOS player is unverified (fallback:
  wrap it as `iwyknet.bundle`). The Windows DLL is cross-built and still has to be run in a
  Windows player.
- The join probe depends on `UDPCommsProvider.OnRawDataReceive` answering synchronously
  through `TrySendRawData`, which matches the current game code; a different provider would
  make every join Pending.
- Send completions are raised one Update after the data was handed to the native queue, not
  when the phone received it. Game transfers to a phone that disconnects meanwhile still
  complete (so game queues never stall).
- `SetEnableHeartbeats` has no effect; the native layer always pings every 2 s.
- The optional native loop test (`NativeTests.NativeLoop`) runs only when
  `work/native/macos-arm64/iwyknet.dylib` and `iwykbot` exist.
