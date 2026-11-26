"""Convert the game's Wwise media into Ogg Vorbis files plus media tables.

Sources under <source_root> (GeneratedSoundBanks/PS4):

- embedded media: DIDX/DATA of every bank (LocaleIndependent and localized);
- streamed media: loose numeric <Lang>/<id>.wem (filtered by `wanted`);
- external sources: every named <Lang>/<prefix>_<name>.wem.

Each is remuxed losslessly with pipeline.wwise_vorbis. A sample (or all) is
checked against vgmstream; anything that cannot be remuxed or fails the check
is decoded by vgmstream to WAV instead. Outputs follow the design's staged
layout (3.4) and media table format (4.4). See docs/notes/wwise-vorbis.md.
"""
from __future__ import annotations

import array
import collections
import json
import math
import multiprocessing
import os
import re
import struct
import subprocess
import sys
import tempfile
import time
import zlib
from dataclasses import asdict, dataclass
from pathlib import Path

from pipeline import wwise_vorbis

VERSION = "1"
LI_FOLDER = "LocaleIndependent"
LI = "li"
SAMPLE_FRACTION = 0.02
SAMPLE_MIN = 4
VERIFY_MODES = ("none", "sample", "all")
_JSON = {"separators": (",", ":"), "ensure_ascii": False}


def lang_key(name: str) -> str:
    """Lowercase; each run of chars outside [a-z0-9] becomes '_'; strip '_'."""
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def sanitize_name(stem: str) -> str:
    return re.sub(r"[^A-Za-z0-9._-]", "_", stem)


# ---------------------------------------------------------------- banks

def read_bank_media(path: Path) -> dict:
    """BKHD header and DIDX entries of a bank, without reading DATA.

    Returns {"version", "id", "language_id", "media": [(id, file_offset, size)]}.
    """
    out = {"version": None, "id": None, "language_id": None, "media": []}
    didx = []
    data_at = data_size = None
    with open(path, "rb") as f:
        total = os.fstat(f.fileno()).st_size
        pos = 0
        while pos + 8 <= total:
            f.seek(pos)
            head = f.read(8)
            cid, size = head[:4], struct.unpack("<I", head[4:])[0]
            body_at = pos + 8
            if cid == b"BKHD":
                body = f.read(min(size, 16))
                if len(body) >= 12:
                    out["version"], out["id"], out["language_id"] = struct.unpack_from("<III", body)
            elif cid == b"DIDX":
                body = f.read(size)
                didx = [struct.unpack_from("<III", body, i) for i in range(0, len(body) - 11, 12)]
            elif cid == b"DATA":
                data_at, data_size = body_at, size
            pos = body_at + size
    if didx and data_at is None:
        raise ValueError(f"{path.name}: DIDX without DATA")
    for mid, off, size in didx:
        if off + size > data_size:
            raise ValueError(f"{path.name}: media {mid} outside DATA")
        out["media"].append((mid, data_at + off, size))
    return out


# ---------------------------------------------------------------- sources

@dataclass
class Source:
    scope: str
    kind: str            # embedded | streamed | external
    key: str             # report/index key, relative to source_root
    path: str
    offset: int
    size: int
    out: str             # output path relative to out_root, without extension
    id: int | None = None
    name: str | None = None
    sample: bool = False


def _scope_of(folder: str) -> str:
    return LI if folder == LI_FOLDER else lang_key(folder)


def _out_base(scope: str, mid: int) -> str:
    return f"media/{mid}" if scope == LI else f"lang/{scope}/{mid}"


