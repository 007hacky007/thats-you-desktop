# Wwise Vorbis to Ogg Vorbis (pipeline/wwise_vorbis.py, pipeline/audio_convert.py)

All media of the game is Wwise 2015.1 Vorbis. `wwise_vorbis.wem_to_ogg` rewrites it into a standard Ogg Vorbis file without decoding: the audio bits are copied, only the stripped header fields are put back. The output decodes to the same PCM as the original.

Written from the Vorbis I specification, the Ogg framing spec (RFC 3533) and the public descriptions of the Wwise layout (ww2ogg, vgmstream). No code is copied. The codebook data is read at conversion time from the pinned vgmstream source (`src/coding/libs/vorbis_codebooks_wwise.h`). It is never committed, and tests use synthetic codebooks.

## Input format as implemented

RIFF/WAVE, little endian. Chunks seen: `fmt ` (0x42 bytes), `cue `, `LIST`/`adtl`/`labl`, `data`.

fmt (offsets from the chunk body):

| off | size | field |
|---|---|---|
| 0x00 | 2 | tag 0xFFFF |
| 0x02 | 2 | channels |
| 0x04 | 4 | sample rate |
| 0x10 | 2 | extra size 0x30 |
| 0x14 | 4 | channel layout (mask) |
| 0x18 | 4 | total PCM frames (`samples`) |
| 0x1c / 0x20 | 4+4 | loop start / end packet offsets (unused here) |
| 0x28 | 4 | seek table size (setup packet offset within data) |
| 0x2c | 4 | first audio packet offset within data |
| 0x30 | 2 | max packet size |
| 0x40 / 0x41 | 1+1 | short / long block size exponents (always 8 / 11 in this game) |

- `cue ` entries are 24 bytes (id, sample position, ...). `labl` maps a cue id to a NUL-terminated text. Every file has a cue at 0 carrying the original asset name. That name becomes `label`, and all cues are listed in `cues`.
- data = seek table, then the setup packet, then audio packets. Each packet has a 2-byte size header and no granule ("type 2").

Setup packet (bit-packed, LSB first) compared with the standard setup header:

- Codebooks: count-1 (8 bits), then a 10-bit ID per book into the external library. aoTuV 6.03 list first, with the standard list as a retry. A packed book has 4-bit dimensions, 14-bit entries, no sync pattern, a 3-bit width for unordered lengths (written as 5 bits) and a 1-bit lookup type (written as 4 bits). The size check (packed bytes equal the bits read, rounded up, or plus one byte) rejects the wrong list.
- Time domain section is missing. We write count 0 and one 16-bit zero value.
- Floors drop the 16-bit type (always 1). Residues have a 2-bit type (written as 16 bits). Mappings drop the 16-bit type (always 0). Modes drop window type and transform type (16+16 bits, written as zero). All other fields are bit-identical and copied with range checks.
- The framing bit is added at the end.

Audio packets ("modified" packets, used when the two block sizes differ):

- No packet-type bit. The packet starts with the mode number (ilog(modes-1) bits).
- Long-block packets have no previous/next window flags. We take them from the previous packet's block flag and the next packet's mode (0 after the last packet).
- Rebuilt as `0 | mode | [prev, next] | rest`, with Python big ints (LSB-first, so insertion is shifts and ors). The result is always size+1 bytes, the same as vgmstream.
- With equal block sizes the packets are already standard and are copied.

## Output

- Page 0: identification header alone (BOS). Page 1: comment header (vendor `PortAudio remux`, `WWISE_ID=<id>` for numeric media) and setup header, flushed.
- Audio pages: about 4 KB each, packets are split across pages when 255 segments are full.
- Granule: the first audio packet is 0, then each packet adds prev_blocksize/4 + cur_blocksize/4. The last (EOS) page holds the header sample count. In the real data the header count is always within the last packet (0..1088 frames below the computed end), so decoders trim only the tail.
- CRC: `rev32(zlib.crc32(page.translate(REV8), 0xFFFFFFFF) ^ 0xFFFFFFFF)`, checked in tests against a bitwise MSB-first reference.

## Converter

`audio_convert.convert` (design 3.4/3.5/4.4):

- **Sources.**
  - Bank DIDX/DATA blobs, read with our own chunk reader. BKHD language id is recorded.
  - Loose numeric `<Lang>/<id>.wem` files, filtered by `wanted[scope]`.
  - Every named `<Lang>/<prefix>_<name>.wem` (external source).
  - `Wwise.dat` is ignored.
- **Scopes and dedupe.** Scope is `li` or `lang_key(folder)`. Sources are deduped by (scope, id), and a loose file beats an embedded copy.
- **Outputs:**
  - `media/<id>.ogg` for LI media.
  - `lang/<key>/<id>.ogg` for localized media.
  - `lang/<key>/ext/<sanitized stem>.ogg` for external sources. `name` is the lowercased stem, and `externalPrefix` is the most common stem prefix (for example `en-GB_`).
  - Tables `media.json` and `lang/<key>/media.json`, and `report_dir/convert-report.json`. The report also has `filtered`, `duplicates`, `verify.identical` and `totals.pruned`.
- **Execution.**
  - A spawn pool runs the work, largest sources first, with temp-plus-rename writes.
  - The per-scope index is `report_dir/convert-state/<scope>.json`, keyed by source size, mtime, offset/length and `VERSION`. A WAV fallback is kept until the source or VERSION changes.
  - Outputs that are no longer produced are pruned, together with stale `.tmp` files.
- **Verify** (`vgmstream-cli -i -o`, comparing equal frames, channels and rate, and max abs PCM diff <= 1):
  - `sample`: 2% per scope (at least 4, chosen by CRC of the source key), plus every stereo or 44.1 kHz LI item.
  - `all`: every file.
  - A failed remux or check falls back to a vgmstream WAV (`format: wav`).
- **Debug:** `python3 -m pipeline.audio_convert --one <wem> --codebooks <h> -o x.ogg`. Full scratch run: `--source <PS4 dir> -o <out> --report-dir <dir> [--vgmstream <cli>] [--verify all]`.

## Verification results (2026-11-26, macOS arm64, 10 cores, vgmstream r2168 built from source)

- **Scratch subset 1:** all 13 LI banks (1878 unique embedded media, 4 cross-bank duplicates), the localized banks, and 300 random loose wems per language. 4596 files, 632 MB in, 652 MB out.
  - `--verify all`: 4596/4596 decode identically to vgmstream on the original (bit-exact PCM, equal frame counts), with 0 fallbacks. 52 s.
  - `--verify sample`: 1496 checked, all identical. 29 s.
- **Scratch subset 2:** 1173 files, `--verify all`, all identical in 7 s. An unchanged rerun skips everything in 0.1 s.
- Output is about 3% larger than the source, from the Ogg page overhead plus the one byte added per packet. The full set should take a few minutes.
- Formats seen in loose wems: all mono, 48 kHz except one 44.1 kHz file, block sizes 256/2048. LI music and SFX are mostly stereo, at 48 kHz and 44.1 kHz.
- Opt-in real-data test: `PORT_AUDIO_REAL=1 python3 -m unittest tests.test_wwise_vorbis`. It uses `PORT_VGMSTREAM`, `work/tools/vgmstream-r2168-macos-arm64/vgmstream-cli`, or `work/scratch-audio/src/build/cli/vgmstream-cli`.
