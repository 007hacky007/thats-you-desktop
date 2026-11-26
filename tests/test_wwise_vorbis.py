"""Tests for pipeline.wwise_vorbis with synthetic Wwise Vorbis data only.

The checks use an independent, deliberately simple Ogg/Vorbis reader written
here (bit lists, table CRC) so the module is not checked against itself.
"""
import os
import random
import shutil
import struct
import subprocess
import tempfile
import unittest
import zlib
from pathlib import Path

from pipeline import wwise_vorbis as wv

ROOT = Path(__file__).resolve().parent.parent


# ---------------------------------------------------------------- independent helpers

class Bits:
    """Builds an LSB-first bitstream from a list of bits."""

    def __init__(self):
        self.bits = []

    def put(self, value, n):
        assert 0 <= value < (1 << n) or n == 0
        self.bits.extend((value >> i) & 1 for i in range(n))
        return self

    def tobytes(self):
        out = bytearray()
        for i in range(0, len(self.bits), 8):
            chunk = self.bits[i:i + 8]
            out.append(sum(b << k for k, b in enumerate(chunk)))
        return bytes(out)


class Unbits:
    def __init__(self, data):
        self.bits = [(byte >> k) & 1 for byte in data for k in range(8)]
        self.pos = 0

    def get(self, n):
        if self.pos + n > len(self.bits):
            raise EOFError
        v = sum(self.bits[self.pos + i] << i for i in range(n))
        self.pos += n
        return v


def ref_crc(data):
    """Bitwise MSB-first CRC-32, polynomial 0x04C11DB7, as in the Ogg spec."""
    crc = 0
    for byte in data:
        crc ^= byte << 24
        for _ in range(8):
            crc = ((crc << 1) ^ 0x04C11DB7) if crc & 0x80000000 else (crc << 1)
            crc &= 0xFFFFFFFF
    return crc


def read_pages(blob):
    pages = []
    pos = 0
    while pos < len(blob):
        assert blob[pos:pos + 4] == b"OggS", f"no capture pattern at {pos}"
        version, flags, granule, serial, seq, crc, nseg = struct.unpack_from("<BBqIIIB", blob, pos + 4)
        segs = list(blob[pos + 27:pos + 27 + nseg])
        body_len = sum(segs)
        end = pos + 27 + nseg + body_len
        raw = bytearray(blob[pos:end])
        raw[22:26] = b"\0\0\0\0"
        pages.append({"version": version, "flags": flags, "granule": granule, "serial": serial, "seq": seq,
                      "crc": crc, "crc_ok": ref_crc(bytes(raw)) == crc, "segs": segs,
                      "body": blob[pos + 27 + nseg:end]})
        pos = end
    return pages


def read_packets(pages):
    """[(packet bytes, granule of the page where it ends, page index)]"""
    out = []
    cur = b""
    for pi, p in enumerate(pages):
        off = 0
        for lv in p["segs"]:
            cur += p["body"][off:off + lv]
            off += lv
            if lv < 255:
                out.append((cur, p["granule"], pi))
                cur = b""
    assert cur == b"", "unterminated packet"
    return out


def ilog(v):
    n = 0
    while v > 0:
        n += 1
        v >>= 1
    return n


