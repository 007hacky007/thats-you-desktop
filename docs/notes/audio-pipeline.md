# Audio pipeline

`python3 -m pipeline.audio` turns the PS4 Wwise 2015.1 audio (`work/merged/Media/StreamingAssets/Audio/GeneratedSoundBanks/PS4`, read only) into the PortAudio data the managed player reads, and installs it into the Unity project. The converted audio is game data. It lives in `work/` and in the build's StreamingAssets, and is never committed.

## Tools (pinned in pipeline/versions.json, sha256 checked by pipeline/tools.py)

| entry | version | use |
|---|---|---|
| `vgmstream` | r2168 | official win64 zip (self-contained), Windows only |
| `vgmstream-src` | r2168 | source tarball: macOS build of vgmstream-cli, and the codebook header |
| `libogg` | 1.3.6 | source for the macOS build |
| `libvorbis` | 1.3.7 | source for the macOS build |

`pipeline/audio_tools.py`:

- `ensure_vgmstream(work_dir)`:
  - Windows: `tools.ensure("vgmstream")`, returning `vgmstream-cli.exe`.
  - macOS: the official Mac zip links absolute Homebrew dylibs, so it is not used. vgmstream-cli is built from the three source tarballs with cmake:

    ```
    cmake -S <vgmstream-r2168> -B <build> -DCMAKE_BUILD_TYPE=Release -DUSE_FFMPEG=OFF -DUSE_MPEG=OFF
      -DUSE_CELT=OFF -DUSE_SPEEX=OFF -DBUILD_V123=OFF -DBUILD_AUDACIOUS=OFF -DOGG_PATH=<libogg-1.3.6>
      -DVORBIS_PATH=<libvorbis-1.3.7> -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0 -DCMAKE_POLICY_VERSION_MINIMUM=3.5
    cmake --build <build> --target vgmstream_cli
    ```

    - Output: `work/tools/vgmstream-r2168-macos-arm64/vgmstream-cli`. It links only libSystem.
    - `.tool-complete` holds a digest of the three sha256 pins and the flags. Changing a pin or a flag rebuilds it.
    - The build tree (`...-macos-arm64.build`) is deleted afterwards.
    - OGG_PATH and VORBIS_PATH avoid FetchContent, so the build needs no network beyond the pinned downloads.
    - Needs `cmake` (`brew install cmake`) and a C compiler (`xcode-select --install`). A missing one fails with a clear message.
- `ensure_codebooks(work_dir)` returns `work/tools/vgmstream-src-r2168/vgmstream-r2168/src/coding/libs/vorbis_codebooks_wwise.h`. The converter reads and parses it at conversion time.

Run just the tools: `python3 -m pipeline.audio_tools`.

### Codebook compliance note (owner decision)

The Wwise Vorbis media uses external aoTuV 6.03 codebooks. The codebook arrays in `vorbis_codebooks_wwise.h` are data that ww2ogg extracted from the Wwise SDK. vgmstream embeds the same data. We use it the way vgmstream does: downloaded from the pinned vgmstream release, read at build time, never committed, never in test fixtures. Tests use synthetic codebooks.

If the owner rejects this, the fallback is decode-and-re-encode (vgmstream to WAV, then a pinned Vorbis encoder). That is a separate decision.

## Steps

1. **tools:** `ensure_vgmstream`, `ensure_codebooks`.
2. **graph:** `audio_graph.build` writes `manifest.json`, `banks/**` and `reports/graph-report.json`. It takes seconds.
3. **referenced media:** `audio_graph.referenced_media(report)` gives media ids per scope. The converter uses it to skip stale numeric wems.
4. **convert:** `audio_convert.convert` does a lossless remux to Ogg Vorbis, with vgmstream verify and WAV fallback. It runs in a process pool and skips unchanged files.
5. **coverage:** `reports/coverage.json` and `coverage.md`.
   - Every event from the graph report: AK.cs name, bank, language, media count, external voice count, missing media (`not converted` or `file missing`), reachesNothing, missingTargets.
   - An event is `ok` when every reachable media file exists and it reaches something.
   - External voice files are resolved at runtime by name and are only counted.
6. **install:** `audio_install.install(work/audio/PortAudio, work/project)`.

