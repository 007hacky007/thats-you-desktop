# Game <-> phone protocol

How "That's You!" (PS4) and its companion phone app ("It's Who You Know", `com.playstation.iwyk` 1.6) talk to each other, and what the PC/Mac port has to reproduce over the WebRTC data channel. Everything here was read from the decompiled game C#, the phone app's IL2CPP metadata (`dump.cs`, `stringliteral.json`, `script.json`) and targeted ARM64 disassembly of `libil2cpp.so`. No code is reproduced; behaviour is described in prose.

Conventions:

- `G/` is `work/project/Assets/Scripts/Assembly-CSharp/` (game C#; identical to the original decompile in `work/export/game/...` except the stubbed `PS4Net.cs` and `PS4Rudp.cs`).
- "app" means the phone app. "RVA 0x..." is a method start from `dump.cs`; "at 0x..." is one instruction inside a method.
- All integers on the wire are little-endian. Text is UTF-8.
- The game is the server. Phones never talk to each other.

## 1. Transport (original)

### 1.1 Sockets and ports

- Plain UDP over IPv4. Sony RUDP (`PS4Rudp`, `RudpPeer`, `PS4RoomPeerComms`) is only for PSN play between consoles and is not on the phone path.
- Game: 6 per-peer slots, one receive socket per slot, bound to 9066..9071 (`NetworkConfig.UDPClientSendStartPort` = 9066 plus slot index). Options: SO_REUSEADDR, receive timeout 3000 ms, receive buffer 65536. Reads go into 1024-byte buffers. Bind address is `IPAddress.Any`, or the PS4 hotspot address while Wi-Fi Direct is active.
- Game sends from one unbound socket (ephemeral port) to `phoneIP : 9060 + portOffset` (`UDPServerSendStartPort` = 9060).
- Phone: sends to `game:9066 + offset` and listens on `9060 + offset`. One peer slot only.
- The phone always uses port offset 0 (`ConsoleConnector.Connect`, RVA 0x612818). So every phone lands on game socket 0, and the game tells phones apart only by source IPv4 address. Two phones behind one IP cannot coexist. `UDPPortAssignment` exists but has no callers. Neither side checks the source port.
- A peer address is a `SocketAddress` = (uint32 IPv4 built from the 4 bytes read little-endian, PortOffset). A slot is in use when IPAddr != 0 and PortOffset > -1.
- Dead or unused: broadcast ports 9070/9071, `HeartbeatMessageStart` (4A 72), `UnityNetworkServer` listen port 25104 and password, the own-IP-change disconnect path (guarded by `m_serverCreated`, which is never set true), and the app's UNET/RPC leftovers (the app carries the literal "Only UDP is supported now").

### 1.2 Threads

- Game: one worker thread (`UDPCommsProvider.ThreadBody`) loops every 1 ms: flush sends, drain receive sockets, send queued acks and heartbeats. All protocol state (dispatch, reassembly, send state machines, death timers) advances on the Unity main thread in `UDPCommsProviderBehaviour.Update`.
- App: same structure (`ClientUDPCommsProvider.ThreadBody`, RVA 0x4C9AB0). After one specific caught `SocketException` code it sleeps 5000 ms, during which nothing is sent.

### 1.3 Datagram types

| Type | First bytes | Layout | Notes |
|---|---|---|---|
| Join request | `C1 7C DC 01 43 CF 93` | magic(7), nonce(1), uidLen(int32), UID (UTF-8) | phone -> game, unacknowledged |
| Join response | `6A E4 41 2E B9 41 C1` | magic(7), echoed nonce(1) | game -> phone, unacknowledged |
| Join refuse | `98 AB 64 59 44 4E 2D` | magic(7), reason(1): 1 NotAllowed, 2 NoFreeSlots, 3 OnlyOneAllowed | game -> phone, unacknowledged |
| Data part | `79 22` | id(2), ackedTransferID(int32), numParts(int32), partIdx(int32), partDataSize(int32), partByteOffset(int32), payload | 22-byte header, payload at most 1002 bytes, datagram at most 1024 bytes |
| Ack | `8A 33` | id(2), ackedTransferID(int32), 4 x uint64 missing-part masks (parts 0-63, 64-127, 128-191, 192-255) | exactly 38 bytes; a set bit means "still missing"; all zero means complete |

A data part is valid only if: length >= 22, numParts > 0, 0 <= partIdx < numParts, partDataSize > 0, and 22 + partDataSize <= datagram length.

### 1.4 Reliability layer (UDPAcknowledgedSend / UDPAcknowledgedReceive)

- One transfer at a time per peer and direction (states Idle, Sending, WaitingForAck). Each transfer gets the next per-peer transfer ID (first is 1). The counter is not reset when a slot is reused.
- Sender: when the send cooldown has expired, queue every unsent part in one burst (bounded by a 64-entry part pool shared with acks; the start index rotates by 64 per tick). Then wait for acks.
- Retransmit: after `ackTimeout` (0.5 s) with no send activity, every unacknowledged part is marked unsent again.
- Completion: all parts acknowledged, or an all-zero ack for the current ID. Acks for other IDs are ignored. Completion raises `OnSendComplete(peer, transferID)`.
- AIMD values (identical on both sides): ack timeout 0.5 s constant; send cooldown 0.1 s (min 0.1, max 0.5, -0.1 on success, x2 on failure); part receive timeout 2 s (min 0.4, max 5).
- Receiver:
  - At most 128 parts per transfer (numParts * 1024 must not exceed 131072). The largest deliverable network message is 128 * 1002 = 128256 bytes.
  - Parts are copied into a 131072-byte buffer at their byte offset. Every part triggers an ack with the current missing mask; pending acks for the same ID are merged by AND.
  - The last 10 completed IDs are remembered; duplicates get an all-zero ack. Hazard: a restarted phone (IDs from 1 again) sending before the game resets the old peer can collide with a remembered ID and have its data silently dropped.
  - 16 reassembly buffers per peer; when they run out the part is dropped.
  - Partial transfers are never abandoned by timeout, only freed on peer reset.
  - A transfer with too many parts throws inside the game's main-thread dispatch loop. The loop exits before clearing its list, so the same datagrams are re-dispatched every frame and socket 0 (every phone) is wedged.
- Completed messages go to a `ReceivedData` pool (game 48 entries, app 16, 131072-byte buffers each). Consumers claim them with `TryGetData(transferID, sender)`. Anything unclaimed for 2 s (`UDPUnclaimedReceiveAbandonTimeSec`) is discarded. If no pool entry is free, a non-batch message is dropped after it was already acknowledged.
- The per-socket datagram pool is 100 (game) / 128 (app). On overflow the worker throws, the socket is closed and re-created, and datagrams in flight are lost.

### 1.5 Framing above the reliability layer

Network message (`DataComms.CreateNetworkMessage`):

| Field | Size |
|---|---|
| start bytes `31 54` | 2 |
| applicationTransferID | int32 |
| payloadLength | int32 |
| payload | payloadLength |

The receiver requires header + length to equal the reassembled size exactly; a zero-length payload is rejected.

Batch (`UDPBatch`), carried as a network-message payload:

| Field | Size |
|---|---|
| batch start `84 12 40 EE` | 4 |
| segment: segLen (int32), segTransferID (int32), data | 8 + segLen, repeated 1..N times |
| batch end `32 90` | 2 |

- The receiver recognises a batch by its first 4 bytes and delivers each segment as its own message under its own transfer ID. The outer ID is discarded.
- `ParseBatch` is fragile: if exactly 1 byte remains without an end marker it loops forever on the game's main thread. Segment lengths are not validated.
- Small untracked messages are merged automatically into batches of at most 1410 bytes. Game `ClientServerSendQueue` merges entries with the same peer, priority and broadcast flag, both untracked and not started. App `SocketDataSendQueue` merges on same player, priority, untracked, not started and same contentImageType. A new message can jump ahead of an older non-batchable (tracked) entry. A parser must therefore accept a bare JSON payload and an N-segment batch that holds several JSON messages.

### 1.6 JSON messages

- Application transfer ID -7503 (0xFFFFE2B1, `DataComms.s_JSONMessageTransferID`) for every JSON message.
- Payload: compact UTF-8 JSON from Unity `JsonUtility.ToJson` on both sides (game `JSONHelper.JsonSerialize`; app `JSONConvertor.SerialiseObject`, RVA 0xEADD84). The app parses incoming JSON with MiniJSON into a dictionary and per-class `Deserialise` methods.
- Every message has `TypeString` = the bare C# class name (it is a base-class field, so it comes first). Field names are the public C# field names, case-sensitive. Enums are integers. `List<string>` is a JSON array. Missing fields keep the class default, which is usually a sentinel that fails validation.
- Dispatch: the receiver reads `TypeString`, resolves the type with `Type.GetType`, and offers the JSON to handlers that match the type exactly. Unknown types are dropped silently. Most handlers deserialise and call `GetIsValid()`; invalid messages are dropped silently. Senders never validate.
- Example: `{"TypeString":"ClientRequestPlayerIDMessage","UID":"<device uid>"}`.
- GUIDs are .NET `Guid.ToString()` strings (lowercase, 36 characters with hyphens) and are compared as exact strings. Where the game parses a GUID (`new Guid(...)`), a non-GUID string throws a caught `FormatException` and the message is silently ignored.

### 1.7 Binary content

- Content (photos, drawings, thumbnails, audio) is a 2-segment batch: segment 1 is the JSON header (ID -7503), segment 2 is the raw bytes (ID = the header's `TransferID`). Framing overhead is 22 bytes (4 + 8 + 8 + 2).
- Game -> phone: header and data segment IDs are N, and the outer network transfer ID is also N; the transfer is tracked (completion comes from `OnSendComplete`). If a queued transfer already uses N, `SendQueue.InitiateTransfer` picks a fresh outer ID.
- Phone -> game: the app takes N = counter + 1 for the header and data segment, then queues the batch with another increment (N + 1) as the outer ID, untracked, priority 50. contentImageType is EncodedPlayerPortrait (0) for portraits, otherwise NotSet (1000). So each upload consumes two IDs, and the outer ID is not the segment ID.
- The receiver handles the JSON header first, which registers a pending receive, then claims the data with `TryGetData(TransferID)`. Safe order: header first, or both within 2 s (unclaimed data is dropped after 2 s). A pending header retries every frame until 30 s without activity. A header that fails validation is ignored and its data ages out.
- Size limits:
  - Send buffers are 131062 bytes (`MaxContentSendSizeBytes`, `UDPBatch.BatchBuffer`, `SendQueue.DataTransfer`). Rule: JSON + data + 22 <= 131062.
  - The receive side only accepts 128 parts, so on the original transport the largest payload is 128246 bytes. Payloads between 128247 and 131062 pass the sender's checks but wedge the game's receive socket (section 1.4).
  - The app's batch buffer is only 65536 bytes. Larger content makes `MakeBatch` return 0 and the app sends an empty payload, which the game rejects. Original phone uploads were therefore always under about 65 KB.
  - The game does not handle oversized data cleanly: `Player.SetImageData` copies into a 131062-byte store without a check (throws), and `MakeBatch` failure leaves a content transfer that never completes. The port must enforce the cap itself.
- No compression, encryption, checksum or authentication anywhere.

### 1.8 Send queues

- Game `SocketAddressSendQueue` (used by `JSONSocketAddrSend`, `JSONRPCSend`) is keyed by socket address and works before a phone has a UID. `ClientServerSendQueue` (used by `JSONSend` and content) is keyed by player UID.
- Socket-address traffic has precedence: UID traffic to a peer waits while any socket-address transfer to that peer exists, or while the peer is marked `SetNoTransfersToClient` (set on join, cleared when `AssignPlayerIDAndSlotMessage` is sent). So `AssignPlayerIDAndSlotMessage` always arrives before any UID-path message.
- Entries for disconnected clients are skipped. Lower priority values go first (Default 50, PortraitRelay 100). Pools of 256 entries are soft limits.
- Ordering is FIFO only within one queue, one priority and runs of batchable entries.
- `JSONSend.SendMessage(msg, uid)` is silently dropped when the UID is not a known local client. `SendMessageToAll` goes to all clients in the lobby and to the game's local players during a game.

### 1.9 Keepalive and disconnect detection

- Game heartbeat: about every 1 s (`ConnectionHeartbeatFrequency`), the worker re-sends the last ack to each peer (or an initial ack with ID -1 and zero masks) when no other ack or data went out. Enabled by `PlayerNetworkManager`.
- Phone heartbeat: decided on the main thread (`UDPAcknowledgedReceive.EnqueueHeartbeat`, RVA 0x5E1D14) every 1.0 s. Queued acks (heartbeat and data acks) are only sent while `WifiInformation.IsWifiAvailable` is true.
- Game death timer: 17 s per peer (`ConnectionDeathTime`), reset by every datagram from that peer, counted down with scaled `Time.deltaTime` on the main thread. At 0: `StopHeartbeat` -> `ResetPeer` (clears send/receive state, slot address, pending datagrams and queued sends) -> `OnHeartStopped` -> `PlayerNetworkManager.OnUDPHeartStopped` (Connected = false) -> `UnityNetworkServer.OnPlayerHeartbeatStopped` -> `ClientJoinRequestManager.DisconnectClient`.
- Game "bad connection": indicator follows 0.999 * indicator + 0.001 * timer, snapping down when the timer drops below both 6.5 s and the indicator. Bad when below 6.5 s.
- Second game rule: `PlayerNetworkManager.Update` stops the heartbeat of any client whose acked sender has gone 30 s without a newly acknowledged part (`GetActiveSendTimeSinceAck`, 0 while idle). While the lobby is open it also stops local clients whose death time is <= 0.
- Phone death timer: 17 s (`OneRPCConsoleConnectivityController`, Update RVA 0x6E5B04), reset by every datagram, counted with unscaled time, only while heartbeats are enabled. Bad connection below 0.666 * 17 (about 11.3 s), shown by overlay PARTY_GAME_CONNECTION_STATUS. At 0 `OnDisconnected` (RVA 0x6E5ED4) runs. The countdown also runs while trying to connect, so 17 s with no reply after Connect shows "No game found".
- Unused on the phone: `noReceiveDisconnectTimeSec` 3600 and `heartbeatSendFrequencySec` 5.

## 2. Discovery

- Original: the phone used Sony's Android/iOS second-screen library (`com.playstation.companionutil`, Device Discovery Protocol: UDP broadcast `SRCH * HTTP/1.1`, `device-discovery-protocol-version:00020020`), title ID `NPXS29001_00`. The PS4 system software answers, not the game; the game's `PS4Discovery` is an empty stub.
- The phone keeps only consoles whose `running-app-name` exactly matches a line of `ExportedProject/Assets/TextAsset/ps4AppName.txt` (18 lines: 9 localised titles, each also with a trademark sign; lines are trimmed).
- Choice (`OneRPCConsoleConnectivityWorker`, values from `App-Master.unity`): after at least 1 s, one console is auto-selected; 2 or more open CHOOSE_PS4; none for 10 s shows "No game found". Manual IP entry and Wi-Fi Direct (hotspot subnets 192.168.52/53, console at `.1`) are alternatives.
- The chosen IP and name are saved as PlayerPrefs `OLD_SERVER_IP` / `OLD_SERVER_NAME` for the Reconnect button.
- Port: none of this is needed. The QR code fragment (`h`, `p`, `u`, `w`, `f`, `t`) replaces discovery, the console list, manual IP and Wi-Fi Direct.

## 3. Join, rejoin and leave

### 3.1 Raw join handshake

Phone side (`ConsoleConnector`):

1. `Connect` sets port offset 0, disables data receive, picks a nonce once per attempt (`Random.value * 126`, so 0..126) and starts retrying.
2. Every 1.5 s (`s_joinRetrySec`) it sends the join request with the DeviceUID.
3. A join response whose nonce matches stops the retries, raises `OnConnectedToServer` and enables data receive. A refusal stops retries and raises `OnConnectionRefused(reason)`. No game data is accepted before that.

Game side (`UDPCommsProvider.OnRawDataReceive`), checks in order:

1. Online-only mode, sender has no slot, another client connected: refuse 3 (OnlyOneAllowed).
2. `GetUIDAllowedToJoin(uid)` false (lobby closed and UID unknown): refuse 1 (NotAllowed).
3. `UDPDataComms.ConnectClient` fails (all 6 slots taken): refuse 2 (NoFreeSlots). An address that already has a slot succeeds.
4. Raise `OnSocketAddressJoined` (again on every retry from an address that holds a slot).
5. Send the join response only if `GetSendJoinResponseToUID(uid)` is true, i.e. that UID is not still marked Connected. Otherwise nothing at all is sent back.

`GetUIDAllowedToJoin` reads `uidLen` without bounds checking it against the datagram.

`UnityNetworkServer.OnSocketAddressJoined`:

1. Advisory subnet check under PS4 Wi-Fi Direct: on mismatch it queues `DisconnectCommandMessage{Msg:ConnectionRejected}` but does not return.
2. If the socket-address queue has nothing pending for this address and the address belongs to a client that is still connected, it sets that client's death time to near zero and returns. The stale session dies on the next Update; the phone's next retry then joins cleanly.
3. Otherwise (nothing pending) it sends `InterfaceVersionMessage` and `SessionStateMessage` by socket address.
4. It always blocks UID-keyed sends to that address until `AssignPlayerIDAndSlotMessage`.

Phone reaction to refusal (`OneRPCConsoleConnectivityController.OnConnectionRefused`, RVA 0x6E7F74): reason 1 -> disconnect message 7 (ConnectionNotAllowed), 2 -> 2 (GameFull), 3 -> 6 (OnlyOnePlayer), anything else -> 4. If already rejecting, it disconnects silently.

### 3.2 Message-level join (fresh player, lobby)

| # | Direction | Message | Notes |
|---|---|---|---|
| 1 | P -> G | raw join request | retried every 1.5 s |
| 2 | G -> P | raw join response | only if UID not still Connected |
| 3 | G -> P | `InterfaceVersionMessage{InterfaceVersion:"2016-07-12-1645"}` | socket path |
| 4 | G -> P | `SessionStateMessage{SessionID}` | random int per game process, must be > 0 |
| 5 | P | compare version | mismatch: disconnect with 5 VersionDisparity |
| 6 | P | load saved profile if stored SESSION_ID string matches, else clear it | see 3.5 |
| 7 | P -> G | `ClientRequestPlayerIDMessage{UID}` | identity is the UID only; old player ID is not sent |
| 8 | G -> P | `AssignPlayerIDAndSlotMessage{PlayerID, SlotID, UDPPortOffset, DisplayName, PSNID}` | socket path; unblocks UID path |
| 9 | G -> P | `SyncJokersQuizStateMessage`, `ClientOnlineStateMessage{IsOnline:false}`, `ClientUGCStateMessage`, empty `ResourceRequirementsMessage` | UID path; UGC state may first be broadcast to all |
| 10 | P -> G | `DeviceInfoMessage` | sent when the profile flow starts |
| 11 | G | catch-up state Initial -> Joined -> Current on the next `TargetClientState.Update`, sending the current target-mode command | |
| 12 | G -> P | `ClientQuizCommandMessage{action:31 EditProfile}` | from `FrontEndFlow.SendProfileEntryCommands` once the player is active, otherwise later through catch-up |
| 13 | P -> G | portrait upload (`ClientImageResourceContentTransferMessage` ImgType 2 + JPEG), `ClientPlayerProfileMessage` | |
| 14 | G -> P | thumbnails (`PortraitThumbnailContentTransferMessage`), LOOK_AT_TV holding screen | |
| 15 | G -> P | `ClientQuizCommandMessage{action:14 StartGameButtonPrompt}` | when at least 2 players and all have names |
| 16 | P -> G | `StartGameButtonPressedResponseMessage` | any phone starts the game |

The raw response normally arrives first; rows 3 and 4 are queued and retransmitted until the phone (now receiving) acknowledges them.

### 3.3 Players, slots and IDs

- Maximum 6 players (`PlayerManager.s_MaximumPlayers`); minimum 2 to start. `PlayerNetworkManager.m_MaxNetworkPlayers` = 16 caps the client list (including disconnected entries).
- PlayerID: increasing, never reused in one run (`ChooseNewPlayerID`; the first ID is 2).
- SlotID 0..5: first free slot at join. At game start and after removals slots are renumbered to 0..n-1 (`MakeSlotsContiguous`, `ReassignSlots`) and every local phone gets a new `AssignPlayerIDAndSlotMessage` with the same PlayerID. Phones must accept repeated assignments.
- `Host_TryAddLocalClient` outcomes in order: known local UID reuses its player (rejoin); a socket address already owned by a local client reuses that player even with a different UID; lobby open with room creates a player; otherwise `DisconnectCommandMessage{Msg:GameFull}`. With the lobby closed, a found client whose ID is not in the game roster gets no reply at all.
- `_TryInitializeClient` moves the client to the sender's address, sets Connected, resets the death time and sends rows 8 and 9.
- Identity on screen: slot index (per-slot UI and "Player N" default name `PreProfile_PlayerName_1..6`), personality card `CARD_1`..`CARD_12`, and the selfie. No colour field exists in the protocol.

### 3.4 Disconnect, rejoin and leave

- Lobby: a disconnected player (17 s silent, or `ClientDisconnectMessage`) is permanently removed; others get `PlayerLeftMessage` if they had details. Coming back means a new PlayerID with the same UID; the phone restores its saved name, card and selfie locally, and the game sends EditProfile again.
- In game: the client stays with Connected = false and is shown as away (`OfflinePlayerAwayNotification`). Flows treat it as missing and stop waiting for it. Disconnected locals are purged on return to the front end.
- Rejoin in game: same UID -> same PlayerID and slot. The game re-sends `AssignPlayerIDAndSlotMessage` and the join set, `PlayerJoinedMessage` + `PlayerNameQuizStateMessage` for every other player, and on catch-up the current target-mode command plus, where relevant, joker screen or Continue, scenario transition, Pause, `RejoiningClientOwnProfileMessage{Name, PortraitGUID, CardID}` and `ClientGameIDMessage`. TV notification `Notif_PlayerRejoin`.
- Mid-question rejoin: catch-up is disabled from `StartQuestionFlow` until `IterationCompleteFlow` / `WinSequenceFlow`. The phone gets `ClientHoldingScreenCommandMessage{HoldingScreenType:8 REJOIN}` at once (plus Pause if paused) and waits for the next question.
- Same-address rejoin of a still-connected UID: handled by the "death time near zero" shortcut in 3.1. Different-address rejoin of a still-connected UID: InterfaceVersion and SessionState are sent, but no raw response until the old peer times out (up to 17 s).
- Leaving: the phone's confirm popup sends `ClientDisconnectMessage{}`; the game calls `StopHeartbeat` (same path as a timeout).
- Kick/refuse: `DisconnectCommandMessage{Msg}`. A GameFull refusal at this stage leaves the UDP peer slot taken until its 17 s timer expires.
- Phone reaction to any disconnect: disconnect request, back to the INITIAL screen, close party overlays, show the popup for the reason. Only ConnectionFailed (3) opens DISCONNECTED_POPUP with a Reconnect button; Reconnect re-runs the normal join with the saved server IP. There is no automatic reconnect loop (`ConsoleConnector.reconnectTime` = 5 is unused).
- App backgrounded / screen locked: nothing is sent; the 17 s timers decide.

### 3.5 What the phone persists (PlayerPrefs)

`SESSION_ID` (string), `OLD_PLAYER_ID`, `OLD_PORTRAIT_ID`, `OLD_CARD`, `OLD_NAME`, `OLD_SERVER_IP`, `OLD_SERVER_NAME`. `LoadOldSessionData` compares the received SessionID as a string: on a match it restores player ID, portrait, card and name; otherwise it deletes the stored portrait and saves the new session ID.

## 4. Message catalogue

Direction: P -> G is phone to game, G -> P is game to phone. "Path" for G -> P is socket (by address) or UID. Validity rules are the receiver's `GetIsValid`; a failing message is dropped silently.

### 4.1 Shared enums (integers on the wire)

| Enum | Values |
|---|---|
| `eAction` | NotSet -1, Idle 1, JokerScreen 2, AnswerQuestion 3, ContinueButton 4, HoldingScreen 5, Drawing 7, TextEntry 8, ScenarioTransition 9, RoleCall 11, TakeDrawingCanvasPhoto 12, WishMessage 13 (game only), StartGameButtonPrompt 14, EndOfGameMessage 15, SendAnswer 16, SendJoker 17, SendTextEntry 18, SendDrawing 19, UGCSelection 20, MessageCollection 21, ActivityMessage 22, MimicThis 23, SendMimicThisPhoto 24, QuickDrawTakePhoto 25, QuickDrawDrawing 27, SendQuickDrawDrawing 28, Pause 29, Unpause 30, EditProfile 31 |
| `eHoldingScreenType` | NONE 0, BLANK 1, OTHER_PLAYER_CHOOSING_UGC 2, OTHER_PLAYER_TAKING_PHOTO 3, LOOK_AT_TV 4, QUICK_DRAW 5, CUSTOM_MESSAGE 6, QUICK_DRAW_MESSAGE 7, REJOIN 8, STRING_HACKS 99 |
| `RoundType` (bits) | TopDrawer 1, ThatsYou 2, PictureThis 4, CanvasOpinion 8, ThatsYouGC 16, BlankYou 32, MimicThis 64, WouldYouRather 128, QuickDraw 256, NoType 512 |
| `eDisconnectDisplayMessage` | Default 0, GameFull 2, ConnectionFailed 3, ConnectionRejected 4, VersionDisparity 5, OnlyOnePlayer 6, ConnectionNotAllowed 7 (phone only) |
| `ResponseType` | NotSet 0, AnswerIndex 1, JokerConsumed 2, ContinuePressed 3, UsePicPressed 4, TextAnswer 5, PlayerName 6, UGCDeclined 7, PlayerCard 8, StartButtonPressed 9, DeviceInfo 10, TopDrawerPhotoTaken 11 |
| `ImageType` | Unspecified 0, PlayerDrawing 1, PlayerPortrait 2, DrawingCanvas 3, MimicPhoto 4, QuickDraw 5 |
| `eEnabledUgcState` (flags) | 0 enabled, DISABLED_ONLINE 1, DISABLED_INSTALLING 2, DISABLED_FIRST_RUN 4, DISABLED_UNSPECIFIED_REASON 8 |
| `AudioCodec` | NotSet -1, Speex 0, Opus 1 |
| `eDeviceSize` | UNKNOWN 0, SMALL 1, MEDIUM 2, LARGE 3 |
| `eDeviceOS` | UNKNOWN 0, ANDROID 1, IOS 2, WINDOWS 3, MAC 4, LINUX 5, WINPHONE 6 |
| `ResourceRequirements.Requirement` | NoRequirement 0, Required 1, SafeToUnload 2 |
| UGC `scenario` | NoScenario -1, Scenario_01..12 = 1..12, InvalidScenarioType -100 |
| `AcknowledgeContentTransferMessage.TransferType` | AcknowledgeComplete 4 |

### 4.2 Phone -> game

| Message | Fields | Game validity | When / effect |
|---|---|---|---|
| `ClientRequestPlayerIDMessage` | UID | UID non-empty | after InterfaceVersion and SessionState; accepted from unknown addresses and from known clients (rejoin) |
| `DeviceInfoMessage` | Response=10, DeviceSize, DeviceOS, DeviceModel, DeviceType, DeviceUID, DeviceOperatingSystem (game class also has DeviceName, never sent) | Response 10, size and OS not 0 | stored in `ClientDeviceInfo`; no reply |
| `ClientPlayerProfileMessage` | playerName, uppercasePlayerName, deviceCultureName, isGuest (0 existing, 1 guest), playerCardId, ugcInfo{scenarioInt, scenario, challengeID, creationID, set} | four strings non-empty, isGuest 0 or 1 | end of profile flow; queued until the client exists; sets name, card, guest flag, UGC info; a set `ugcInfo` later triggers UGCSelection |
| `ClientDisconnectMessage` | none | always | deliberate leave; game stops the heartbeat |
| `AnswerIndexResponseMessage` | AnswerIndex | >= -2 | the player's pick; meaning per round in 4.5 |
| `JokerConsumedResponseMessage` | Response=2, jokerConsumed | Response 2 | reply to SendJoker; `ConsumeJoker` |
| `ContinuePressedResponseMessage` | Response=3 | Response 3 | reply to ContinueButton |
| `UsePicPressedResponseMessage` | Response=4 | Response 4 | photo confirmed (Top Drawer, QuickDraw); marks ProgressionButtonPressed |
| `TextAnswerReponseMessage` (spelling as in source) | Response=5, Text | Response 5, Text set | reply to SendTextEntry (Blank You) |
| `StartGameButtonPressedResponseMessage` | Response=9 | not validated; type name alone is enough | reply to StartGameButtonPrompt |
| `TopDrawerPhotoTakenResponseMessage` | Response=11 | Response 11 | at the shutter in Top Drawer; TV plays a flash |
| `TrophyPhotoFXInfoMessage` | ImageGUID, usesFX | always | after each photo upload; trophy tracking only |
| `TrophyDrawingColouringInfoMessage` | ColoursAvailable, ColoursUsed (bitmasks over `eDrawingColour`; ImageGUID unused) | always | after a drawing; trophy tracking only |
| `ClientImageResourceContentTransferMessage` + data | TransferID, ImageGUID, ImgType | TransferID > 0 | image upload; empty GUID means the game makes one; type routing in 4.6 |
| `ClientImageUGCContentMessage` + data | challengeId, metadata, imageId, TransferID | always | reply to UGCSelection with a drawing |
| `ClientQuestionUGCContentMessage` | challengeId, metadata, question | always | reply to UGCSelection with written content |
| `ClientAudioClipContentTransferMessage` + data | TransferID, Codec | TransferID > 0, Codec != -1 | voice chat clip; offline only cached and relayed |
| `CancelContentSendMessage` | TransferID | > -1 | drop a pending receive (the app only cancels an unstarted portrait) |
| `RequestResourceMessage` | ResourceGUID | always (non-GUID silently ignored) | ask for an image; reply is `ImageResourceContentTransferMessage` + JPEG unless already queued |
| `AllResourcesReceivedMessage` | Requirements: [{GUID, Rqrmnt}] | always | reply to every ResourceRequirementsMessage once Required items are held |
| `ClientSendChatLogMessage` | PlayerId, Message, TickTimestamp | always | chat line; forwarded to other phones; PlayerId must be the sender's own ID (> 0) |
| `AcknowledgeContentTransferMessage` | TransferType=4, TransferID, Successful | type 4, ID > -1 | accepted by the game, never sent by the original app |

Dead on the game side (the game has no such class): `ImageContentTransferMessage`, `DrawingCanvasContentTransferMessage`. Never sent by the app: `WishResponseMessage`, `JoinResponseMessage`, `PlayerNameResponseMessage`, `PlayerCardResponseMessage`, `UGCDeclinedMessage`.

### 4.3 Game -> phone: session and roster

| Message | Fields | Path | Phone validity | When |
|---|---|---|---|---|
| `InterfaceVersionMessage` | InterfaceVersion | socket | not null, then must equal "2016-07-12-1645" | on join |
| `SessionStateMessage` | SessionID | socket | > 0 | on join |
| `AssignPlayerIDAndSlotMessage` | PlayerID, SlotID, UDPPortOffset, DisplayName, PSNID | socket | three ints >= 0 (strings not checked) | join, rejoin, slot renumbering |
| `DisconnectCommandMessage` | Msg | socket | always | refused join, kick, Wi-Fi Direct subnet mismatch |
| `JoinLeaveMessage` | none | UID | always | online only |
| `ClientOnlineStateMessage` | IsOnline | UID | always | join, online change (false in the port) |
| `ClientUGCStateMessage` | ugcState | UID / all | always | join, flag changes |
| `PlayerJoinedMessage` | CurrentPlayerID, OldPlayerID (= current), SlotID | UID | CurrentPlayerID and SlotID != -1 | game start (one per other player), rejoin, after mid-game removals; not in the lobby |
| `PlayerNameQuizStateMessage` | PlayerName, PlayerID | UID | not sentinel, PlayerID >= 0 | paired with PlayerJoined |
| `PlayerLeftMessage` | PlayerID | UID | != -1 | game end/quit (one per other player), removals |
| `RejoiningClientOwnProfileMessage` | Name, PortraitGUID, CardID | UID | Name and CardID non-empty | in-game catch-up |
| `ClientGameIDMessage` | GameID ("1"+GUID front end, "2"+GUID in game) | UID | non-empty | game start, game end, catch-up |
| `SyncJokersQuizStateMessage` | NumJokers | UID | >= 0 | join, game start, after scoring and scenario transitions |
| `ClientReceiveChatLogMessage` | PlayerId, Message, TickTimestamp, PSNID, ClientUID, DisplayName, SlotID | UID | always | chat from another phone |
| `AcknowledgeContentTransferMessage` | TransferType, TransferID, Successful | - | type 4, ID >= 0 | never sent to phones in the client-server path |

### 4.4 Game -> phone: commands

All commands derive from `ClientQuizCommandMessage{action, time}`. `time` is 0.0 except for `ClientJokerCommandMessage` (-1.0). Commands go only to active players (connected and caught up) unless noted. The phone queues them and runs one `Execute*` handler per action (`ClientQuizCommandSchedulerHandler`).

Base class `ClientQuizCommandMessage` (phone validity: action != -1):

| action | Sent by | Phone effect | Expected reply |
|---|---|---|---|
| 31 EditProfile | `FrontEndFlow.SendProfileEntryCommands` (new lobby player), `SendRestartProfileMessages` (all phones at front-end start; clears names) | starts the profile flow | profile, portrait, DeviceInfo |
| 14 StartGameButtonPrompt | `SetShowStartButton` when everyone has a profile | PARTY_CONTINUE screen in start mode | StartGameButtonPressedResponseMessage |
| 4 ContinueButton | `ShowContinueButton` (resent periodically) | PARTY_CONTINUE screen | ContinuePressedResponseMessage |
| 16 SendAnswer | `CleanUpAfterConsensusQuestionFlow`, `CleanUpAfterTextAQuestion` | submit current selection now | AnswerIndexResponseMessage (game waits up to 30 s) |
| 17 SendJoker | same, plus `CleanUpAfterPlayerDrawingConsensusQuestionFlow`, when jokers enabled | submit joker choice | JokerConsumedResponseMessage (30 s) |
| 18 SendTextEntry | `ReceiveAndDistributePlayerText` | submit typed text | TextAnswerReponseMessage (60 s, 300 s online) |
| 19 SendDrawing | `ReceiveAndDistributePlayerDrawingsFlow`, `ReceiveAndDistributeDrawingsBFlow` | submit drawing | image upload ImgType 1 |
| 24 SendMimicThisPhoto | `ReceiveAndDistributeMimicThisFlow` | submit mimic photo | image upload ImgType 4 |
| 27 QuickDrawDrawing | start of each QuickDraw drawing pass | open drawing on the received image | none |
| 28 SendQuickDrawDrawing | end of each pass | submit drawing | image upload (game waits 30 s, 300 s online) |
| 15 EndOfGameMessage | `QuitGame.SetClientStateEndOfGame` | end session, back to JOINED_GAME | none |
| 29 Pause / 30 Unpause | `PauseGame` to every client (own instance) | PAUSE overlay | none |

Never sent by the shipped game: Idle 1, RoleCall 11, WishMessage 13, MessageCollection 21, ActivityMessage 22. JokerScreen 2 in base form is dead code.

Caveat: EditProfile, StartGameButtonPrompt, EndOfGameMessage, SendAnswer and SendJoker share one message instance on the game side, and the Pause catch-up path overwrites its `action`. A catching-up phone can receive a stale action instead of its own mode's command.

Subclasses:

| Message | Extra fields | Phone validity | Sent when | Phone effect / reply |
|---|---|---|---|---|
| `ClientHoldingScreenCommandMessage` (action 5) | HoldingScreenText, HoldingScreenType, OtherPlayerIndex, ShowPortraitPhotoControls | action 5, text not `***NOTSETHOLDINGSCREENTEXT`, type != 0, OtherPlayerIndex > 0 for types 2 and 3 | LOOK_AT_TV to all between steps; LOOK_AT_TV to named lobby players; OTHER_PLAYER_TAKING_PHOTO (with target name and ID) to everyone but the Top Drawer target; REJOIN with empty text | holding screen by type (phone-app.md); no reply |
| `ClientQuickDrawHoldingScreenCommandMessage` (action 5, type 5) | ImageGUID, Heading, Description, NumberOfDrawingsRemaining, PlayerName; text stays the sentinel | action 5 and type 5 only | once per QuickDraw iteration, to one destination | QUICK_DRAW_HOLDING screen; must not be rejected for the sentinel text |
| `ClientTextEntryCommandMessage` (8) | TitleText | not `***NOTSETTEXTENTRYTITLE` | Blank You writing phase | TEXT_ENTRY screen; text sent on SendTextEntry |
| `ClientAnswerCommandMessage` (3) | roundID (QuickDraw sent as CanvasOpinion), questionBarText, jokersEnabled, ImageGUIDs | not `****NOTSETQUESTIONBARTEXT`, roundID != 512 | `SetAnsweringWithQuestionResources` (portraits or question images) and `SetAnsweringWithPlayerDrawings` (drawings, random order; slot order for QuickDraw; players without a drawing skipped) | CHOOSE_PLAYER_ANSWER screen; AnswerIndexResponseMessage |
| `ClientStartDrawingCommandMessage` (7) | CanvasGUID | action 7 | `DrawingBFlow`, `PoseTopDrawerDrawingFlow` | DRAWING screen with that background; upload on SendDrawing |
| `ClientTopDrawerTakePhotoCommandMessage` (12) | CommandText (may contain an unformatted `{0}`), PlayerName (not in the app class) | action 12 | only to the Top Drawer target | photo screen; TopDrawerPhotoTaken, UsePicPressed, upload ImgType 3 (game waits 360 s) |
| `ClientMimicThisCommandMessage` (23) | MimicPictureGUID | action 23 | `MimicThisQuestionFlow` | MIMIC_PHOTO screen; upload ImgType 4 on SendMimicThisPhoto |
| `ClientQuickDrawTakePhotoCommandMessage` (25) | CommandText | action 25 | each local player individually | photo screen; upload ImgType 5 |
| `ClientScenarioTransitionCommandMessage` (9) | Scenario (-1 at end) | action 9 | every scenario change, win sequence, quit | background and card art change |
| `ClientJokerCommandMessage` (2) | PreNumJokers, CurNumJokers | both >= 0, action 2 | when a joker count changes | JOKER_EARNED screen |
| `ClientSelectedUGCRequestMessage` (20) | UGCCreationId | action 20 | game start, to players whose profile had `ugcInfo.set` | sends the selected creation; the game waits for it with no timeout |
| `ClientEndOfGameMessage` | none | always | never sent by the game (app-only class) | - |
| `WishCommandMessage` (13) | AnswerGUID, QuestionDataString | - | never sent; not in the app | - |

### 4.5 Game -> phone: per-question data and resources

| Message | Fields | Phone validity | When |
|---|---|---|---|
| `QuestionDataMessage` | RoundType, QuestionText | always | `StartQuestionFlow`, to all, followed by a ResourceRequirementsMessage |
| `ResourceRequirementsMessage` | Requirements: [{GUID, Rqrmnt}] (only non-zero entries) | always | empty on join; full list at question start and after each "receive and distribute" step; per player in QuickDraw; resent every 30 s to unconfirmed phones |
| `ImageResourceContentTransferMessage` + JPEG | TransferID, ImageGUID, Acknowledge (always true) | TransferID >= 1, GUID non-empty | reply to RequestResourceMessage, or pushed by `PreemptiveResourceSender` |
| `PortraitThumbnailContentTransferMessage` + JPEG | TransferID, ClientUID | TransferID >= 1, ClientUID non-empty | lobby (and online) thumbnail pushes |
| `AnswerTextQuizStateMessage` | AnswerText, PlayerID (a label) | not sentinel, PlayerID >= 0 | Blank You, one per player after all texts arrive |
| `WouldYouRatherAnswerTextQuizMessage` | AnswerText, AnswerId (1 or 2) | not `---NOTSETTEXT---`, AnswerId >= 0 | Would You Rather, two per phone |

Meaning of `AnswerIndex` (game `HandleAnswerIndexResponseMessage`, by the game's current round type):

- Blank You: one of the PlayerID labels from `AnswerTextQuizStateMessage`.
- Top Drawer, Canvas Opinion, Mimic This: 1-based index into the last `ImageGUIDs` list.
- All others (That's You, That's You GC, Picture This, Would You Rather, QuickDraw): stored as is, read downstream as "slot/position v - 1". For Would You Rather send the AnswerId; for QuickDraw the list is in slot order.
- -1 means invalid, -2 no answer. Anything above -2 counts as answered.
- Drawing-vote rounds never get SendAnswer; the phone must send its pick as soon as the player chooses. Only consensus and text rounds send SendAnswer and wait.

### 4.6 What the game does with uploaded images

- ImgType 3 (DrawingCanvas) goes to the Top Drawer canvas (`ReceiveImage.ProcessData_DrawingCanvas`; it dereferences `client.receivedCanvas` without a null check, so it must only be sent in Top Drawer).
- ImgType 2 sets the portrait slot. ImgTypes 1, 4 and 5 all overwrite the player's single "drawing" slot and mark the Drawing item received.
- Nothing happens if the GUID does not parse or already exists. ImgType 0 is valid but ignored.
- The previous GUID of a slot is unloaded; if it was Required, a copy is re-registered under the old GUID.

## 5. Binary formats

- Images in both directions are plain JPEG files (no strokes, no PNG, no raw pixels).

| Image | Size | Notes |
|---|---|---|
| Photos (portrait, Top Drawer, Mimic, QuickDraw) | 480x640 | phone crops the camera to 3:4 and resamples to 480x640 with effects baked in |
| Drawings | 384x512 | centred 3:4 crop of the 512x512 canvas render texture, background composited, RGB24 |
| Thumbnails (game -> phone) | 128x128 | centre-square crop of the portrait, JPEG quality 50 (4:2:0) |
| Question images (game -> phone) | 480x640 (CanvasOpinion `_CO_`), 360x480 (MimicThis `_MT_`, PictureThis `_PT_`) | progressive JPEGs from `StreamingAssets/QuestionImages/TransferImages.bytes`, 23-51 KB |

- Phone JPEG encoding (`ImageEncodingUtil.EncodeTextureToJpeg`, RVA 0x5C6B1C): quality 75; if the result is 63489 bytes or more, re-encode at quality 20 without a second check.
- GUIDs for uploads: `Guid.NewGuid().ToString()` on the phone.
- Audio: `ClientAudioClipContentTransferMessage` + clip. Codec Opus (1) if supported, else Speex (0). The game's Opus decoder runs at 48000 Hz mono and reads a sequence of [uint32 big-endian length][uint32 big-endian, ignored][Opus packet]. Phone settings: Opus 48000 Hz, 1 channel, 16000 bit/s, frame 960, complexity 10; recorder 24000 Hz, maximum 5 s. Offline the game only caches and relays clips.
- Resource handshake: game sends `ResourceRequirementsMessage`; phone requests every Required GUID it lacks with `RequestResourceMessage` (or waits for a push), frees SafeToUnload ones, then echoes `AllResourcesReceivedMessage`. The game confirms only if every Required GUID is present with Rqrmnt 1; otherwise it resends. Flows block until every game player confirms or is missing.

## 6. Game flow per mode

Common question skeleton (`StartQuestionFlow` and friends):

1. G -> all: `ClientHoldingScreenCommandMessage` LOOK_AT_TV.
2. G -> all: `QuestionDataMessage`, `ResourceRequirementsMessage`.
3. P -> G: `RequestResourceMessage` for missing images, then `AllResourcesReceivedMessage`. Game waits (manifest resent every 30 s).
4. Mode-specific steps (below).
5. Scoring on the TV; `SyncJokersQuizStateMessage` / `ClientJokerCommandMessage` if jokers changed.
6. Continue: `ClientQuizCommandMessage` action 4 -> `ContinuePressedResponseMessage`. After a Continue press, disconnected players may be removed, followed by full PlayerLeft and PlayerJoined bursts.
7. Scenario changes send `ClientScenarioTransitionCommandMessage`.

Thumbnails are only sent in the lobby during local play; preemptive pushes of portraits and question images stop during waits.

### 6.1 Lobby and start

See 3.2. On Start: slots made contiguous (possible new AssignPlayerIDAndSlot), `PlayerJoinedMessage` + `PlayerNameQuizStateMessage` per other player, `ClientGameIDMessage` ("2..."), `SyncJokersQuizStateMessage` with starting jokers, `ClientSelectedUGCRequestMessage` to players with attached creations. The game waits with no timeout until every connected player with `ugcInfo.set` has sent its creation (`ClientQuestionUGCContentMessage` or `ClientImageUGCContentMessage`).

### 6.2 That's You / That's You GC / Picture This (consensus vote)

1. That's You: all portraits become Required; `ImageGUIDs` are the portraits. Picture This: the answer images are Required.
2. G -> all: `ClientAnswerCommandMessage{roundID, questionBarText, jokersEnabled, ImageGUIDs}`.
3. P -> G: `AnswerIndexResponseMessage` when the player picks (optional early), then on G -> P SendAnswer (16) the final pick; on SendJoker (17) `JokerConsumedResponseMessage`. Game waits up to 30 s each.

### 6.3 Would You Rather

1. G -> each phone: two `WouldYouRatherAnswerTextQuizMessage` (AnswerId 1 and 2) and `ClientAnswerCommandMessage`.
2. P -> G: `AnswerIndexResponseMessage{AnswerIndex: AnswerId}`, final on SendAnswer.

### 6.4 Blank You (text)

1. G -> all: `ClientTextEntryCommandMessage{TitleText}`.
2. G -> all: SendTextEntry (18) -> P -> G `TextAnswerReponseMessage{Text}` (60 s).
3. G -> all: one `AnswerTextQuizStateMessage` per player (shuffled labels), `ClientAnswerCommandMessage`.
4. P -> G: `AnswerIndexResponseMessage` with the chosen label; SendAnswer / SendJoker as in 6.2.

### 6.5 Top Drawer (photo, drawing, vote)

1. Game disables thumbnails and preemptive sends, waits for resource confirmations, prepares the target's canvas.
2. G -> target: `ClientTopDrawerTakePhotoCommandMessage{CommandText}`. G -> others: holding OTHER_PLAYER_TAKING_PHOTO with the target's name and ID.
3. Target at the shutter: `TopDrawerPhotoTakenResponseMessage` (TV flash).
4. Target on "use pic": upload ImgType 3 (480x640) and `UsePicPressedResponseMessage`. The FSM state `WaitForUsePicButton` advances only on UsePicPressed (or the target missing); the JPEG alone does not.
5. Game waits up to 360 s for the canvas (falls back to the portrait), marks it Required, sends the manifest.
6. G -> all: `ClientStartDrawingCommandMessage{CanvasGUID: photo}`.
7. G -> all: SendDrawing (19) -> uploads ImgType 1 (384x512). Game waits 60 s, copies the canvas for missing players, marks drawings Required, sends the manifest.
8. G -> all: `ClientAnswerCommandMessage` with drawings (random order). P -> G: AnswerIndex (1-based) immediately on pick. SendJoker if jokers enabled.

### 6.6 Canvas Opinion (draw on a picture, vote)

1. G -> all: `ClientStartDrawingCommandMessage{CanvasGUID: 480x640 question image}`.
2. SendDrawing -> uploads ImgType 1 (60 s). Missing players get the question image as their drawing.
3. Drawing vote as in 6.5 step 8.

### 6.7 Mimic This (photo, vote)

1. Question image (360x480) Required. G -> all: `ClientMimicThisCommandMessage{MimicPictureGUID}`.
2. SendMimicThisPhoto (24) -> uploads ImgType 4 (480x640). Game waits 30 s, falls back to portraits.
3. Vote on the photos (1-based index).

### 6.8 QuickDraw (selfie, chain drawing, vote)

1. G -> each player: `ClientQuickDrawTakePhotoCommandMessage{CommandText}` -> upload ImgType 5 selfie. The game loops with no timeout until every player's image has arrived and decoded, or the player is missing (missing players get their portrait).
2. Each iteration: per-player manifest (the previous player's drawing GUID Required, via the per-peer manifest), `ClientQuickDrawHoldingScreenCommandMessage{ImageGUID, Heading, Description, NumberOfDrawingsRemaining, PlayerName}` to the destination, QuickDrawDrawing (27) to start, SendQuickDrawDrawing (28) to submit (upload with ImgType 5; 30 s).
3. Final vote: `ClientAnswerCommandMessage` with roundID CanvasOpinion and drawings in slot order.

### 6.9 End of game

`WinSequenceFlow` / `QuitGame`: `ClientScenarioTransitionCommandMessage{Scenario:-1}`, `PlayerLeftMessage` per other player, `ClientGameIDMessage` ("1..."), EndOfGameMessage (15). On return to the front end, disconnected players are purged and `SendRestartProfileMessages` sends EditProfile to every phone (names cleared).

## 7. Timeouts and edge cases

| Item | Value | Where |
|---|---|---|
| Join retry | 1.5 s | phone |
| Peer death (both sides) | 17 s of silence | game scaled time, phone unscaled time |
| Game bad-connection threshold | 6.5 s left | game |
| Phone bad-connection threshold | about 11.3 s left | phone |
| Active send without new ack | 30 s -> disconnect | game |
| Unclaimed received data | 2 s | both |
| Content receive inactivity | 30 s (game), 10 s (app config) | |
| Content send inactivity | 3600 s (game content), 30 s (send queues) | |
| Answers, jokers | 30 s | game flows |
| Text entry | 60 s (300 s online) | game |
| Drawings | 60 s (300 s online) | game |
| Mimic photos | 30 s | game |
| Top Drawer canvas photo | 360 s | game |
| QuickDraw selfies | no timeout | game |
| UGC at game start | no timeout | game |
| Resource manifest resend | 30 s | game |
| Unclaimed game -> phone transfer | never completes without `OnSendComplete`; `WaitForAllNetworkTransfers` has no timeout | game |

Other edge cases worth preserving or fixing:

- Join response withheld while the same UID is still Connected (3.1).
- Repeated `AssignPlayerIDAndSlotMessage` with a new SlotID after renumbering.
- Stale command action during catch-up (4.4 caveat).
- `ClientQuickDrawHoldingScreenCommandMessage` carries the holding-text sentinel.
- `ClientTopDrawerTakePhotoCommandMessage.CommandText` may contain an unformatted `{0}`.
- `ShowPortraitPhotoControls` stays true on most holding messages after the lobby, because the game reuses one instance; only REJOIN sends false.
- `SessionID` can in theory be 0 (about 1 in 2^31), which phones reject.
- A drawing-vote list skips players without a drawing, so list positions may not line up with slots.

## 8. What the port must implement

The production transport is one reliable, ordered WebRTC data channel per phone (SCTP), opened with the QR code values. Text messages are JSON; binary messages are content chunks.

### 8.1 Becomes unnecessary

- Join/response/refuse magic datagrams and the nonce: replaced by a first `hello` message and a `joined` / `refused` reply.
- Data parts, acks, missing-part masks, retransmission, AIMD cooldowns, duplicate-ID memory and reassembly buffers: SCTP is reliable and ordered.
- Network-message header (`31 54`) and the batch envelope (`84 12 40 EE` ... `32 90`): one data channel message is one logical message. Batching was only a sender optimisation; delivering each segment as its own message with its own transfer ID is equivalent.
- Heartbeat acks as datagrams: still need liveness, but as a tiny ping message or any traffic.
- Port offsets, ephemeral ports, Wi-Fi Direct subnet check, discovery (DDP), console selection and manual IP.
- 1024-byte datagram limits and the 128-part receive limit (but see the size cap below).

### 8.2 Must be kept (seen by game logic)

1. A new comms provider in place of `UDPCommsProvider`/`UDPDataComms` that presents the same callbacks: `OnSocketAddressJoined`, received data claimable by (sender, transferID) through `TryGetData`, `OnSendComplete(peer, outerTransferID)`, receive activity, and `OnHeartStopped`.
2. A synthetic `SocketAddress` per WebRTC session: IPAddr non-zero, PortOffset >= 0 and, if `ResetPeer` or `m_udpReceives` indexing is reused, 0..5; the pair must be unique. Keep PortOffset 0 if possible so `AssignPlayerIDAndSlotMessage.UDPPortOffset` looks like the original. One session = one address; a reload gets a new address.
3. Join policy from `OnRawDataReceive`: refuse 1 (lobby closed, unknown UID), 2 (no free slot; 6 peers), 3 (online only). Raise `OnSocketAddressJoined` so InterfaceVersion and SessionState are sent; keep UID-keyed sends blocked until `AssignPlayerIDAndSlotMessage`.
4. Stale sessions: when a channel closes, or the same UID says hello from a new session, call `StopHeartbeat` on the old address at once. Otherwise every page reload stalls up to 17 s (and in the lobby the player is removed and comes back with a new PlayerID). If the old UID is still Connected and not yet stopped, either resolve it in the provider or reply `pending` so the page retries.
5. Route every message from a session under that session's SocketAddress, so `ClientServerJSONReceive` finds the client after a rejoin rebinds it.
6. Transfer IDs: JSON uses -7503; content data uses the header's `TransferID`. Deliver the header and its data within one processing window (2 s rule). Raise `OnSendComplete` with the outer transfer ID when a game -> phone transfer has been sent (or acknowledged by the page); otherwise content transfers never complete and preemptive sends to that peer stop.
7. Liveness: reset the peer's 17 s death timer on every data channel message; the page sends a ping well inside 17 s (for example every 1-5 s) and treats 17 s of silence as lost connection. Keep "bad connection" below 6.5 s on the game side. `GetActiveSendTimeSinceAck` must return 0 or match the original meaning, or `PlayerNetworkManager` disconnects clients after 30 s.
8. Size cap: keep JSON + content at or below 128246 bytes (or raise the game's receive limit deliberately); never exceed 131062 - 22 - header. Images should follow the original sizes (480x640 photos, 384x512 drawings, JPEG quality 0.75 with a re-encode near 0.2 at 63489 bytes or more), which keeps them well under the cap.
9. Page side of the handshake: send `hello{token, uid}`; wait for InterfaceVersion (compare with "2016-07-12-1645") and SessionState; compare SessionID as a string with localStorage; send `ClientRequestPlayerIDMessage{UID}`; accept repeated `AssignPlayerIDAndSlotMessage`; follow commands. On any `DisconnectCommandMessage` close the channel, return to the start screen and show the reason's popup; offer Reconnect only for ConnectionFailed (3). Add a timeout for the silent cases (closed lobby with a non-roster ID gets no reply).
10. Device UID: random, created once, stored in localStorage; it is the only identity used for rejoin.
11. PSNID: `PS4NpLibrary.GetInitialUserOnlineId` may return a placeholder; the phone never validates it.
12. Game-side robustness worth adding in the port: a timeout on the UGC wait at game start, a size check before `Player.SetImageData`, and ignoring ImgType 3 outside Top Drawer.

### 8.3 Suggested data channel envelope

- Text frame: `{"t":"hello","token":...,"uid":...}`, `{"t":"joined"}`, `{"t":"refused","reason":1|2|3}`, `{"t":"pending"}`, `{"t":"ping"}`, and `{"t":"msg","id":-7503,"json":<game JSON>}` (or the game JSON string as is, since `TypeString` already identifies it).
- Binary frame: a small header (transfer ID int32 LE, total length, chunk offset) followed by up to 16 KB of content, as in the spike. The receiver reassembles by transfer ID and hands the complete buffer to the provider under (session, transferID).

## 9. Open questions

- The exact phone reaction to `DisconnectCommandMessage{ConnectionRejected}` arriving after a successful join (Wi-Fi Direct subnet mismatch) was not traced. It is assumed to disconnect. Irrelevant on PC unless `PS4Wifi.Active` is ever true.
- Which `SocketException` code triggers the app worker's 5000 ms sleep is unresolved (irrelevant for the port).
- Whether the app's `SocketDataSendQueue.InitiateTransfer` reassigns a duplicate transfer ID like the game does was not checked.
- The app's game -> phone receive limits (131072 bytes, 128 parts) were inferred from the shared library, not disassembled.
- Two `DebugConsoleConnectionState` values that reset the phone's death timer were not mapped to names.
- Untested: a page that sends `ClientRequestPlayerIDMessage` before the join response while the old session of the same UID is still Connected. `_TryInitializeClient` should rebind the client; a runtime check is needed before relying on it instead of an explicit `StopHeartbeat`.
- Whether the phone's unscaled-time death timer expires on the first frame after a long suspension (inferred, not traced).
- When exactly `ClientOtherClientDetails.m_clientsHaveOtherClientDetails` is set and cleared (affects whether lobby phones ever get `PlayerJoinedMessage` for others).
- Which round prefabs use `PosePlayerDrawingConsensusQuestionFlow` versus `PoseConsensusQuestionFlow` was not checked in the PlayMaker wiring; it decides which rounds need an immediate AnswerIndex. The per-mode sequences in section 6 are assembled from the flow classes and should be confirmed in a smoke playthrough.
- The full catch-up burst order in `TargetClientState.SendCommandMessages` should be cross-checked during implementation.
- Whether the port should fix the shared `m_quizCommandMessage` instance (stale actions on catch-up) or the page should tolerate them.
- Whether Speex or Opus clips are worth supporting offline at all (the game only caches and relays them locally).
- The phone recorder runs at 24 kHz while Opus is configured for 48 kHz; whether audio is resampled is unverified.