def parse_setup(pkt, channels):
    """Minimal Vorbis I setup header reader (spec 4.2.4). Returns a summary."""
    assert pkt[0] == 5 and pkt[1:7] == b"vorbis"
    r = Unbits(pkt[7:])
    books = []
    for _ in range(r.get(8) + 1):
        assert r.get(24) == 0x564342
        dims, entries = r.get(16), r.get(24)
        ordered = r.get(1)
        lengths = []
        if ordered:
            length = r.get(5) + 1
            while len(lengths) < entries:
                n = r.get(ilog(entries - len(lengths)))
                lengths += [length] * n
                length += 1
        else:
            sparse = r.get(1)
            for _ in range(entries):
                if sparse and not r.get(1):
                    lengths.append(0)
                else:
                    lengths.append(r.get(5) + 1)
        lookup = r.get(4)
        mults = []
        if lookup:
            assert lookup == 1
            r.get(32), r.get(32)
            vb = r.get(4) + 1
            r.get(1)
            q = 0
            while (q + 1) ** dims <= entries:
                q += 1
            mults = [r.get(vb) for _ in range(q)]
        books.append({"dims": dims, "entries": entries, "lengths": lengths, "lookup": lookup, "mults": mults})
    times = [r.get(16) for _ in range(r.get(6) + 1)]
    floors = []
    for _ in range(r.get(6) + 1):
        ftype = r.get(16)
        assert ftype == 1
        parts = r.get(5)
        classes = [r.get(4) for _ in range(parts)]
        dims = {}
        for c in range(max(classes) + 1 if classes else 0):
            dims[c] = r.get(3) + 1
            sub = r.get(2)
            if sub:
                r.get(8)
            for _ in range(1 << sub):
                r.get(8)
        mult = r.get(2) + 1
        rb = r.get(4)
        xs = [r.get(rb) for c in classes for _ in range(dims[c])]
        floors.append({"type": ftype, "classes": classes, "mult": mult, "xs": xs})
    residues = []
    for _ in range(r.get(6) + 1):
        rtype = r.get(16)
        begin, end, psize = r.get(24), r.get(24), r.get(24) + 1
        ncls = r.get(6) + 1
        classbook = r.get(8)
        casc = []
        for _ in range(ncls):
            low = r.get(3)
            high = r.get(5) if r.get(1) else 0
            casc.append(high * 8 + low)
        rbooks = [[r.get(8) if c & (1 << k) else None for k in range(8)] for c in casc]
        residues.append({"type": rtype, "begin": begin, "end": end, "psize": psize, "classbook": classbook,
                         "books": rbooks})
    mappings = []
    for _ in range(r.get(6) + 1):
        assert r.get(16) == 0
        submaps = r.get(4) + 1 if r.get(1) else 1
        coupling = []
        if r.get(1):
            for _ in range(r.get(8) + 1):
                coupling.append((r.get(ilog(channels - 1)), r.get(ilog(channels - 1))))
        assert r.get(2) == 0
        mux = [r.get(4) for _ in range(channels)] if submaps > 1 else []
        subs = [(r.get(8), r.get(8), r.get(8)) for _ in range(submaps)]
        mappings.append({"coupling": coupling, "mux": mux, "submaps": subs})
    modes = []
    for _ in range(r.get(6) + 1):
        modes.append({"blockflag": r.get(1), "window": r.get(16), "transform": r.get(16), "mapping": r.get(8)})
    framing = r.get(1)
    leftover = len(r.bits) - r.pos
    return {"books": books, "times": times, "floors": floors, "residues": residues, "mappings": mappings,
            "modes": modes, "framing": framing, "leftover": leftover}


# ---------------------------------------------------------------- synthetic Wwise data

def packed_book_unordered(dims, entries, lengths_minus1, width, sparse_mask=None, lookup=None):
    b = Bits().put(dims, 4).put(entries, 14).put(0, 1).put(width, 3).put(1 if sparse_mask else 0, 1)
    for i in range(entries):
        if sparse_mask:
            b.put(sparse_mask[i], 1)
            if not sparse_mask[i]:
                continue
        b.put(lengths_minus1[i], width)
    if lookup is None:
        b.put(0, 1)
    else:
        mn, mx, vb, seq, mults = lookup
        b.put(1, 1).put(mn, 32).put(mx, 32).put(vb - 1, 4).put(seq, 1)
        for m in mults:
            b.put(m, vb)
    return b.tobytes()


def packed_book_ordered(dims, entries, initial_minus1, counts):
    b = Bits().put(dims, 4).put(entries, 14).put(1, 1).put(initial_minus1, 5)
    cur = 0
    for c in counts:
        b.put(c, ilog(entries - cur))
        cur += c
    b.put(0, 1)
    return b.tobytes()


BOOK_A = packed_book_unordered(1, 4, [1, 1, 1, 1], 2)                                   # 4 x length 2
BOOK_B = packed_book_unordered(2, 9, [0, 2, 2, 2, 2, 3, 3, 3, 3], 3,
                               lookup=(0x60000000, 0x41000000, 2, 0, [0, 1, 2]))       # dims 2, lookup1
BOOK_C = packed_book_ordered(1, 8, 1, [2, 2, 4])                                        # ordered lengths 2,2,3,3,4..
BOOK_D = packed_book_unordered(1, 5, [0, 1, 2, 3, 3], 3, sparse_mask=[1, 0, 1, 1, 1])  # sparse


