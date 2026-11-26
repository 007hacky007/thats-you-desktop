# Phone app: screens, camera, localisation

Reference for rebuilding the companion app ("It's Who You Know", `com.playstation.iwyk` 1.6, Unity 2017.4 IL2CPP) as a web page. Sources: `work/export/app-il2cpp/dump.cs` (names, enums, RVAs), `script.json`, `stringliteral.json`, the AssetRipper project `work/export/app/ExportedProject` (scene `Assets/_Scene/App-Master.unity`, materials, textures, localisation) and targeted ARM64 disassembly of `work/apk/lib/arm64-v8a/libil2cpp.so`. Behaviour is described in prose; no code is reproduced. The wire protocol is in `protocol.md`.

Asset paths below are relative to `work/export/app/ExportedProject/Assets/`. They are extracted from each person's own APK and are never committed.

## 1. App structure

- One scene, `_Scene/App-Master.unity`, holds every screen and overlay as a GameObject under `Screens` and `Overlays`. The object names carry the enum number, for example `33 LookAtTvHoldingScreen`.
- GUI is NGUI with three atlases: `Texture2D/GUISharedNewAtlas.png` (2048x2048, 97 sprites listed in `GameObject/GUISharedNewAtlas.prefab`: icons such as `icon_camera`, `icon_flip`, `icon_fx`, `icon_crop`, `icon_retake`, `icon_undo`, `icon_eraser`, `icon_pencil`, `icon_spray`, `icon_chat`, `icon_mic`, `icon_cog`, `icon_leave`, `icon_tv`, `icon_warning`, `polaroid_GUI`, `view_finder`, `popup`, `chat_bubble`, `circleBar_pressHold`, `breadCrumbElement`, `jokerShadow`), `Texture2D/GUISharedARGB.png` and `Texture2D/GUIAdditive.png`. `Resources/bitmapfont/IconFont.*` is an icon font.
- Fonts: `Font/FuturaLTPro-Heavy.otf`, `Font/FuturaPro-Demi.ttf`, `Font/Museo_Slab_700.ttf`, `Font/CaeciliaET.ttf`, `Font/AmericanTypeITCPro-Med.otf`, `Font/AchilleIICyrFY-Regular_L_CYR.ttf` (Cyrillic), `Font/PSS65.otf`.
- Each screen has a state class (`*ScreenState`), a view (`*ScreenView`, with tablet and mobile variants through `GuiViewTabletMobileReference`) and an input handler (`*ScreenInputHandler`). Screen changes go through `GuiChangeScreenRequest(eGuiScreenType)`; overlays through `GuiOpenOverlayRequest(eGuiOverlayType)`.
- Network commands arrive in `ClientQuizCommandSchedulerHandler`, which queues them and runs one `Execute*` per action. Each raises a `*DirectCommandEventArgs`, and `ConsoleConnectivityInputHandler.HandleEvent` turns that into a screen change. The mapping below was read from those handlers (screen value passed to `GuiChangeScreenRequest`).
- Background art follows the scenario: `Resources/scenarios/ScenarioData.json` maps scenario IDs to backgrounds (`Resources/scenarios/backgrounds/`, tiled, with `_half` variants) and scenario cards (`Resources/scenarios/cards/`). IDs: -1/0 home, 1 Into the Wild, 2 Back to School, 3 Travel, 4 Rock, 5 Fear, 6 Love, 7 Funfair, 8 Crime, 9 Home Life, 10 Creativity, 11 and 12 extra. `ClientScenarioTransitionCommandMessage` switches it.
- Sounds: `Resources/audio/SoundData.json` and `Resources/audio/sounds/` (Ogg; button, box appear/disappear, camera, start-game charge, joker, messaging, drawing).

## 2. Screen enums

`eGuiScreenType`: NONE 0, INITIAL 1, JOINED_GAME 3, NAME_ENTRY 4, TAKE_PHOTO 5, CHOOSE_PLAYER_ANSWER 6, DRAWING 7, TEXT_ENTRY 8, MIMIC_PHOTO 10, WAIT 11, QUICK_DRAW_PHOTO 12, DEBUG_SERVER_SETUP 13, CARD_ENTRY 14, CONFIRM_PROFILE 15, PARTY_UGC_PICKER 16, PROFILE_PORTRAIT_PHOTO 17, JOKER_EARNED 20, GENERAL_HOLDING 30, OTHER_PHOTO_HOLDING 31, OTHER_UGC_HOLDING 32, LOOK_AT_TV_HOLDING 33, QUICK_DRAW_HOLDING 34, PARTY_CONTINUE 40, CONNECTION_HELP 41, MANUAL_IP_ENTRY 45, CHOOSE_PS4 46, PASS_PLAY_PASS 50, PASS_PLAY_FINISHED_DRAWING 51, PASS_PLAY_TAKE_PHOTO 52, MARKETING 55, LEGAL_INFO 56, STARTUP 60, INITIAL_INTRO 61, WIFI_DIRECT 62, UGC_CHALLENGE 111, UGC_CREATE_HUB 115, CAMERA_FX_PROTOTYPE 201, FLICK_TEST 400, LOADING 9999.