def enumerate_sources(source_root: Path, wanted: dict[str, set[int]] | None = None):
    """Returns (sources, info) where info has per-scope language, prefix, filtered counts."""
    source_root = Path(source_root)
    chosen: dict[tuple[str, int], Source] = {}
    externals: list[Source] = []
    info: dict[str, dict] = {}
    for folder in sorted(p.name for p in source_root.iterdir() if p.is_dir()):
        scope = _scope_of(folder)
        meta = info.setdefault(scope, {"language": "" if scope == LI else folder, "externalPrefix": "",
                                       "filtered": 0, "duplicates": 0, "bankLanguages": {}})
        d = source_root / folder
        files = sorted(p for p in d.iterdir() if p.is_file())
        for bank in (p for p in files if p.suffix.lower() == ".bnk"):
            b = read_bank_media(bank)
            meta["bankLanguages"][bank.stem] = b["language_id"]
            for mid, off, size in b["media"]:
                k = (scope, mid)
                if k in chosen:
                    meta["duplicates"] += 1
                    continue
                chosen[k] = Source(scope, "embedded", f"{folder}/{bank.name}#{mid}", str(bank), off, size,
                                   _out_base(scope, mid), id=mid)
        stems_used: set[str] = set()
        prefixes = collections.Counter()
        for wem in (p for p in files if p.suffix.lower() == ".wem"):
            size = wem.stat().st_size
            if wem.stem.isdigit():
                mid = int(wem.stem)
                if wanted is not None and mid not in wanted.get(scope, ()):
                    meta["filtered"] += 1
                    continue
                k = (scope, mid)
                if k in chosen:
                    meta["duplicates"] += 1
                    if chosen[k].kind == "streamed":
                        continue
                chosen[k] = Source(scope, "streamed", f"{folder}/{wem.name}", str(wem), 0, size,
                                   _out_base(scope, mid), id=mid)
            else:
                base = sanitize_name(wem.stem)
                cand, n = base, 1
                while cand.lower() in stems_used:
                    n += 1
                    cand = f"{base}_{n}"
                stems_used.add(cand.lower())
                if "_" in wem.stem:
                    prefixes[wem.stem.split("_", 1)[0] + "_"] += 1
                ext_dir = "media/ext" if scope == LI else f"lang/{scope}/ext"
                externals.append(Source(scope, "external", f"{folder}/{wem.name}", str(wem), 0, size,
                                        f"{ext_dir}/{cand}", name=wem.stem.lower()))
        if prefixes:
            meta["externalPrefix"] = prefixes.most_common(1)[0][0]
    sources = sorted(chosen.values(), key=lambda s: (s.scope, s.id)) + externals
    by_scope = collections.defaultdict(list)
    for s in sources:
        by_scope[s.scope].append(s)
    for items in by_scope.values():
        n = max(min(len(items), SAMPLE_MIN), math.ceil(len(items) * SAMPLE_FRACTION))
        for s in sorted(items, key=lambda s: (zlib.crc32(s.key.encode()), s.key))[:n]:
            s.sample = True
    return sources, info


# ---------------------------------------------------------------- WAV compare