def c_array(name, data):
    rows = ",".join(f"0x{b:02x}" for b in data)
    return f"static const uint8_t {name}[] = {{\n    {rows},\n}};\n"


def synthetic_header():
    # The aoTuV list reuses "standard" arrays, like the real file; one entry
    # declares a size shorter than its array.
    padded_c = BOOK_C + b"\xee\xee"
    text = "/* synthetic codebooks for tests */\n#include \"x.h\"\n"
    text += c_array("vcb_standard_0000", BOOK_A) + c_array("vcb_standard_0001", BOOK_B)
    text += c_array("vcb_standard_0002", padded_c) + c_array("vcb_aotuv603_0003", BOOK_D)
    text += "static const vcb_info_t vcb_list_standard[] = {\n"
    text += f"    {{0x0000,0x{len(BOOK_A):04x},vcb_standard_0000}},\n    {{0x0001,0x{len(BOOK_B):04x},vcb_standard_0001}},\n"
    text += f"    {{0x0004,0x{len(BOOK_A):04x},vcb_standard_0000}},\n}};\n"
    text += "static const vcb_info_t vcb_list_aotuv603[] = {\n"
    text += f"    {{0x0000,0x{len(BOOK_B):04x},vcb_standard_0001}},\n"
    text += f"    {{0x0001,0x{len(BOOK_A):04x},vcb_standard_0000}},\n"
    text += f"    {{0x0002,{len(BOOK_C)},vcb_standard_0002}},\n"
    text += f"    {{0x0003,0x{len(BOOK_D):04x},vcb_aotuv603_0003}}\n}};\n"
    text += "static const int vcb_list_count_standard = 2;\n"
    return text


def wwise_setup(channels, book_ids, nmodes_flags):
    """Wwise-form setup: books (ids into aotuv list), 1 floor, 1 residue, 1 mapping, modes."""
    b = Bits().put(len(book_ids) - 1, 8)
    for cid in book_ids:
        b.put(cid, 10)
    # floors: 1, partitions 2 classes [0, 1]
    b.put(0, 6).put(2, 5).put(0, 4).put(1, 4)
    b.put(0, 3).put(0, 2).put(2, 8)            # class 0: dims 1, no subclasses, book 1 (plus1=2)
    b.put(1, 3).put(1, 2).put(0, 8)            # class 1: dims 2, 1 subclass bit, master 0
    b.put(0, 8).put(3, 8)                      #   sub books: none, book 2
    b.put(1, 2).put(7, 4)                      # multiplier 2, rangebits 7
    for x in (20, 60, 90):
        b.put(x, 7)
    # residues: 1 of type 2
    b.put(0, 6).put(2, 2).put(0, 24).put(128, 24).put(15, 24).put(1, 6).put(0, 8)
    b.put(1, 3).put(0, 1)                      # class 0 cascade: pass 0
    b.put(2, 3).put(1, 1).put(1, 5)            # class 1 cascade: low 2, high 1 -> bits 1 and 3
    b.put(1, 8)                                # class 0 pass 0 book
    b.put(3, 8).put(1, 8)                      # class 1 pass 1 and pass 3 books
    # mappings: 1
    b.put(0, 6).put(0, 1)
    if channels == 2:
        b.put(1, 1).put(0, 8).put(0, 1).put(1, 1)
    else:
        b.put(0, 1)
    b.put(0, 2).put(0, 8).put(0, 8).put(0, 8)
    # modes
    b.put(len(nmodes_flags) - 1, 6)
    for flag in nmodes_flags:
        b.put(flag, 1).put(0, 8)
    return b.tobytes()