`eGuiOverlayType`: NAVIGATION_BAR 2, CAMERA_EFFECTS_PANEL 9, SMALL_CAMERA 10, PROFILE_ENTRY_CRUMBS 20, CONNECTION_WARNING 25, UNARY_POPUP 29, BINARY_POPUP 30, BINARY_OBJECT_POPUP 31, SLIDE_TO_CONFIRM 33, CHAT_LOG_OVERLAY 34, PAUSE 35, OPTIONS 37, TUTORIAL 38, TIMER_DISPLAY 40, PARTY_GAME_CONNECTION_STATUS 50, FRAMERATE_DISPLAY 90, DEBUG_BLOCKER 91, BATTERY_DISPLAY 92, INTRO_SLIDES 95, DISCONNECTED_POPUP 96, WIFI_CONNECTIVITY 101, NETWORK_DIAGNOSTICS 200.

Binary popup intents (`eBinaryPopupIntent`): DISCONNECT 3 ("Are you sure you want to leave the game?"), JOIN_LEAVE_ONLINE_SERVER 5, MIC_PERMISSION 11, PHOTO_PERMISSION 12, QUIT_APP 13, plus UGC and Pass It On intents.

## 3. Screens of the party game

Legend: "Opened by" is the message (or local event) that shows the screen; "Leaves" is what replaces it. Texts are localisation keys (section 6) with the English text.

### 3.1 Start and connection (mostly not needed in the port)

| Screen | Scene object | Shows | Inputs | Opened by | Leaves |
|---|---|---|---|---|---|
| STARTUP 60 | `60 StartupScreen` | Sony presents splash (`Resources/loadingscreenimages/sonyPresents_<region>.png`) | none | app start | INITIAL |
| INITIAL_INTRO 61 / overlay INTRO_SLIDES 95 | `61 InitialIntroScreen`, `95 IntroSlidesOverlay` | 3 slides (`Intro_Slide1..3Title/Description`; art `Resources/introslides/` and `introslides_half/`: `ps4_bg`, `ps4_tv`, `ps4_hand_l1/r1`, `ugc_*`, `pnp_*`) | swipe, next | first run, Options "View intro" | INITIAL |
| INITIAL 1 | `01 InitialScreen` | main menu: logo (`Resources/localisedlogoimages/mainLogo_<lang>.png`), PLAY (`JoinPS4ButtonLabel`), write questions, make drawings, Pass It On tickets, cog | buttons | start, every disconnect | searching / CHOOSE_PS4 / UGC hub / Pass It On |
| CHOOSE_PS4 46 | `46 ChoosePS4Screen` | list of found consoles (`ChoosePS4GameInstruction`, `MultipleGamesFoundMessage`) | pick one | 2+ consoles found | join |
| CONNECTION_HELP 41 | `41 ConnectionHelpScreen` | help text (`ConnectionHelp_Heading`, `ConnectionHelp_Body_1..4`) | manual IP, Wi-Fi Direct | warning popups, Options | 45, 62 |
| MANUAL_IP_ENTRY 45 | `45 ManualIpEntryScreen` | IP field (`EnterIpInstruction`, `IpAddressFieldLabel`, `InvalidIpAddressMessage`) | keyboard | help | join |
| WIFI_DIRECT 62 | `62 WifiDirectScreen` | hotspot instructions (`WiFiDirect_*`) | none | help | join |
| JOINED_GAME 3 | `03 JoinedGameScreen` | "YOU'VE JOINED" (`JoinedGameMessage`), "Please wait" (`WaitForGameStartInstruction`) | leave (nav bar) | join accepted; EndOfGameMessage (15) | profile flow |

Overlays here: CONNECTION_WARNING 25 (`ConnectionWarning_NoWifiMessage` "No Wi-Fi detected", `ConnectionWarning_NoGameMessage` "No game found"), WIFI_CONNECTIVITY 101, UNARY_POPUP 29 for refusal texts, DISCONNECTED_POPUP 96 (`DisconnectedPopup_Heading`, `_Explanation`, buttons `_ConnectionHelp`, `_Reconnect`, `_Close`).

### 3.2 Profile flow

