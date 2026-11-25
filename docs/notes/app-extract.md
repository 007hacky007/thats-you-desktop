# Phone app extraction (com.playstation.iwyk 1.6)

- Unity 2017.4.35f1, IL2CPP, metadata v24. AssetRipper 2.0.0 runs Cpp2IL itself; Il2CppDumper 6.7.46 (net7, run with `DOTNET_ROLL_FORWARD=Major`, `RequireAnyKey` forced off) writes `work/export/app-il2cpp/{dump.cs,script.json,il2cpp.h,stringliteral.json,DummyDll/}`.
- `dump.cs` has the full type/method/field names: `CameraEffectsPanelOverlayView`, `CameraEffectName_Twist`, and so on.
- AssetRipper project: `work/export/app/ExportedProject` (162 MB). `Assets/Resources/` holds animations, audio, bitmapfont, buttonprofiles, cards, introslides, loadingscreenimages, localisation, localisedlogoimages, scenarios, shaders.
- **Shaders: all 49 are placeholders** (compiled GLES programs, not decompiled to source by AssetRipper 2.0.0).
- **Camera effects are built from a few base shaders:**
  - `camfeed normal` (plain camera feed)
  - `camfx lumpymap 1015` with `smooth2` and `smooth4` variants: displacement-map distortion. Bulge, Twist, Warp and so on are most likely displacement textures plus parameters for this shader. Plan 5: find the textures and materials per effect.
  - `camfx lut32` and `lut32 vignette`: 32-step colour lookup table grading.
  - `camfx mirror` / `mirror smoothcentre`, `camfx v2mirror` / `v2mirror smoothcentre`.
- Plan 5 lead: GLES shader programs are stored as GLSL text inside the serialized Shader asset, so `ShaderExportMode=Yaml` may expose them as reference for how each effect looks. The spec still requires newly written shaders.
