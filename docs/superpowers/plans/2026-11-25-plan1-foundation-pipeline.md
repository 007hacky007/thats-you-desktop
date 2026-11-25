# Plan 1 - Foundation: repo, extraction pipeline, first compile report

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One command takes the owner's base dump, patch dump and APK and produces an extracted Unity project of the game (opened in the pinned Unity, with a categorized compile-error report), an extracted phone-app asset tree with Il2Cpp metadata, and a repo that cannot accidentally publish decompiled code.

**Architecture:** Python 3 stdlib-only pipeline scripts in `pipeline/`, one per step, each idempotent and driven by a git-ignored local config. Pinned third-party tools (AssetRipper, Il2CppDumper) are downloaded with checksum verification into a git-ignored work directory. Decompiled-code changes live in `local-patches/`, guarded by a pre-push hook.

**Tech Stack:** Python 3.11+ (stdlib only, tests with `unittest`), AssetRipper (pinned release, headless web API), Il2CppDumper (pinned release, .NET), Unity LTS (version decided in Task 2), git.

**Spec:** `docs/superpowers/specs/2026-11-25-thats-you-port-design.md`

## Roadmap (later plans, written when their inputs exist)

1. **This plan** - foundation and extraction.
2. Plan 2 - make the game compile and reach the title screen on macOS arm64 (PS4 plugin stubs, NGUI/PlayMaker, shaders, UnityScript to C#, entitlement stubs). Input: compile report from Task 8.
3. Plan 3 - audio: identify PS4 Wwise encodings, convert, replacement `AkSoundEngine` layer.
4. Plan 4 - networking: message catalog from the game DLL and APK metadata, production WebRTC Direct server as native plugin, new comms provider, join QR screen, config.
5. Plan 5 - phone web app: screens and flows, 11 effect shaders, content transfer, service worker, deploy script.
6. Plan 6 - Windows x64 build, full playtests with real phones on both platforms.
7. Plan 7 (before step 2, mandatory before any publication) - convert `local-patches/` to build-time compiled-assembly edits (Cecil/Harmony).

## Global Constraints

- Inputs are never modified. Base dump `/Users/honza/Desktop/CUSA08389`, patch dump `/Users/honza/Desktop/CUSA08389-patch`, APK `/Users/honza/Downloads/com.playstation.iwyk_1.6-1623_minAPI22(arm64-v8a,armeabi-v7a)(nodpi)_apkmirror.com.apk` are read-only inputs given via config, never hard-coded in committed files.
- Nothing extracted, decompiled or copied from the game or APK is committed outside `local-patches/`. Extracted output goes to the work directory (default `work/`, git-ignored).
- Nothing is pushed to any public remote until Plan 7 is done. The pre-push hook (Task 7) enforces this.
- The owner's deployed phone page address never appears in committed files. `config/local.json` is git-ignored.
- Scripts run on macOS and Windows: Python stdlib only, `pathlib` paths, no shell-specific syntax.
- Git: default branch `master`. Repo-local identity for a future public GitHub repo: `git config user.name "007hacky007"`, `git config user.email "007hacky007@users.noreply.github.com"`, `git config commit.gpgsign false`. Commit messages: one short imperative line, no body, no trailers, no AI attribution.
- Generated documents use only ASCII hyphen-minus, never Unicode dashes.

## Review Focus

- Input paths with spaces and parentheses (the APK filename has both): every script must pass paths as argv lists, never through a shell. Test in Task 3.
- A re-run after a partial failure (interrupted copy, killed AssetRipper): steps must detect incomplete output and redo it instead of trusting a half-written directory. Test in Task 3 (merge) and Task 5 (export marker written last).
- Disk usage: the merged dump is about 10 GB. Merge must use copy-on-write clones or hard links where the filesystem supports them, not full copies. Test in Task 3.
- A missing or wrong input (patch path pointing at the base, APK path wrong): fail fast with a message naming the config key, before any long step starts. Test in Task 1.
- Tool download tampering or a changed release asset: checksum mismatch must abort and delete the file. Test in Task 4.

---

### Task 0: Android check of the spike (manual, owner)

Spec open risk: Android Chrome may block or prompt. This gates the networking design.

- [ ] **Step 1:** Restart `spike/webrtc-direct/spike -page <owner page url>` and have the owner scan the QR code with an Android phone in Chrome, run Start, try effects, Snap + send.
- [ ] **Step 2:** Record the result in `docs/notes/android-spike.md` (device, Chrome version, any permission prompt, photo received yes/no). If Chrome blocks outright: stop and bring options to the owner (spec).

### Task 1: Repo skeleton and config

**Files:**
- Create: `.gitignore`, `README.md`, `config/local.example.json`, `pipeline/__init__.py`, `pipeline/config.py`
- Test: `tests/test_config.py`

**Interfaces:**
- Produces: `pipeline.config.load(path: Path | None = None) -> Config`; `Config` is a frozen dataclass with `base_dump: Path`, `patch_dump: Path`, `apk: Path`, `work_dir: Path`, `unity_editor: Path | None`, `phone_page_url: str | None`. Default path `config/local.json`. Raises `ConfigError(key: str, message: str)`.

- [ ] **Step 1:** `git init` (branch `master`), set the repo-local identity from Global Constraints. `.gitignore` covers `work/`, `config/local.json`, `*.apk`, `spike/webrtc-direct/photos/`, `spike/webrtc-direct/spike`, `__pycache__/`. README: one paragraph on what the project is, the "Before publishing" warning (Plan 7 required, never publish your page address), and a placeholder setup section.
- [ ] **Step 2: Write failing tests** in `tests/test_config.py`: `test_loads_valid_config` (tmp dirs exist, `eboot.bin` in base, `sce_sys/param.sfo` in both dumps, apk file exists, returns paths); `test_missing_key_names_key` (no `apk` key -> `ConfigError.key == "apk"`); `test_patch_equal_to_base_rejected` (same path -> `ConfigError.key == "patch_dump"`); `test_base_without_eboot_rejected` (`key == "base_dump"`); `test_work_dir_defaults_to_repo_work` (`work_dir` omitted -> `<repo>/work`); `test_paths_with_spaces_and_parens` (apk named `a (b) c.apk` loads).
- [ ] **Step 3:** Run `python3 -m unittest tests.test_config -v`. Expected: FAIL (module missing).
- [ ] **Step 4:** Implement `pipeline/config.py`. Patch dump is recognized by `sce_sys/changeinfo/` existing; base by `eboot.bin`.
- [ ] **Step 5:** Run the tests. Expected: PASS. Create `config/local.json` locally from the example with the owner's real paths (not committed).
- [ ] **Step 6:** Commit: `git add -A && git commit -m "Add repo skeleton and pipeline config"`. Also commit `spike/` and `docs/` (photos and binary excluded by .gitignore).

### Task 2: Decide and pin the Unity version

**Files:**
- Create: `pipeline/versions.json`, `docs/notes/unity-version.md`

**Interfaces:**
- Produces: `pipeline/versions.json` key `"unity": {"version": "<exact e.g. 6000.3.xf1 or 2022.3.62f1>", "changeset": "<hash>"}` used by Task 8 and later plans.

- [ ] **Step 1:** Research the current Unity 6 LTS: whether the built-in render pipeline is still supported (not removed, not just deprecated-with-warnings), and native macOS arm64 and Windows x64 standalone players. Sources: Unity release notes and the Unity 6 LTS upgrade guide. Rule: pick the newest LTS where the built-in render pipeline is still fully supported; otherwise pin the last 2022.3 LTS patch.
- [ ] **Step 2:** Write the decision and sources into `docs/notes/unity-version.md`, the version and changeset into `pipeline/versions.json`.
- [ ] **Step 3 (owner):** Install Unity Hub, sign in with a Unity account (Personal license), install the pinned editor with Windows Build Support (Mono) and Mac Build Support modules. Set `unity_editor` in `config/local.json`.
- [ ] **Step 4:** Verify: `<unity_editor> -batchmode -nographics -quit -logFile -` exits 0 and the log shows the licence activated.
- [ ] **Step 5:** Commit: `git commit -m "Pin Unity version"`.

### Task 3: Merge base and patch dumps

**Files:**
- Create: `pipeline/merge.py`
- Test: `tests/test_merge.py`

**Interfaces:**
- Consumes: `Config` (Task 1).
- Produces: `pipeline.merge.merge(base: Path, patch: Path, out: Path) -> MergeResult` (`files: int`, `from_patch: int`, `cloned: bool`). Output layout mirrors the dumps. Writes `out/.merge-manifest.json` last (sha256 of the sorted list of `(relpath, size, mtime_ns, source)`). CLI: `python3 -m pipeline.merge` uses config, output `work/merged`.

- [ ] **Step 1: Write failing tests:** `test_patch_file_wins` (same relpath in both -> patch content); `test_base_only_file_kept`; `test_patch_only_file_added`; `test_rerun_is_noop` (second run returns without copying: mock the copy function, assert not called); `test_incomplete_output_redone` (delete the manifest after a run -> next run redoes it); `test_input_untouched` (input tree hashes are unchanged after merge); `test_paths_with_spaces`. The patch's `sce_discmap_patch.plt` and `sce_sys/` files are copied like any others.
- [ ] **Step 2:** Run `python3 -m unittest tests.test_merge -v`. Expected: FAIL.
- [ ] **Step 3:** Implement. Per file: on macOS try an APFS clone (`cp -c` via `subprocess.run([...])` argv list), on other OSes try `os.link`, fall back to `shutil.copy2`. Build into `out.tmp` and rename to `out` at the end.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Run on the real dumps: `python3 -m pipeline.merge`. Verify: `work/merged/Media/level14` exists, `work/merged/Media/Managed/Assembly-CSharp.dll` is 3005440 bytes (the patch version), and `df` shows used space grew by far less than 10 GB.
- [ ] **Step 6:** Commit: `git commit -m "Add dump merge step"`.

### Task 4: Pinned tool download

**Files:**
- Create: `pipeline/tools.py`, add `"tools"` to `pipeline/versions.json`
- Test: `tests/test_tools.py`

**Interfaces:**
- Produces: `pipeline.tools.ensure(name: str, work_dir: Path) -> Path` (returns the tool's extracted directory). `versions.json` entries: `"assetripper": {"version", "url_<platform>", "sha256_<platform>"}` and `"il2cppdumper": {...}`, with platform in `{"macos-arm64", "win-x64"}`. Raises `ChecksumError`.

- [ ] **Step 1: Write failing tests** with a local HTTP server (`http.server` on a thread) serving a fixture zip: `test_downloads_and_extracts`; `test_checksum_mismatch_raises_and_deletes`; `test_cached_tool_not_redownloaded`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement with `urllib.request` and `zipfile`. Mark the executable bit on macOS.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Pin real releases: the latest AssetRipper GUI release from github.com/AssetRipper/AssetRipper with both platform zips, and Il2CppDumper from github.com/Perfare/Il2CppDumper (runs on .NET, net6+ build). Compute and record sha256. Verify: `python3 -c "from pipeline.tools import ensure; ..."` fetches both.
- [ ] **Step 6:** Commit: `git commit -m "Add pinned tool download"`.

### Task 5: Extract the game with AssetRipper

**Files:**
- Create: `pipeline/assetripper.py`, `pipeline/extract_game.py`
- Test: `tests/test_assetripper.py`

**Interfaces:**
- Consumes: `tools.ensure("assetripper", ...)`, merge output.
- Produces: `pipeline.assetripper.export(input_dir: Path, out_dir: Path, settings: dict) -> None`, which starts AssetRipper headless on a free localhost port, loads the folder, exports a Unity project, stops the process, and writes `out_dir/.export-complete` last. CLI `python3 -m pipeline.extract_game` -> `work/export/game`.

- [ ] **Step 1:** Run the pinned AssetRipper with `--help` and read its web API (the GUI is a local web server; loading and exporting are HTTP POST endpoints). Write the endpoint names and the settings keys into `docs/notes/assetripper.md`. Settings to set: script export as decompiled C#, export Unity project, the shader export mode that yields the most usable shaders (compare decompiled vs. dummy on a sample, record the choice).
- [ ] **Step 2: Write failing tests** with a fake AssetRipper (a small Python HTTP server mimicking the endpoints from Step 1): `test_export_calls_load_then_export`; `test_marker_written_last` (fake fails mid-export -> no marker); `test_process_stopped_on_error`.
- [ ] **Step 3:** Run. Expected: FAIL.
- [ ] **Step 4:** Implement.
- [ ] **Step 5:** Run. Expected: PASS.
- [ ] **Step 6:** Run on real data. Verify: `work/export/game` contains `Assets/Scripts/Assembly-CSharp/` with `.cs` files including `UDPCommsProviderBehaviour.cs` and `JSONRPCReceive.cs`, 15 scenes (level0 to level14), and `Assets/StreamingAssets/` (or the copied `StreamingAssets` equivalent) with `Localisation/en_text.loc`. Record counts in `docs/notes/assetripper.md`.
- [ ] **Step 7:** Commit: `git commit -m "Add game extraction step"`.

### Task 6: Extract the phone app (assets and Il2Cpp metadata)

**Files:**
- Create: `pipeline/extract_app.py`
- Test: `tests/test_extract_app.py`

**Interfaces:**
- Consumes: `assetripper.export`, `tools.ensure("il2cppdumper", ...)`.
- Produces: `work/export/app` (AssetRipper project from the APK) and `work/export/app-il2cpp/{dump.cs,script.json,il2cpp.h}` (Il2CppDumper on `lib/arm64-v8a/libil2cpp.so` + `assets/bin/Data/Managed/Metadata/global-metadata.dat`). The APK is unzipped into `work/apk/` first.

- [ ] **Step 1: Write failing tests:** `test_unzip_paths_safe` (zip entry `../evil` rejected); `test_il2cppdumper_invoked_with_arm64_lib_and_metadata` (subprocess mocked, argv asserted).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Il2CppDumper needs .NET; detect `dotnet` and fail with a clear message if missing.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Run on real data. Verify: `dump.cs` contains `class CameraEffectsPanelOverlayView` and `CameraEffectName_Twist`. `work/export/app` contains Texture2D/Sprite and Shader assets (list the shader names matching the 11 effects into `docs/notes/app-extract.md`).
- [ ] **Step 6:** Commit: `git commit -m "Add phone app extraction step"`.

### Task 7: Local patches and publication guard

**Files:**
- Create: `pipeline/patches.py`, `local-patches/README.md`, `hooks/pre-push`, `pipeline/install_hooks.py`
- Test: `tests/test_patches.py`, `tests/test_prepush.py`

**Interfaces:**
- Produces: `pipeline.patches.apply_all(project: Path, patch_dir: Path = Path("local-patches")) -> list[str]` applies `local-patches/*.patch` in name order with `git apply --directory=<rel> --whitespace=nowarn` (argv list, run against the project dir) and returns applied names. Raises `PatchError(name, stderr)`. Each patch has a sibling `<name>.md` saying what it changes and why (needed for Plan 7). `pipeline.patches.make(project: Path, name: str)` writes a patch from the project's diff against the pristine export (`work/export/game-pristine`, copied after Task 5).
- Hook: `hooks/pre-push` (Python script) refuses any push while `local-patches/` contains any `*.patch` file, and refuses any push whose diff contains the string set in git config `iwyk.privateUrl` (set locally to the owner's page host). `install_hooks.py` sets `core.hooksPath=hooks`.

- [ ] **Step 1: Write failing tests:** `test_apply_in_order`; `test_failed_patch_raises_with_name`; `test_patch_without_md_rejected`; `test_prepush_blocks_with_patches` (temp repo with a bare remote, push exits non-zero, message mentions Plan 7); `test_prepush_blocks_private_url`; `test_prepush_allows_clean_repo`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. `local-patches/README.md` repeats the warning: these patches contain decompiled code and must be converted before publication.
- [ ] **Step 4:** Run. Expected: PASS. Run `python3 -m pipeline.install_hooks` and set `git config iwyk.privateUrl <owner host>` locally.
- [ ] **Step 5:** Commit: `git commit -m "Add local patch tooling and push guard"`.

### Task 8: First Unity import and compile report

**Files:**
- Create: `pipeline/unity.py`, `pipeline/compile_report.py`, `pipeline/build_all.py`
- Test: `tests/test_compile_report.py`

**Interfaces:**
- Consumes: `Config.unity_editor`, `versions.json["unity"]`, `work/export/game`.
- Produces: `pipeline.unity.run_batch(project: Path, args: list[str], log: Path) -> int` (runs `-batchmode -nographics -quit -projectPath <p> -logFile <log>`). `pipeline.compile_report.parse(log: Path) -> Report` with errors grouped by `(code, assembly, file)` and categories `ps4_api` (missing `UnityEngine.PS4`, `Sony.*`, `Sce*`), `removed_unity_api`, `third_party` (NGUI, PlayMaker, Amplify), `unityscript`, `other`. Writes `work/reports/compile.md` and `.json`. `pipeline/build_all.py` runs merge -> extract_game -> extract_app -> copy pristine -> apply_all -> Unity import -> report, skipping completed steps.

- [ ] **Step 1: Write failing tests** with fixture log excerpts: `test_parses_cs_errors` (`Assets/Scripts/Assembly-CSharp/PS4Wifi.cs(12,7): error CS0234: The type or namespace name 'PS4' does not exist in the namespace 'UnityEngine'` -> category `ps4_api`); `test_groups_duplicates`; `test_no_errors_report_says_clean`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Unity opens the 5.3-era project and runs its API updater automatically; keep the log.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Run `python3 -m pipeline.build_all` on real data from a clean `work/` (except tools and merged). Verify: it finishes, `work/reports/compile.md` lists error counts per category, and a second run skips all completed steps in under a minute.
- [ ] **Step 6:** Summarize the report for the owner (counts per category, the top 10 error groups). This is the input for Plan 2.
- [ ] **Step 7:** Commit: `git commit -m "Add Unity import and compile report"`.