def build_wem(channels=1, rate=48000, samples=None, small=8, big=11, modes=(0, 1), packets=None,
              book_ids=(0, 1, 2, 3), seek=b"\x11" * 8, cues=((0, "Synth_Label"), (480, "1j")),
              setup=None, trailing=b""):
    rnd = random.Random(7)
    mode_bits = ilog(len(modes) - 1)
    if packets is None:
        seq = [0, 1, 1, 0, 1, 1, 0]
        packets = []
        for m in seq:
            body = bytes(rnd.randrange(256) for _ in range(rnd.randrange(3, 40)))
            first = (body[0] & ~((1 << mode_bits) - 1) & 0xFF) | m
            packets.append(bytes([first]) + body[1:])
    if setup is None:
        setup = wwise_setup(channels, book_ids, modes)
    stream = seek + struct.pack("<H", len(setup)) + setup
    audio_off = len(stream)
    for p in packets:
        stream += struct.pack("<H", len(p)) + p
    stream += trailing
    if samples is None:
        samples = 4000
    fmt = struct.pack("<HHIIHHHHI", 0xFFFF, channels, rate, 12345, 0, 0, 0x30, 0, 4 if channels == 1 else 3)
    fmt += struct.pack("<IIIHHII", samples, audio_off, len(stream), 0, 0, len(seek), audio_off)
    fmt += struct.pack("<HHII", max(map(len, packets)), 0, 0, 0)
    fmt += b"\0" * 4 + bytes([small, big])
    assert len(fmt) == 0x42
    cue = struct.pack("<I", len(cues))
    labl = b""
    for i, (pos, text) in enumerate(cues, 1):
        cue += struct.pack("<II4sIII", i, pos, b"data", 0, 0, 0)
        t = text.encode() + b"\0"
        sub = struct.pack("<I", i) + t
        labl += b"labl" + struct.pack("<I", len(sub)) + sub + (b"\0" if len(sub) & 1 else b"")
    lst = b"adtl" + labl

    def chunk(cid, body):
        return cid + struct.pack("<I", len(body)) + body + (b"\0" if len(body) & 1 else b"")

    body = b"WAVE" + chunk(b"fmt ", fmt) + chunk(b"cue ", cue) + chunk(b"LIST", lst) + chunk(b"data", stream)
    return b"RIFF" + struct.pack("<I", len(body)) + body, packets


def library():
    return wv.CodebookLibrary.from_text(synthetic_header())


# ---------------------------------------------------------------- tests