Digest gating is stored in `work/audio/.audio-state.json`:

- graph: merged dump digest (`work/merged/.merge-manifest.json`), `audio_graph.VERSION`, and the AK.cs content.
- convert: merged digest, `audio_convert.VERSION`, the four tool pins, the verify mode, and the graph digest.
- A step is skipped when its digest is unchanged and its outputs exist.
- Coverage is recomputed every run. Install runs every time and is cheap.

## CLI

```
python3 -m pipeline.audio [--verify none|sample|all] [--jobs N] [--no-install] [--force] [--graph-only]
```

- `--verify`: compare remuxed media against vgmstream. The default is `sample`.
- `--no-install`: stage only and do not touch the project.
- `--graph-only`: graph plus coverage. No tools, convert or install.
- `--force`: ignore the state file.
- Paths come from `config/local.json`. The names file is `work/project/Assets/Scripts/Assembly-CSharp/AK.cs` when it exists.

## Layout

```
work/tools/vgmstream-src-r2168/ libogg-1.3.6/ libvorbis-1.3.7/ vgmstream-r2168-macos-arm64/
work/audio/PortAudio/      manifest.json, banks/**, media.json, media/*.ogg|wav, lang/<key>/{media.json,*.ogg,ext/*.ogg}
work/audio/reports/        graph-report.json, convert-report.json, coverage.json, coverage.md
work/audio/.audio-state.json, work/audio/tmp/
work/project/Assets/StreamingAssets/PortAudio/   installed copy (APFS clones)
```

## Install and build hooks

- `audio_install.install(staged, project)` mirrors the staged tree into `Assets/StreamingAssets/PortAudio`.
  - It clones or copies (`merge._place_file`) only the files whose size or mtime_ns differ.
  - It deletes installed files that are no longer staged, with their `.meta`. Folder `.meta` files of folders that are gone are deleted too.
  - It then calls `prune_wwise_audio`.
  - It returns `{copied, unchanged, deleted, pruned}`.
- `audio_install.prune_wwise_audio(project)` deletes `Assets/StreamingAssets/Audio/GeneratedSoundBanks/PS4/**` except `LocaleIndependent/Init.bnk` and its `.meta`.
  - `AkBasePathGetter` disables audio init without Init.bnk, so that file is kept.
  - Empty folders are removed. Symlinks are removed, never followed.
  - A root that resolves outside the project is refused.
  - A project rebuild re-clones the PS4 audio from the export, and the next install prunes it again.
- `build_all.build_project`: `prepare_project`, then `audio.build(work, project)`, then the Unity import. Unity imports PortAudio once.
- `build_player.main`: after `sync_port`, it installs when `work/audio/PortAudio/manifest.json` exists. Otherwise it prints `audio: no converted audio (run python3 -m pipeline.audio); the player will be silent` and builds anyway.
- `PortBuild.BuildMac`:
  - `ProjectSettings/AudioManager.asset` ships with `m_DisableAudio: 1` and 32 real voices, because the original used Wwise. For the build, PortBuild sets `m_DisableAudio=false`, 128 real voices and 512 virtual voices through a SerializedObject. It restores the old values in the same try/finally as `useMacAppStoreValidation`. Log line: `[PortBuild] audio: Unity audio enabled for the build (real voices 128)`.
  - After the build it warns when `StreamingAssets/PortAudio/manifest.json` is missing in the app. It deletes any PS4 Wwise audio left in the app except `LocaleIndependent/Init.bnk`, and logs the counts.

## Disk and time

- vgmstream toolchain, first run on an M-series Mac: about 90 s including the downloads (about 3 MB of tarballs from github and xiph.org). Later runs are instant.
  - Disk: vgmstream-src 31 MB, libvorbis 7 MB, libogg 2 MB, binary 3 MB.
- Source audio: about 3.8 GB.
- Converted output: about 3.5 GB, a lossless remux about the size of the source, without the stale numeric wems. Decoding everything to WAV would need about 30 GB.
- The install is APFS clones, so it costs almost no extra disk on macOS. Pruning removes the 3.8 GB PS4 copy from the project and so from every build.
- Graph step: seconds. Convert step: a few minutes on all cores.
