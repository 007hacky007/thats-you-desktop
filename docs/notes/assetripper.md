# AssetRipper 2.0.0 - headless use

Start: `AssetRipper.GUI.Free --headless --port <n> --log-path <file>`. A local web server (Kestrel); `/openapi.json` lists the endpoints.

All calls are form-encoded POSTs that answer `302` and block until the work is done:

- `POST /Reset`
- `POST /Settings/Update`: only works before files are loaded. Checkboxes count as true when the key is present (any value) and false when absent. Select fields take the enum name.
- `POST /LoadFolder` with `Path=<folder>`
- `POST /Export/UnityProject` with `Path=<out dir>`; writes `ExportedProject/` and `AuxiliaryFiles/`.

Success can't be read from HTTP status. `pipeline/assetripper.py` checks the log for `Export : Finished post-export`.

Settings keys: IgnoreStreamingAssets, EnableStaticMeshSeparation, RemoveNullableAttributes, PublicizeAssemblies, DefaultVersion, BundledAssetsExportMode (GroupByAssetType|GroupByBundleName|DirectExport), ScriptContentLevel (Level0-3), EnablePrefabOutlining, EnableAssetDeduplication, TargetVersion, AudioExportFormat (Yaml|Native|Default|PreferWav), ImageExportFormat (Bmp|Exr|Hdr|Jpeg|Png|Tga), LightmapTextureExportFormat (Exr|Image|Yaml), SpriteExportMode (Yaml|Native|Texture2D), ShaderExportMode (Dummy|Yaml|Decompile), TextExportMode (Bytes|Txt|Parse), ScriptLanguageVersion (CSharp1..CSharp15_0|Latest|AutoExperimental|AutoSafe), ScriptExportMode (Decompiled|Hybrid|DllExportWithRenaming|DllExportWithoutRenaming), ScriptTypesFullyQualified, PreferOriginalTextureExtension, ExportUnreadableAssets, SaveSettingsToDisk.

## Game export result (2026-11-25, settings in pipeline/extract_game.py)

- Unity versions in files: 5.3.6p6 (plus 5.3.6p2), exported as 5.3.6p6. 4.7 GB without StreamingAssets.
- 2462 decompiled `.cs` files under `Assets/Scripts/` (Assembly-CSharp, Assembly-UnityScript, PlayMaker, AmplifyColor/Occlusion, SonyPS4MemorySave, ...).
- 15 scenes (LanguageSelect, Loading, Party-Master, Scenarios/...).
- StreamingAssets cloned from the merged dump (`IgnoreStreamingAssets` on, to save 4.4 GB of disk).
- **Shaders: all 108 are placeholders** (`//DummyShaderTextExporter`) even with `ShaderExportMode=Decompile`, because PS4 shader binaries can't be decompiled. By family: Hidden 45 (mostly Unity built-ins / image effects), Shader Forge 23, Wish 11, Unlit 5, Nature 5, Legacy Shaders 5, VacuumShaders 2, Sonic Ether 2, Skybox 2, Particles 2, Custom 2, AFS 2, Standard 2. Plan 2 must source or rewrite them.