Started by `ClientQuizCommandMessage{action:31 EditProfile}`. The handler first shows GENERAL_HOLDING 30, sends `DeviceInfoMessage`, and starts the flow (`PartyGameProfileCreationStartRequest`, or `...EditRequest` when a saved profile exists; `PartyGameProfileSkipProfileRequest` is a debug shortcut). Flow states: START 1, SET_NAME 2, SET_CARD 3, SET_PHOTO 4, COMPLETE 5. The breadcrumb overlay PROFILE_ENTRY_CRUMBS 20 shows progress. The front camera is selected for the selfie.

| Screen | Scene object | Shows | Inputs | Next |
|---|---|---|---|---|
| NAME_ENTRY 4 | `04 NameEntryScreen` | "ENTER YOUR NAME" (`EnterNameTitle`), text field, guest toggle (`ProfileFlowScreenGuestToggledEventArgs` -> `isGuest`) | keyboard, confirm, disconnect | CARD_ENTRY |
| CARD_ENTRY 14 | `14 CardEntryScreen` | "PICK A CARD" (`SelectCardTitle`), 12 personality cards from `Resources/cards/CardData.txt` and `Resources/cards/textures/card_*.png` (CARD_1 artist, 2 caregiver, 3 everyman, 4 explorer, 5 hero, 6 innocent, 7 joker, 8 lover, 9 magician, 10 rebel, 11 ruler, 12 sage), flick animations `Resources/animations/CardFlick_*.anim` | tap a card, flick to confirm | PROFILE_PORTRAIT_PHOTO |
| PROFILE_PORTRAIT_PHOTO 17 | `17 ProfilePortraitPhotoScreen` | "TAKE A SELFIE" (`TakePhotoTitle`), live camera in a polaroid (`Texture2D/blankPolaroid.png`, `photo_frame_polaroid.png`), effects button, flip, camera roll | capture, effects panel, crop/retake (`CropPhotoButtonLabel`, `RetakePhotoButtonLabel`) | CONFIRM_PROFILE |
| CONFIRM_PROFILE 15 | `15 ConfirmProfileScreen` | "CONFIRM YOUR PROFILE" (`ConfirmProfileTitle`): name, card, selfie, optional attached creation ticket (`AttachCreationTicketTitle` "ADD A QUESTION OR DRAWING") | edit steps (back), attach creation, confirm | PARTY_UGC_PICKER or send profile |
| PARTY_UGC_PICKER 16 | `16 PartyUgcPickerScreen` | the player's saved creations (`CreationPickerTitle`, `NeverUsedCreationsHeader`, `UsedCreationsHeader`) | pick one | CONFIRM_PROFILE |

On confirm (`PartyGameProfileSendToServerRequest`): portrait upload (ImgType 2, 480x640 JPEG), `TrophyPhotoFXInfoMessage`, `ClientPlayerProfileMessage`. Then GENERAL_HOLDING until the game sends LOOK_AT_TV. UGC states disable the attach ticket: `UgcDisabledFirstRunMessage`, `UgcDisabledInstallingMessage`, `UgcDisabledOnlineMessage`.

### 3.3 Holding and lobby

| Screen | Scene object | Shows | Opened by |
|---|---|---|---|
| GENERAL_HOLDING 30 | `30 GeneralHoldingScreen` | generic wait, custom text; REJOIN text `WaitForNextQuestionInstruction` "You are connected, and will rejoin the game at the next question" | `ClientHoldingScreenCommandMessage` types CUSTOM_MESSAGE, REJOIN and others; after confirming an answer; after Top Drawer photo; after joker use |
| WAIT 11 | `11 WaitScreen` | blank wait | holding type BLANK (1) |
| OTHER_PHOTO_HOLDING 31 | `31 OtherPhotoHoldingScreen` | "{0} IS SNAPPING A PHOTO OF SOMEONE" (`OtherPersonTakingPhotoMessage`), "Get ready to pose! Match the topic shown on the TV" | holding type OTHER_PLAYER_TAKING_PHOTO (3), text = target name |
| OTHER_UGC_HOLDING 32 | `32 OtherUgcHoldingScreen` | another player choosing a creation | holding type OTHER_PLAYER_CHOOSING_UGC (2) |
| LOOK_AT_TV_HOLDING 33 | `33 LookAtTvHoldingScreen` | "LOOK AT THE TV" (`LookAtTvInstruction`), TV icon; with `ShowPortraitPhotoControls` the SMALL_CAMERA overlay (10) lets the player retake the selfie (capture, effects, open/close panel) | holding type LOOK_AT_TV (4); after drawing, text entry, mimic and QuickDraw photo |
| QUICK_DRAW_HOLDING 34 | `34 QuickDrawHoldingScreen` | next QuickDraw image, heading, description, owner name, drawings remaining (`DrawingsToGoMessage1..5`) | `ClientQuickDrawHoldingScreenCommandMessage` |
| PARTY_CONTINUE 40 | `40 PartyContinueScreen` | CONTINUE button (`PartyContinueButtonLabel`); in start mode "ARE ALL PLAYERS READY?" (`PartyReadyToStartMessage`) with a press-and-hold start button that charges a ring (`circleBar_pressHold`, sounds `IWYK_StartGame_Start/Charge/End`) | action 4 ContinueButton; action 14 StartGameButtonPrompt |
| JOKER_EARNED 20 | `20 JokerEarnedScreen` | "JOKER ADDED" / plural (`JokerAddedMessage`, `JokerAddedPluralMessage`), joker art (`Texture2D/Joker_01_Albedo.png`) | `ClientJokerCommandMessage` |