def wav_info(data: bytes) -> dict:
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError("not a WAV file")
    pos, fmt, body = 12, None, None
    while pos + 8 <= len(data):
        cid, size = data[pos:pos + 4], struct.unpack_from("<I", data, pos + 4)[0]
        if cid == b"fmt ":
            fmt = struct.unpack_from("<HHIIHH", data, pos + 8)
        elif cid == b"data":
            body = data[pos + 8:pos + 8 + size]
        pos += 8 + size + (size & 1)
    if fmt is None or body is None:
        raise ValueError("WAV without fmt or data")
    tag, channels, rate, _, align, bits = fmt
    if bits != 16 or not channels:
        raise ValueError(f"unexpected WAV format {tag}/{bits} bit")
    return {"channels": channels, "rate": rate, "frames": len(body) // (2 * channels), "pcm": body}


def compare_wavs(a: bytes, b: bytes, tolerance: int = 1) -> tuple[bool, str]:
    """Equal frames, channels and rate, and max |PCM diff| <= tolerance."""
    try:
        x, y = wav_info(a), wav_info(b)
    except ValueError as e:
        return False, str(e)
    for k in ("channels", "rate", "frames"):
        if x[k] != y[k]:
            return False, f"{k} {x[k]} != {y[k]}"
    if x["pcm"] == y["pcm"]:
        return True, "identical"
    p, q = array.array("h"), array.array("h")
    p.frombytes(x["pcm"][:len(x["pcm"]) & ~1])
    q.frombytes(y["pcm"][:len(y["pcm"]) & ~1])
    if sys.byteorder != "little":
        p.byteswap()
        q.byteswap()
    worst = max(abs(i - j) for i, j in zip(p, q))
    return worst <= tolerance, f"max diff {worst}"


# ---------------------------------------------------------------- worker

_W: dict = {}


def _init_worker(codebooks_header: str, vgmstream: str | None, out_root: str, tmp_root: str, verify: str):
    _W.clear()
    _W.update(lib=wwise_vorbis.CodebookLibrary.from_vgmstream_header(Path(codebooks_header)),
              vg=vgmstream, out=Path(out_root), tmp=Path(tmp_root), verify=verify)


def _read_source(s: dict) -> bytes:
    with open(s["path"], "rb") as f:
        f.seek(s["offset"])
        data = f.read(s["size"])
    if len(data) != s["size"]:
        raise OSError("short read")
    return data


def _atomic_write(dst: Path, data: bytes) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_name(f".{dst.name}.{os.getpid()}.tmp")
    tmp.write_bytes(data)
    os.replace(tmp, dst)


def _vgm_decode(src: Path, dst: Path) -> None:
    r = subprocess.run([_W["vg"], "-i", "-o", str(dst), str(src)], capture_output=True, text=True, timeout=600)
    if r.returncode != 0 or not dst.is_file():
        tail = (r.stderr or r.stdout).strip().splitlines()[-1:] or ["no output"]
        raise RuntimeError(f"vgmstream failed: {tail[0]}")


def _entry(s: dict, fmt: str, info: dict, samples: int, nbytes: int, rel: str) -> dict:
    e = {"id": s["id"]} if s["kind"] != "external" else {"name": s["name"]}
    e.update(file=rel, format=fmt, rate=info["rate"], channels=info["channels"], samples=samples, bytes=nbytes)
    if info.get("label"):
        e["label"] = info["label"]
    if info.get("cues"):
        e["cues"] = [[p, t] for p, t in info["cues"]]
    return e


def convert_one(s: dict) -> dict:
    """Worker body: convert one source. Never raises; returns a result dict."""
    res = {"key": s["key"], "scope": s["scope"], "status": "failed", "error": None, "verified": False,
           "mismatch": None, "entry": None}
    out_root, tmp_root = _W["out"], _W["tmp"]
    try:
        data = _read_source(s)
        info = wwise_vorbis.wem_info(data)
    except (OSError, wwise_vorbis.WemError, struct.error) as e:
        res["error"] = f"read: {e}"
        return res
    ogg = None
    try:
        ogg = wwise_vorbis.wem_to_ogg(data, _W["lib"], serial=zlib.crc32(s["key"].encode()),
                                      wwise_id=s["id"])
    except (wwise_vorbis.WemError, struct.error, IndexError) as e:
        res["error"] = f"remux: {e}"

    mode = _W["verify"]
    want_check = ogg is not None and _W["vg"] and (
        mode == "all" or (mode == "sample" and (
            s["sample"] or (s["scope"] == LI and (info["channels"] == 2 or info["rate"] == 44100)))))
    with tempfile.TemporaryDirectory(dir=tmp_root) as td:
        td = Path(td)
        wem_path = Path(s["path"])
        if s["kind"] == "embedded" and (want_check or ogg is None):
            wem_path = td / f"{s['id']}.wem"
            wem_path.write_bytes(data)
        if want_check:
            try:
                (td / "x.ogg").write_bytes(ogg)
                _vgm_decode(wem_path, td / "ref.wav")
                _vgm_decode(td / "x.ogg", td / "ogg.wav")
                ok, why = compare_wavs((td / "ref.wav").read_bytes(), (td / "ogg.wav").read_bytes())
            except (RuntimeError, OSError, subprocess.TimeoutExpired) as e:
                ok, why = False, str(e)
            res["verified"] = True
            res["identical"] = ok and why == "identical"
            if not ok:
                res["mismatch"] = why
                res["error"] = f"verify: {why}"
                ogg = None
        if ogg is not None:
            rel = s["out"] + ".ogg"
            _atomic_write(out_root / rel, ogg)
            res.update(status="ok", error=None, entry=_entry(s, "ogg", info, info["samples"], len(ogg), rel))
            return res
        if not _W["vg"]:
            return res
        rel = s["out"] + ".wav"
        try:
            wav = td / "fallback.wav"
            _vgm_decode(wem_path, wav)
            w = wav_info(wav.read_bytes())
            nbytes = wav.stat().st_size
            dst = out_root / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            tmp = dst.with_name(f".{dst.name}.{os.getpid()}.tmp")
            with open(wav, "rb") as fi, open(tmp, "wb") as fo:
                while chunk := fi.read(1 << 20):
                    fo.write(chunk)
            os.replace(tmp, dst)
        except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as e:
            res["error"] = f"{res['error']}; fallback: {e}"
            return res
        res.update(status="wav", entry=_entry(s, "wav", {**info, "channels": w["channels"], "rate": w["rate"]},
                                             w["frames"], nbytes, rel))
        return res


# ---------------------------------------------------------------- incremental index

def _load_index(path: Path) -> dict:
    try:
        idx = json.loads(path.read_text())
        if idx.get("version") == VERSION:
            return idx.get("items", {})
    except (OSError, ValueError, AttributeError):
        pass
    return {}


def _source_stamp(s: Source) -> dict:
    st = os.stat(s.path)
    return {"size": st.st_size, "mtime": st.st_mtime_ns, "offset": s.offset, "length": s.size}


# ---------------------------------------------------------------- driver

def _prune(out_root: Path, keep: set[str], scopes: list[str]) -> int:
    removed = 0
    dirs = []
    for scope in scopes:
        if scope == LI:
            dirs += [out_root / "media", out_root / "media" / "ext"]
        else:
            dirs += [out_root / "lang" / scope, out_root / "lang" / scope / "ext"]
    for d in dirs:
        if not d.is_dir():
            continue
        for p in d.iterdir():
            if not p.is_file():
                continue
            rel = p.relative_to(out_root).as_posix()
            stale_tmp = p.name.startswith(".") and p.name.endswith(".tmp")
            if stale_tmp or (p.suffix in (".ogg", ".wav") and rel not in keep):
                p.unlink()
                removed += 1
    return removed


def _media_table(scope: str, meta: dict, entries: list[dict]) -> dict:
    media = sorted((e for e in entries if "id" in e), key=lambda e: e["id"])
    external = sorted((e for e in entries if "name" in e), key=lambda e: e["name"])
    return {"format": 1, "scope": scope, "language": meta["language"], "externalPrefix": meta["externalPrefix"],
            "media": media, "external": external}


def _write_json(path: Path, obj) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.tmp")
    tmp.write_text(json.dumps(obj, **_JSON), encoding="utf-8")
    os.replace(tmp, path)


def convert(source_root: Path, out_root: Path, codebooks_header: Path, *, report_dir: Path,
            vgmstream: Path | None = None, wanted: dict[str, set[int]] | None = None,
            jobs: int | None = None, verify: str = "sample", log=print) -> dict:
    """Convert all media; writes media tables and report_dir/convert-report.json."""
    if verify not in VERIFY_MODES:
        raise ValueError(f"verify must be one of {VERIFY_MODES}")
    t0 = time.monotonic()
    source_root, out_root, report_dir = Path(source_root), Path(out_root), Path(report_dir)
    out_root.mkdir(parents=True, exist_ok=True)
    report_dir.mkdir(parents=True, exist_ok=True)
    state_dir = report_dir / "convert-state"
    tmp_root = out_root.parent / "tmp" / "convert"
    tmp_root.mkdir(parents=True, exist_ok=True)
    wwise_vorbis.CodebookLibrary.from_vgmstream_header(Path(codebooks_header))  # fail early
    vg = str(vgmstream) if vgmstream else None
    if vg is None and verify != "none":
        log("audio_convert: no vgmstream, verification and WAV fallback are off")

    sources, info = enumerate_sources(source_root, wanted)
    scopes = sorted(info)
    indexes = {sc: _load_index(state_dir / f"{sc}.json") for sc in scopes}
    new_index: dict[str, dict] = {sc: {} for sc in scopes}
    results: dict[str, dict] = {}
    todo: list[dict] = []
    skipped = collections.Counter()
    for s in sources:
        stamp = _source_stamp(s)
        old = indexes[s.scope].get(s.key)
        if (old and old.get("stamp") == stamp and old.get("status") in ("ok", "wav") and old.get("entry")
                and not (verify == "all" and not old.get("verified"))):
            f = out_root / old["entry"]["file"]
            same_name = old["entry"]["file"].rsplit(".", 1)[0] == s.out
            if same_name and f.is_file() and f.stat().st_size == old["entry"]["bytes"]:
                results[s.key] = {**old, "skipped": True}
                new_index[s.scope][s.key] = old
                skipped[s.scope] += 1
                continue
        d = asdict(s)
        d["_stamp"] = stamp
        todo.append(d)
    log(f"audio_convert: {len(sources)} sources, {len(todo)} to convert, {sum(skipped.values())} up to date")

    todo.sort(key=lambda d: -d["size"])
    initargs = (str(codebooks_header), vg, str(out_root), str(tmp_root), verify)
    jobs = jobs or os.cpu_count() or 1
    done = 0
    last_log = time.monotonic()

    def take(r: dict, stamp: dict) -> None:
        nonlocal done, last_log
        done += 1
        r = {**r, "stamp": stamp}
        results[r["key"]] = r
        if r["status"] in ("ok", "wav"):
            new_index[r["scope"]][r["key"]] = {k: r[k] for k in ("stamp", "status", "entry", "verified")}
        if time.monotonic() - last_log > 15:
            last_log = time.monotonic()
            log(f"audio_convert: {done}/{len(todo)}")

    stamps = {d["key"]: d.pop("_stamp") for d in todo}
    if todo and jobs > 1:
        ctx = multiprocessing.get_context("spawn")
        with ctx.Pool(min(jobs, len(todo)), initializer=_init_worker, initargs=initargs) as pool:
            for r in pool.imap_unordered(convert_one, todo, chunksize=4):
                take(r, stamps[r["key"]])
    else:
        if todo:
            _init_worker(*initargs)
        for d in todo:
            r = convert_one(d)
            take(r, stamps[r["key"]])

    for sc in scopes:
        _write_json(state_dir / f"{sc}.json", {"version": VERSION, "items": new_index[sc]})

    # tables and report
    keep: set[str] = set()
    by_scope = collections.defaultdict(list)
    report_scopes = {}
    checked, identical, mismatch = 0, 0, []
    bytes_in = bytes_out = 0
    for sc in scopes:
        report_scopes[sc] = {"files": 0, "ok": 0, "wav": 0, "skipped": skipped[sc], "failed": [],
                             "filtered": info[sc]["filtered"], "duplicates": info[sc]["duplicates"]}
    for s in sources:
        r = results[s.key]
        rs = report_scopes[s.scope]
        rs["files"] += 1
        bytes_in += s.size
        if r["status"] in ("ok", "wav"):
            rs[r["status"]] += 1
            by_scope[s.scope].append(r["entry"])
            keep.add(r["entry"]["file"])
            bytes_out += r["entry"]["bytes"]
        else:
            rs["failed"].append({"source": s.key, "error": r.get("error") or "unknown"})
        if not r.get("skipped"):
            checked += 1 if r.get("verified") else 0
            identical += 1 if r.get("identical") else 0
            if r.get("mismatch"):
                mismatch.append({"source": s.key, "error": r["mismatch"]})
    for sc in scopes:
        table = _media_table(sc, info[sc], by_scope[sc])
        _write_json(out_root / ("media.json" if sc == LI else f"lang/{sc}/media.json"), table)
    pruned = _prune(out_root, keep, scopes)

    report = {"scopes": report_scopes,
              "verify": {"mode": verify if vg else "none", "checked": checked, "identical": identical,
                         "mismatch": mismatch},
              "totals": {"bytesIn": bytes_in, "bytesOut": bytes_out, "seconds": round(time.monotonic() - t0, 2),
                         "pruned": pruned}}
    _write_json(report_dir / "convert-report.json", report)
    nfail = sum(len(v["failed"]) for v in report_scopes.values())
    nwav = sum(v["wav"] for v in report_scopes.values())
    log(f"audio_convert: done in {report['totals']['seconds']} s, {nwav} wav fallback, {nfail} failed, "
        f"{checked} verified, {len(mismatch)} mismatch, {bytes_out / 1e6:.1f} MB out")
    return report


def main(argv=None) -> int:
    import argparse
    ap = argparse.ArgumentParser(description="Wwise media to Ogg Vorbis")
    ap.add_argument("--one", type=Path, help="convert one .wem (debug)")
    ap.add_argument("--codebooks", type=Path, required=True, help="vorbis_codebooks_wwise.h")
    ap.add_argument("-o", "--output", type=Path, help="output .ogg for --one, or output root")
    ap.add_argument("--source", type=Path, help="GeneratedSoundBanks/PS4 root (full run)")
    ap.add_argument("--report-dir", type=Path)
    ap.add_argument("--vgmstream", type=Path)
    ap.add_argument("--verify", choices=VERIFY_MODES, default="sample")
    ap.add_argument("--jobs", type=int)
    a = ap.parse_args(argv)
    if a.one:
        if not a.output:
            ap.error("--one needs -o")
        lib = wwise_vorbis.CodebookLibrary.from_vgmstream_header(a.codebooks)
        stem = a.one.stem
        data = a.one.read_bytes()
        a.output.write_bytes(wwise_vorbis.wem_to_ogg(data, lib, wwise_id=int(stem) if stem.isdigit() else None))
        i = wwise_vorbis.wem_info(data)
        print(f"{a.output}: {i['channels']} ch, {i['rate']} Hz, {i['samples']} samples, label {i['label']!r}")
        return 0
    if not (a.source and a.output and a.report_dir):
        ap.error("a full run needs --source, -o and --report-dir")
    r = convert(a.source, a.output, a.codebooks, report_dir=a.report_dir, vgmstream=a.vgmstream,
                jobs=a.jobs, verify=a.verify)
    return 1 if any(v["failed"] for v in r["scopes"].values()) else 0


if __name__ == "__main__":
    raise SystemExit(main())