class CodebookLibraryTests(unittest.TestCase):
    def test_parses_lists_arrays_and_sizes(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "vorbis_codebooks_wwise.h"
            p.write_text(synthetic_header())
            lib = wv.CodebookLibrary.from_vgmstream_header(p)
        self.assertEqual(list(lib.lists), ["aotuv603", "standard"])
        self.assertEqual(lib.lists["standard"], {0: BOOK_A, 1: BOOK_B, 4: BOOK_A})
        self.assertEqual(lib.lists["aotuv603"], {0: BOOK_B, 1: BOOK_A, 2: BOOK_C, 3: BOOK_D})

    def test_unknown_array_is_an_error(self):
        bad = "static const vcb_info_t vcb_list_aotuv603[] = {\n {0x0000,0x0004,vcb_missing},\n};\n"
        with self.assertRaises(wv.WemError):
            wv.CodebookLibrary.from_text(bad)

    def test_unpacked_book_is_standard_form(self):
        lib = library()
        for cid, dims, entries in ((0, 2, 9), (1, 1, 4), (2, 1, 8), (3, 1, 5)):
            value, nbits = lib.unpacked("aotuv603", cid)
            data = value.to_bytes((nbits + 7) // 8, "little")
            r = Unbits(data)
            self.assertEqual(r.get(24), 0x564342)
            self.assertEqual((r.get(16), r.get(24)), (dims, entries))
        # sparse book keeps the absent entry absent and the 5-bit lengths
        value, nbits = lib.unpacked("aotuv603", 3)
        setup = parse_setup(b"\x05vorbis" + _single_book_setup(value, nbits), 1)
        self.assertEqual(setup["books"][0]["lengths"], [1, 0, 3, 4, 4])

    def test_size_mismatch_rejected(self):
        with self.assertRaises(wv.WemError):
            wv.unpack_codebook(BOOK_A + b"\0\0\0", wv.BitWriter())


def _single_book_setup(value, nbits):
    """Wraps one standard codebook in a trivially valid rest-of-setup for parse_setup."""
    w = wv.BitWriter()
    w.write(0, 8)
    w.write(value, nbits)
    w.write(0, 6), w.write(0, 16)                             # time
    w.write(0, 6), w.write(1, 16), w.write(0, 5), w.write(0, 2), w.write(1, 4)   # floor, no partitions
    w.write(0, 6), w.write(0, 16), w.write(0, 24), w.write(0, 24), w.write(0, 24), w.write(0, 6), w.write(0, 8)
    w.write(0, 3), w.write(0, 1)                             # residue cascade
    w.write(0, 6), w.write(0, 16), w.write(0, 1), w.write(0, 1), w.write(0, 2)
    w.write(0, 24)                                           # submap
    w.write(0, 6), w.write(0, 1), w.write(0, 32), w.write(0, 8), w.write(1, 1)
    return w.getvalue()


class CrcTests(unittest.TestCase):
    def test_fast_crc_matches_reference(self):
        rnd = random.Random(3)
        for n in (0, 1, 27, 300, 5000):
            data = bytes(rnd.randrange(256) for _ in range(n))
            self.assertEqual(wv.ogg_crc(data), ref_crc(data))


class WemInfoTests(unittest.TestCase):
    def test_reads_fmt_vorb_cues_labels(self):
        wem, _ = build_wem(channels=2, rate=44100, samples=9999)
        info = wv.wem_info(wem)
        self.assertEqual((info["channels"], info["rate"], info["samples"]), (2, 44100, 9999))
        self.assertEqual(info["codec"], "vorbis")
        self.assertEqual(info["layout"], 3)
        self.assertEqual(info["cues"], [(0, "Synth_Label"), (480, "1j")])
        self.assertEqual(info["label"], "Synth_Label")
        self.assertEqual(info["vorbis"]["blocksize_exps"], (8, 11))
        self.assertFalse(info["truncated"])

    def test_not_riff(self):
        with self.assertRaises(wv.WemError):
            wv.wem_info(b"OggS" + b"\0" * 40)

    def test_truncated_data_flagged(self):
        wem, _ = build_wem()
        self.assertTrue(wv.wem_info(wem[:-10])["truncated"])
        with self.assertRaises(wv.WemError):
            wv.wem_to_ogg(wem[:-10], library())


class RemuxTests(unittest.TestCase):
    def remux(self, **kw):
        wem, packets = build_wem(**kw)
        ogg = wv.wem_to_ogg(wem, library(), serial=0x1234, wwise_id=42)
        pages = read_pages(ogg)
        return wem, packets, pages, read_packets(pages)

    def test_pages_and_headers(self):
        wem, packets, pages, pkts = self.remux(channels=2)
        self.assertTrue(all(p["crc_ok"] for p in pages))
        self.assertEqual([p["seq"] for p in pages], list(range(len(pages))))
        self.assertTrue(all(p["serial"] == 0x1234 and p["version"] == 0 for p in pages))
        self.assertEqual(pages[0]["flags"], 2)
        self.assertEqual(pages[-1]["flags"] & 4, 4)
        self.assertEqual(len(pkts), 3 + len(packets))
        # identification header alone on the first page
        ident, g, pi = pkts[0]
        self.assertEqual((pi, len(pages[0]["segs"])), (0, 1))
        self.assertEqual(ident[:7], b"\x01vorbis")
        ver, ch, rate, _, _, _, bs, framing = struct.unpack("<IBIiiiBB", ident[7:])
        self.assertEqual((ver, ch, rate, bs & 15, bs >> 4, framing), (0, 2, 48000, 8, 11, 1))
        # comment header
        com = pkts[1][0]
        self.assertEqual(com[:7], b"\x03vorbis")
        vlen = struct.unpack_from("<I", com, 7)[0]
        self.assertEqual(com[11:11 + vlen], b"PortAudio remux")
        n = struct.unpack_from("<I", com, 11 + vlen)[0]
        clen = struct.unpack_from("<I", com, 15 + vlen)[0]
        self.assertEqual((n, com[19 + vlen:19 + vlen + clen]), (1, b"WWISE_ID=42"))
        self.assertEqual(com[-1], 1)
        # setup ends its page; audio begins on a new page
        self.assertNotEqual(pkts[2][2], pkts[3][2])
        self.assertEqual(pkts[2][1], 0)

    def test_setup_translated(self):
        _, _, _, pkts = self.remux(channels=2)
        s = parse_setup(pkts[2][0], 2)
        self.assertEqual([b["entries"] for b in s["books"]], [9, 4, 8, 5])
        self.assertEqual(s["books"][0]["mults"], [0, 1, 2])
        self.assertEqual(s["books"][2]["lengths"], [2, 2, 3, 3, 4, 4, 4, 4])
        self.assertEqual(s["times"], [0])
        self.assertEqual(s["floors"], [{"type": 1, "classes": [0, 1], "mult": 2, "xs": [20, 60, 90]}])
        r = s["residues"][0]
        self.assertEqual((r["type"], r["begin"], r["end"], r["psize"], r["classbook"]), (2, 0, 128, 16, 0))
        self.assertEqual(r["books"][0][0], 1)
        self.assertEqual((r["books"][1][1], r["books"][1][3]), (3, 1))
        self.assertEqual(s["mappings"], [{"coupling": [(0, 1)], "mux": [], "submaps": [(0, 0, 0)]}])
        self.assertEqual([(m["blockflag"], m["window"], m["transform"], m["mapping"]) for m in s["modes"]],
                         [(0, 0, 0, 0), (1, 0, 0, 0)])
        self.assertEqual(s["framing"], 1)
        self.assertLess(s["leftover"], 8)

    def test_audio_packets_rebuilt(self):
        _, packets, _, pkts = self.remux()
        audio = [p for p, _, _ in pkts[3:]]
        flags = [p[0] & 1 for p in packets]          # mode bits = 1, mode i -> blockflag i
        for i, (src, out) in enumerate(zip(packets, audio)):
            self.assertEqual(len(out), len(src) + 1)
            r = Unbits(out)
            self.assertEqual(r.get(1), 0, "packet type")
            mode = r.get(1)
            self.assertEqual(mode, flags[i])
            if mode:
                self.assertEqual(r.get(1), flags[i - 1] if i else 0, "previous window flag")
                self.assertEqual(r.get(1), flags[i + 1] if i + 1 < len(packets) else 0, "next window flag")
            src_bits = Unbits(src)
            src_bits.get(1)
            rest = src_bits.bits[1:]
            self.assertEqual(r.bits[r.pos:r.pos + len(rest)], rest)
            self.assertFalse(any(r.bits[r.pos + len(rest):]))

    def test_granules(self):
        rnd = random.Random(13)
        seq = [0, 1, 1, 0, 0, 1, 0, 1, 1, 1, 0, 0, 1, 0]
        bs = [2048 if m else 256 for m in seq]
        expect = [0]
        for i in range(1, len(bs)):
            expect.append(expect[-1] + bs[i - 1] // 4 + bs[i] // 4)
        samples = expect[-1] - 100           # the header trims the last packet
        packets = [bytes([m]) + bytes(rnd.randrange(256) for _ in range(900)) for m in seq]
        _, packets, pages, pkts = self.remux(packets=packets, samples=samples)
        audio_pages = sorted({pi for _, _, pi in pkts[3:]})
        self.assertGreaterEqual(len(audio_pages), 3)
        for pi in audio_pages:
            last_in_page = [i for i, (_, _, p) in enumerate(pkts[3:]) if p == pi][-1]
            want = samples if pi == len(pages) - 1 else expect[last_in_page]
            self.assertEqual(pages[pi]["granule"], want)
        self.assertTrue(all(p["granule"] == 0 for p in pages[:2]))

    def test_large_stream_spans_pages(self):
        rnd = random.Random(11)
        packets = [bytes([m]) + bytes(rnd.randrange(256) for _ in range(600)) for m in [0, 1] * 30]
        _, packets, pages, pkts = self.remux(packets=packets, samples=30000)
        self.assertTrue(all(p["crc_ok"] for p in pages))
        self.assertGreater(len(pages), 4)
        self.assertEqual(len(pkts), 3 + 60)
        self.assertTrue(all(len(p["segs"]) <= 255 for p in pages))

    def test_single_mode_and_equal_blocksizes(self):
        # one mode -> zero mode bits; equal block sizes -> packets are already standard
        _, packets, _, pkts = self.remux(modes=(0,), packets=[b"\xab\xcd", b"\x12\x34\x56"])
        self.assertEqual(pkts[3][0], bytes([0xab << 1 & 0xff, (0xab >> 7) | (0xcd << 1 & 0xff), 0xcd >> 7]))
        std = [b"\x02\x01", b"\x00\x07"]
        _, _, _, pkts = self.remux(small=9, big=9, packets=std)
        self.assertEqual([p for p, _, _ in pkts[3:]], std)

    def test_falls_back_to_second_list(self):
        # id 4 exists only in the standard list, so the aoTuV attempt fails
        setup_std = wwise_setup(1, (4, 0, 1, 0), (0, 1))
        wem, _ = build_wem(setup=setup_std)
        ogg = wv.wem_to_ogg(wem, library())
        s = parse_setup(read_packets(read_pages(ogg))[2][0], 1)
        self.assertEqual([b["entries"] for b in s["books"]], [4, 4, 9, 4])

    def test_trailing_zero_padding_ignored_and_garbage_rejected(self):
        wem, packets = build_wem(trailing=b"\0\0\0")
        self.assertEqual(len(read_packets(read_pages(wv.wem_to_ogg(wem, library())))), 3 + len(packets))
        wem, _ = build_wem(trailing=b"\0\0\x05")
        with self.assertRaises(wv.WemError):
            wv.wem_to_ogg(wem, library())

    def test_bad_inputs_raise(self):
        lib = library()
        wem, _ = build_wem(book_ids=(0, 1, 2, 9))
        with self.assertRaises(wv.WemError):
            wv.wem_to_ogg(wem, lib)
        wem, _ = build_wem(packets=[b"\x03\x00"], modes=(0, 1))   # mode 1 ok
        wv.wem_to_ogg(wem, lib)
        wem, _ = build_wem(packets=[b"\x01"], modes=(0, 1, 1))     # mode bits 2, mode 1 ok
        wv.wem_to_ogg(wem, lib)
        wem, _ = build_wem(packets=[b"\x03"], modes=(0, 1, 1))     # mode 3 >= 3
        with self.assertRaises(wv.WemError):
            wv.wem_to_ogg(wem, lib)
        pcm = bytearray(build_wem()[0])
        pcm[20:22] = b"\x01\x00"
        with self.assertRaises(wv.WemError):
            wv.wem_to_ogg(bytes(pcm), lib)


def _vgmstream():
    env = os.environ.get("PORT_VGMSTREAM")
    for c in [env, ROOT / "work/tools/vgmstream-r2168-macos-arm64/vgmstream-cli",
              ROOT / "work/scratch-audio/src/build/cli/vgmstream-cli"]:
        if c and Path(c).is_file():
            return Path(c)
    return shutil.which("vgmstream-cli")


def _codebooks_header():
    for c in [ROOT / "work/tools/vgmstream-src-r2168/vgmstream-r2168/src/coding/libs/vorbis_codebooks_wwise.h",
              ROOT / "work/scratch-audio/src/vgmstream-r2168/src/coding/libs/vorbis_codebooks_wwise.h"]:
        if c.is_file():
            return c
    return None


@unittest.skipUnless(os.environ.get("PORT_AUDIO_REAL") == "1", "set PORT_AUDIO_REAL=1 for real-data checks")
class RealDataTests(unittest.TestCase):
    def test_real_files_decode_identically(self):
        from pipeline import audio_convert
        vg, hdr = _vgmstream(), _codebooks_header()
        src = ROOT / "work/merged/Media/StreamingAssets/Audio/GeneratedSoundBanks/PS4"
        if not (vg and hdr and src.is_dir()):
            self.skipTest("vgmstream, codebooks or game audio missing")
        lib = wv.CodebookLibrary.from_vgmstream_header(hdr)
        rnd = random.Random(5)
        files = []
        for lang in ("English(UK)", "Czech"):
            names = sorted(p for p in (src / lang).iterdir() if p.suffix == ".wem")
            files += rnd.sample(names, 4)
        with tempfile.TemporaryDirectory() as td:
            td = Path(td)
            for f in files:
                with self.subTest(f.name):
                    ogg = td / (f.stem + ".ogg")
                    ogg.write_bytes(wv.wem_to_ogg(f.read_bytes(), lib))
                    a, b = td / "a.wav", td / "b.wav"
                    subprocess.run([str(vg), "-i", "-o", str(a), str(f)], check=True, capture_output=True)
                    subprocess.run([str(vg), "-i", "-o", str(b), str(ogg)], check=True, capture_output=True)
                    ok, why = audio_convert.compare_wavs(a.read_bytes(), b.read_bytes())
                    self.assertTrue(ok, why)


if __name__ == "__main__":
    unittest.main()