PARTY_CONTINUE replies: Continue -> `ContinuePressedResponseMessage`, then LOOK_AT_TV or GENERAL_HOLDING; full start charge -> `StartGameButtonPressedResponseMessage`.

### 3.4 Answering

| Screen | Scene object | Shows | Inputs | Opened by | Sends |
|---|---|---|---|---|---|
| CHOOSE_PLAYER_ANSWER 6 | `06 ChoosePlayerAnswerScreen` | question bar text; a grid of choices: player portraits (That's You), question/answer images (Picture This), player drawings or photos (Top Drawer, Canvas Opinion, Mimic This, QuickDraw), or text cards (Blank You via `AnswerTextQuizStateMessage`, Would You Rather via `WouldYouRatherAnswerTextQuizMessage`); joker switch (`JokerSwitchTitle`, `JokerSwitchInstruction`, `JokerCounterLabel` "JOKERS{n}REMAINING") when `jokersEnabled` | tap to select, flick to confirm (`GuiFlickConfirmChoosePlayerAnswerScreenView`), joker toggle | `ClientAnswerCommandMessage` | `AnswerIndexResponseMessage` on confirm (`GuiConfirmedUserInputEventArgs`); SendAnswer (16) sends the current selection; SendJoker (17) sends `JokerConsumedResponseMessage` |

After the confirm transition the phone goes to GENERAL_HOLDING. Images come from the resource handshake (`ImageResourceContentTransferMessage`), so the grid fills as JPEGs arrive.

### 3.5 Creating

| Screen | Scene object | Shows | Inputs | Opened by | Sends |
|---|---|---|---|---|---|
| DRAWING 7 | `07 DrawingScreen` | canvas over a background (Top Drawer photo, Canvas Opinion image, QuickDraw image); "GET READY TO DRAW" (`GetReadyToDrawInstruction`); palette, tools, sizes, undo, zoom/pan | draw, colour, tool, size, undo, pinch zoom, flick to confirm | `ClientStartDrawingCommandMessage` (7); QuickDrawDrawing (27) | flick confirm: drawing upload ImgType 1 (384x512) + `TrophyDrawingColouringInfoMessage`, then LOOK_AT_TV; SendDrawing (19) / SendQuickDrawDrawing (28) force the send |
| TEXT_ENTRY 8 | `08 TextEntryScreen` | title (question), text card (`Texture2D/TextEntryCard.png`) | keyboard, confirm (flick, `GuiFlickTextEntryScreenView`) | `ClientTextEntryCommandMessage` | text on SendTextEntry (18) as `TextAnswerReponseMessage`; confirm moves to LOOK_AT_TV |
| QUICK_DRAW_PHOTO 12 | `12 QuickDrawPhotoScreen` | camera in a polaroid with the command text; used for both Top Drawer and QuickDraw photos (`GuiFlickQuickDrawTakePhotoPolaroidScreenView`) | capture, effects panel, retake, flick to use | `ClientTopDrawerTakePhotoCommandMessage` (12); `ClientQuickDrawTakePhotoCommandMessage` (25) | Top Drawer: `TopDrawerPhotoTakenResponseMessage` at capture, upload ImgType 3 + `UsePicPressedResponseMessage` on use, then GENERAL_HOLDING. QuickDraw: upload ImgType 5, then LOOK_AT_TV |
| MIMIC_PHOTO 10 | `10 MimicPhotoScreen` | the picture to imitate (`MimicPictureGUID`) plus the live front camera; effect reset to none | capture, effects panel, confirm | `ClientMimicThisCommandMessage` (23) | on confirm or SendMimicThisPhoto (24): upload ImgType 4, then LOOK_AT_TV |

Drawing details (from `App-Master.unity` and `ColourUtil.GetColour`, RVA 0x4CDEEC):

- Tools (`eDrawingToolType`): BRUSH 1, ERASER 2, SPRAY 3, PAN 4. Palettes: PENCIL 1, BRUSH 2.
- Sizes (`BrushSizeUtil`, relative units): brush and eraser SMALL 0.08, MEDIUM 0.25, LARGE 0.55; spray 0.21, 0.35, 0.8.
- Brush shapes (`BrushSettingUtil`): brush and eraser inner radius 0.85, outer 0.9, falloff type 1, alpha 1; spray inner 0, outer 1, falloff type 6, alpha 0.25.
- Colours (`eDrawingColour`, RGB 0-255): BLACK 1 (0,0,0), RED 2 (243,60,43), YELLOW 3 (255,235,4; Unity `Color.yellow`), BLUE 4 (32,63,230), PINK 5 (220,164,142), BROWN 6 (114,75,43), GREY 7 (143,144,146), DARK_PINK 8 (183,100,167), GREEN 9 (0,164,81), PURPLE 10 (103,45,135), ORANGE 11 (245,146,27), WHITE 12 (255,255,255), KHAKI 13 (162,119,75), TURQUOISE 14 (20,218,255).
- Canvas: `RenderTexture/CanvasLayerRTT.renderTexture` and `PaintLayerRTTbig.renderTexture` are 512x512; the uploaded drawing is the centred 3:4 crop (384x512) with the background composited. Undo uses the `Stage2RT_undotex*` render textures. Brush prefab `GameObject/Brush64.prefab`.
- Drawing intents (`eDrawingScreenIntent`): PARTY_GAME_TOP_DRAWER_DRAWING 1, CHALLENGE_DRAWING 2, PARTY_GAME_CANVAS_DRAWING 3, PARTY_GAME_QUICK_DRAW_DRAWING 4, PASS_PLAY_DRAWING 5.
- Background art used by drawing and answers: `Texture2D/NewsPaperClipping_01..06_MAT_Albedo.png`, `NewsPaperClipping_CanvasOpinion_MAT_Albedo.png`, `PolaroidMini_Albedo.png`, `TableTop2_Albedo.png`.

### 3.6 Overlays during play

| Overlay | Shows | Opened by |
|---|---|---|
| NAVIGATION_BAR 2 | back, leave (`NavigationBarOverlayDisconnectButtonView`), confirm buttons | most screens |
| PAUSE 35 | "PAUSED" / "The game has been paused" (`PausedTitle`, `PausedMessage`) | action 29 Pause; closed by 30 Unpause |
| CHAT_LOG_OVERLAY 34 | chat bubbles, text and voice messages (`ChatMessageFieldDefaultValue`, `AudioSentMessage`, `AudioCancelledMessage`, `ChatReturnToGameButtonLabel`) | chat button; `ClientReceiveChatLogMessage` |
| TIMER_DISPLAY 40 | countdown | timed activities |
| PARTY_GAME_CONNECTION_STATUS 50 | bad-connection indicator (probably the `icon_connection1/2` sprites) | death timer below about 11.3 s |
| OPTIONS 37 | cog menu (`CogMenuTitle`, `CogMenuLeaveGameButtonLabel`, `ViewIntroButtonLabel`, `ConnectionHelpButtonLabel`, `EnterIpButtonLabel`, `SoundOptionButtonLabel`, privacy and legal) | cog |
| BINARY_POPUP 30 | "Are you sure you want to leave the game?" (`LeavePartyGamePopupMessage`) with Leave/Cancel | leave buttons; confirm sends `ClientDisconnectMessage` |
| TUTORIAL 38 | contextual hints (`Tutorial_*_Title/Instructions`, for answers, drawing, profile, photo, selfie, text round, party chat) | first use of each screen |
| CAMERA_EFFECTS_PANEL 9 | scrolling list of effect buttons with names | effects button on photo screens |

Disconnect popups (UNARY_POPUP 29 unless noted), by `eDisconnectDisplayMessage`: GameFull 2 `GameFullPopupMessage` "Could not join because the game is full"; VersionDisparity 5 `VersionDisparityPopupMessage`; OnlyOnePlayer 6 `OnlyOnePlayerOnlineMessage`; ConnectionNotAllowed 7 `GameStartedPopupMessage` "Could not join because the game has already started"; 0, 1, 3, 4 `ConnectionFailedPopupMessage`; only 3 opens DISCONNECTED_POPUP 96 with Reconnect.

### 3.7 Not part of the party game

UGC creation (UGC_CREATE_HUB 115, UGC_CHALLENGE 111, TAKE_PHOTO 5 for drawing challenges, challenge texts `TU0001..`, `TU8000..`), Pass It On (50, 51, 52), MARKETING 55, LEGAL_INFO 56, and debug screens (13, 201, 400). Only the creations matter to the game, through `ClientSelectedUGCRequestMessage` at game start.

## 4. Camera

### 4.1 Capture pipeline

- `UnityWebCamManager`: requests 640x480 from both cameras (`frontWebCamConfig`, `rearWebCamConfig`), runs the front camera at 15 fps and the rear at 30 fps (constants), waits 0.5 s after a camera change and 0.5 s before taking a photo, and shuts the camera down after 10 s idle where allowed (`cameraShutdownTimeLimit`).
- Facing: REAR 0, FRONT 1. Profile selfie, Mimic This and QuickDraw select the front camera. The flip button (`icon_flip`) toggles it on photo screens. Top Drawer keeps the current camera (the player is expected to flip to photograph someone; not verified).
- The feed is rendered through the effect material into `RenderTexture/MainCameraFeed.renderTexture` (480x640). `CameraFeedMaterialManager` applies the camera texture to all effect materials and corrects rotation and mirroring per device by rewriting `_FeedUVTop` / `_FeedUVBottom` (the four corner UVs of the feed quad).
- Default feed UVs (`eCameraFXFeedUsage` FOUR_THREE_ASPECT 0): top-left (0.78125, 1), top-right (0.21875, 1), bottom-left (0.78125, 0), bottom-right (0.21875, 0). That is the central 3:4 portrait slice of a 4:3 landscape feed (u from 0.21875 to 0.78125 = 0.5625 of the width), mirrored horizontally. Kaleidoscope uses SQUARE_IN_CENTER (1): u from 0.125 to 0.875, a centred square. FULL_FEED 2 is unused by the shipped materials.
- Capture: `CameraTakePictureRequest` grabs the rendered (effect-applied) frame, the photo screens show it in the polaroid, `CameraCropImageRequest` resamples the chosen crop at 480x640 (`CreateCroppedDuplicate`), and the texture is stored as JPEG (quality 75, re-encoded at 20 if 63489 bytes or more).
- Flash: `PhotoFlashEffect`, `Texture2D/fullScreenFlash.png`, `FlashOverlaySprite`.
- Camera intents: PARTY_HOLDING 1 (small camera on holding screens), PARTY_GAME_ACTIVITY 2, UGC 6, PASS_PLAY 7. Placeholder when no camera: `Texture2D/BlankCameraFeed.png`.

### 4.2 Effects

`eCameraEffect` and the material mapping (`CameraFeedMaterialManager.materialReferences` in `App-Master.unity`). Names come from `CameraEffectName_<Name>`.

| # | Effect | Material | Shader | Map texture | Smoothing | Feed usage |
|---|---|---|---|---|---|---|
| 0 | None | `Material/CameraFeedNormal.mat` | `camfeed normal` | - | - | 4:3 |
| 1 | Mirror | `Material/CameraFeedMirror.mat` | `camfx mirror smoothcentre` | - | `_CentralSmoothing` 0.015 | 4:3 |
| 2 | Warp | `Material/CameraFeedWarp.mat` | `camfx lumpymap 1015 smooth2` | `Texture2D/Warp.png` 480x640 | 0.1 | 4:3 |
| 3 | Shrink | `Material/CameraFeedShrink.mat` | `camfx lumpymap 1015 smooth4` | `Texture2D/Shrink.png` 384x512 | 0.15 | 4:3 |
| 4 | Stretch | `Material/CameraFeedStretch.mat` | `... smooth4` | `Texture2D/Stretch.png` 384x512 | 0.15 | 4:3 |
| 5 | Kaleidoscope | `Material/CameraFeedKaleido.mat` | `... smooth4` | `Texture2D/Kaleidoscope.png` 192x256 | 0.2 | square in centre |
| 6 | Bulge | `Material/CameraFeedBulge.mat` | `... smooth2` | `Texture2D/Bulge.png` 480x640 | 0.1 | 4:3 |
| 7 | Twist | `Material/CameraFeedTwist.mat` | `... smooth4` | `Texture2D/Twist.png` 384x512 | 0.15 | 4:3 |
| 8 | V2 Mirror | `Material/CameraFeedV2Mirror.mat` | `camfx v2mirror` | - | `_MirrorA` 0.75, `_MirrorB` 0.25 | 4:3 |
| 9 | Square | `Material/CameraFeedSquare.mat` | `... smooth2` | `Texture2D/Square.png` 480x640 | 0.1 | 4:3 |
| 10 | Grid | `Material/CameraFeedGrid.mat` | `... smooth4` | `Texture2D/Grid.png` 384x512 | 0.15 | 4:3 |
| 11 | Hourglass | `Material/CameraFeedHourglass.mat` | `... smooth4` | `Texture2D/Hourglass.png` 384x512 | 0.15 | 4:3 |

Shaders live in `Resources/shaders/camerafeed/` and `Resources/shaders/camerafx/`. AssetRipper exported them as placeholders: the property blocks are real, the bodies are dummies. Unused shader variants: `camfx lumpymap 1015` (no smoothing), `camfx mirror`, `camfx v2mirror smoothcentre`, `camfx lut32`, `camfx lut32 vignette` (LUT grading with `_LutTex`, `_Vignette`, `_VignetteMultiplier`; `Texture2D/VignetteMask.png` 128x128). No shipped material uses the LUT shaders.

### 4.3 Reproducing the effects in WebGL

Property blocks (from the exported `.shader` files):

- lumpymap: `_MainTex` (camera), `_MapTex` labelled "X (GR) Y (AB)", `_FeedUVTop`, `_FeedUVBottom`, `_Smoothing` (0..1).
- mirror smoothcentre: `_MainTex`, feed UVs, `_CentralSmoothing` (0..0.1).
- v2mirror: `_MainTex`, feed UVs, `_MirrorA`, `_MirrorB` (0..1).

What the map textures contain (measured from the PNGs):

- Each map is an absolute lookup table, not an offset field: the pixel at output position (x, y) holds the source position to sample. At the corners the value equals the position (identity), so the image edges stay put.
- X comes from the R and G channels, Y from the B and A channels. The two channels of a pair are within about 1-3 levels of each other (for example R 132, G 129 at the centre of Warp), and Y increases upwards (top row Y near 1, bottom row near 0, matching UV space). The exact combining formula (average of the two 8-bit values, or high byte plus fine byte) is not recoverable from the placeholder shader; averaging the pair (X = (R + G) / 510, Y = (B + A) / 510) matches the corners and centre to within 1/255. Read the PNG as linear data (no sRGB conversion, no premultiplied alpha), because the alpha channel carries Y.
- The result is a source UV in feed-quad space (0..1 across the cropped portrait). Map it through the four corner UVs (bilinear interpolation of `_FeedUVTop` / `_FeedUVBottom`) to get the camera texture coordinate. This is also how the selfie mirroring and the 3:4 crop happen.
- Texture settings (`Texture2D/Warp.png.meta` and siblings): bilinear filtering (filterMode 1), clamp in both axes (wrap 1), sRGB flag on in the import settings (ignore it for maps in WebGL).
- `_Smoothing`: "smooth2" and "smooth4" in the shader names suggest 2 or 4 extra map samples blended by `_Smoothing` to hide 8-bit stepping in the map. A practical reproduction: sample the map at the pixel and at offsets of about half a map texel in 2 or 4 directions, then mix the average into the centre sample by `_Smoothing`. This is a design guess; compare against screenshots of the original.
- Mirror: reflect the image about the vertical centre line (one half copied onto the other), with a soft blend band of width `_CentralSmoothing` (0.015 in UV) at the seam. Which half is kept is unverified.
- V2 Mirror: two mirror lines at u = `_MirrorB` (0.25) and u = `_MirrorA` (0.75); the centre band is kept and reflected outwards (interpretation of the property names; unverified).
- Kaleidoscope uses the square centre crop of the feed and a 192x256 map; the map holds the folded coordinates, so no extra code is needed beyond the generic lookup.
- Output: draw into a 480x640 canvas (the size of `MainCameraFeed`) and capture that canvas for the photo, so the effect is baked in as in the original. `TrophyPhotoFXInfoMessage.usesFX` is true when the effect is not None.

To make the effects exact, the compiled GLES programs can still be read from the original Shader assets in the APK (`assets/bin/Data`): Unity 2017 stores them LZ4-compressed inside the serialized Shader object, so a plain string search finds nothing. A small extractor (for example UnityPy) would expose the GLSL text as reference.

## 5. Phone-side behaviour worth copying

- Version check: compare `InterfaceVersionMessage.InterfaceVersion` with "2016-07-12-1645"; mismatch shows `VersionDisparityPopupMessage`.
- Saved profile (PlayerPrefs, in the port localStorage): `SESSION_ID`, `OLD_PLAYER_ID`, `OLD_PORTRAIT_ID`, `OLD_CARD`, `OLD_NAME`; reused when SessionID matches.
- Every disconnect returns to INITIAL and shows the reason popup.
- The bad-connection overlay shows below about 11.3 s left of the 17 s timer.
- Answers are confirmed by a flick gesture; drawings, text and photos also confirm by a flick of the card or polaroid (`GuiFlick*ScreenView`, `icon_flick2`, sound `IWYK_ConfirmSwipe.ogg`).
- Debug portraits `Texture2D/Monkey1..6.png` (128x128) are only used by the debug skip-profile path.

## 6. Localisation

- Files: `Resources/localisation/appText_<code>.loc.loc`. Format: UTF-8 text, CRLF line ends, tab-separated, header line `String Id` / `Localised Text`, then one `key<TAB>text` per line. Every file has 574 lines (573 strings).
- Codes present: `en`, `en-GB`, `GBR`, `fr`, `fr-CA`, `de`, `it`, `es`, `es-MX`, `pt`, `pt-BR`, `pl`, `ru`, `nl`, `da`, `fi`, `no`, `sv`, `tr`, `cs`, `hu`, `el`, `ar`, plus debug files `DBG_ABC`, `DBG_FRA`, `DBG_KEYS`, `DBG_LONG_SINGLE`, `DBG_LONG_WORDS`, `DBG_RUS`.
- `LocaleCode` enum: USA 1, GBR 2, FRANCE 3, ITALY 4, GERMANY 5, SPAIN 6, POLAND 7, PORTUGAL 8, RUSSIA 9, MEXICO 10, BRAZIL 11, DENMARK 12, FINLAND 13, NORWAY 14, SWEDEN 15, NETHERLANDS 16, TURKEY 17, FRENCH_CANADA 18, CZECH_REPUBLIC 19, HUNGARY 20, GREECE 21, and the debug codes 71, 72, 91-94. Arabic has a file but no locale code.
- Selection: `LocalisationManager.DetectAndSetLanguage` detects the device language and region (auto-detect is configurable) and loads the matching file; `CapitaliseString` uppercases with the current culture (used for `uppercasePlayerName`), and `deviceCultureName` in the profile is that culture name.
- Placeholders: `{n}` is a line break (for example `JokerCounterLabel` "JOKERS{n}REMAINING"), `{0}` is a substituted value (only in `OtherPersonTakingPhotoMessage`, the player name, and `WifiConnectivityPopup_Connected`).
- Localised images: `Resources/localisedlogoimages/mainLogo_<EN|ES|FR|IT|PL|PT|RU>.png`, `passPlayBookLogo_*`, `stickerLogo_*`; atlas sprites `upsellButtonLogo_*`; splash `Resources/loadingscreenimages/sonyPresents_<region>.png` (AR, BRAPOR, CZE, DA, DEU, EL, ESP, FI, FRA, GBR, HUN, ITA, LATESP, NL, NO, POL, PRT, RUS, SV, TR).
- Texts the game itself sends (question text, holding text, player names, `DisplayName`) come already localised from the game; the phone only localises its own UI.

## 7. Suggested screen list for the web app

Party game only; UGC creation, Pass It On, marketing, discovery and debug screens are left out.

1. **Connect** (replaces STARTUP, INITIAL, discovery, CHOOSE_PS4, manual IP, Wi-Fi Direct): logo, "PLAY"/connecting state, camera permission prompt; reads the QR fragment and sends `hello`. Shows "No game found" after 17 s without a reply.
2. **Joined / wait** (JOINED_GAME 3): "YOU'VE JOINED", "Please wait".
3. **Name** (NAME_ENTRY 4) with guest toggle.
4. **Card** (CARD_ENTRY 14): 12 cards.
5. **Selfie** (PROFILE_PORTRAIT_PHOTO 17): camera with effects, flip, crop/retake.
6. **Confirm profile** (CONFIRM_PROFILE 15). Creation attach can stay disabled unless UGC is ported.
7. **Holding** (one component for GENERAL_HOLDING 30, WAIT 11, OTHER_PHOTO_HOLDING 31, OTHER_UGC_HOLDING 32, LOOK_AT_TV_HOLDING 33 with optional small selfie camera, REJOIN text).
8. **QuickDraw holding** (QUICK_DRAW_HOLDING 34).
9. **Continue / Start** (PARTY_CONTINUE 40): tap Continue; press-and-hold Start.
10. **Joker earned** (JOKER_EARNED 20).
11. **Answer** (CHOOSE_PLAYER_ANSWER 6): portrait, image, drawing and text variants, joker switch, flick or tap to confirm.
12. **Draw** (DRAWING 7): palette of 14 colours, brush/eraser/spray, 3 sizes, undo, zoom, background image.
13. **Text entry** (TEXT_ENTRY 8).
14. **Photo** (QUICK_DRAW_PHOTO 12): Top Drawer and QuickDraw variants, with effects.
15. **Mimic photo** (MIMIC_PHOTO 10): reference picture plus front camera with effects.
16. **Overlays**: effects panel, pause, leave confirm, disconnect/refusal popup (Reconnect only for ConnectionFailed), bad-connection indicator, optional chat log (text only is enough offline), optional tutorials.

## 8. Open questions

- The exact shader math (map channel combination, smoothing kernel, mirror halves) needs the compiled GLES text from the APK's Shader assets or side-by-side screenshots of the original app.
- Whether Top Drawer forces the rear camera was not traced (no facing request in its handler).
- How `CameraRollWorker` gallery picks are cropped (they may bypass the 480x640 crop) was not verified.
- The units of the brush sizes (fraction of canvas or of the screen) were not traced.
- How the start button's hold duration is configured (`StartGameButtonUpdateProportionRequest`) was not read.
- Whether the original sends the text answer on its own confirm or only on SendTextEntry was not traced; the game only requires it after SendTextEntry.
- Which voice chat features are worth porting offline; the game only caches and relays clips.
