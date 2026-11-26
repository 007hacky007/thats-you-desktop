"""Lossless remux of Wwise Vorbis (.wem) into standard Ogg Vorbis.

Wwise 2015 media is RIFF/WAVE with fmt tag 0xFFFF and a 0x42-byte fmt chunk
whose extra data (from fmt+0x18) describes the Vorbis stream. The stream in the
data chunk is a seek table, a stripped setup packet and audio packets, each
packet preceded by a 2-byte little-endian size ("type 2" headers, no granule).

What Wwise removed and this module puts back:

- identification and comment headers (rebuilt from fmt);
- setup header: codebooks are referenced by 10-bit IDs into an external
  library (aoTuV 6.03 list) in a packed form; floor, residue, mapping and mode
  sections drop the type fields and the time domain section is missing;
- audio packets ("modified"): the packet-type bit and, for long blocks, the
  previous/next window flags are missing. The flags are recovered from the
  neighbouring packets' modes.

The audio bits themselves are copied unchanged, so the result decodes to the
same PCM as the original. See docs/notes/wwise-vorbis.md.
"""
from __future__ import annotations

import re
import struct
import zlib
from pathlib import Path

VENDOR = "PortAudio remux"
LIST_ORDER = ("aotuv603", "standard")
_PAGE_BODY_TARGET = 4096


class WemError(ValueError):
    """The input is not a Wwise Vorbis stream this remuxer understands."""


# ---------------------------------------------------------------- bit I/O

class BitReader:
    """LSB-first bit reader (Vorbis bit order) over a bytes object."""

    __slots__ = ("data", "pos", "end")

    def __init__(self, data: bytes):
        self.data = data
        self.pos = 0
        self.end = len(data) * 8

    def read(self, n: int) -> int:
        if n == 0:
            return 0
        pos = self.pos
        if pos + n > self.end:
            raise WemError("setup data ends early")
        i = pos >> 3
        sh = pos & 7
        v = int.from_bytes(self.data[i:i + ((sh + n + 7) >> 3)], "little") >> sh
        self.pos = pos + n
        return v & ((1 << n) - 1)


class BitWriter:
    """LSB-first bit writer; values may be arbitrarily wide ints."""

    __slots__ = ("out", "acc", "n")

    def __init__(self):
        self.out = bytearray()
        self.acc = 0
        self.n = 0

    def write(self, value: int, nbits: int) -> None:
        if nbits == 0:
            return
        if value < 0 or value >> nbits:
            raise WemError(f"value {value} does not fit in {nbits} bits")
        self.acc |= value << self.n
        self.n += nbits
        if self.n >= 64:
            k = self.n >> 3
            self.out += (self.acc & ((1 << (k * 8)) - 1)).to_bytes(k, "little")
            self.acc >>= k * 8
            self.n -= k * 8

    @property
    def bits(self) -> int:
        return len(self.out) * 8 + self.n

    def getvalue(self) -> bytes:
        k = (self.n + 7) >> 3
        return bytes(self.out) + self.acc.to_bytes(k, "little")


def ilog(v: int) -> int:
    """Vorbis ilog: number of bits needed to hold v (0 for v <= 0)."""
    return v.bit_length() if v > 0 else 0


def lookup1_values(entries: int, dims: int) -> int:
    """Largest r with r**dims <= entries (Vorbis spec 9.2.3)."""
    if dims <= 0:
        raise WemError("codebook with zero dimensions")
    r = int(round(entries ** (1.0 / dims)))
    while r > 0 and r ** dims > entries:
        r -= 1
    while (r + 1) ** dims <= entries:
        r += 1
    return r


# ---------------------------------------------------------------- codebook library

class CodebookLibrary:
    """Packed Wwise codebooks by ID, in one or more ID lists.

    Lists are tried in order (aoTuV 6.03 first) until a setup rebuilds cleanly.
    """

    def __init__(self, lists: dict[str, dict[int, bytes]]):
        order = [n for n in LIST_ORDER if n in lists] + sorted(n for n in lists if n not in LIST_ORDER)
        self.lists = {n: lists[n] for n in order}
        self._unpacked: dict[tuple[str, int], tuple[int, int]] = {}

    @classmethod
    def from_vgmstream_header(cls, path: Path) -> "CodebookLibrary":
        return cls.from_text(Path(path).read_text(encoding="latin-1"))

    @classmethod
    def from_text(cls, text: str) -> "CodebookLibrary":
        arrays: dict[str, bytes] = {}
        for m in re.finditer(r"static\s+const\s+uint8_t\s+(\w+)\s*\[\s*\]\s*=\s*\{([^}]*)\}", text):
            body = re.findall(r"0[xX]([0-9a-fA-F]{1,2})\b", m.group(2))
            arrays[m.group(1)] = bytes(int(h, 16) for h in body)
        lists: dict[str, dict[int, bytes]] = {}
        for m in re.finditer(r"static\s+const\s+vcb_info_t\s+vcb_list_(\w+)\s*\[\s*\]\s*=\s*\{", text):
            end = re.compile(r"\}\s*;").search(text, m.end())
            if not end:
                raise WemError(f"unterminated codebook list {m.group(1)}")
            books: dict[int, bytes] = {}
            entry = r"\{\s*(0[xX][0-9a-fA-F]+|\d+)\s*,\s*(0[xX][0-9a-fA-F]+|\d+)\s*,\s*(\w+)\s*\}"
            for e in re.finditer(entry, text[m.end():end.end()]):
                cid, size, name = int(e.group(1), 0), int(e.group(2), 0), e.group(3)
                if name not in arrays:
                    raise WemError(f"codebook list {m.group(1)} names unknown array {name}")
                if size > len(arrays[name]):
                    raise WemError(f"codebook {m.group(1)}/{cid} size {size} exceeds array {name}")
                books[cid] = arrays[name][:size]
            lists[m.group(1)] = books
        if not lists:
            raise WemError("no codebook lists found")
        return cls(lists)

    def unpacked(self, list_name: str, cid: int) -> tuple[int, int]:
        """Standard Vorbis codebook bits for an ID, as (value, nbits)."""
        key = (list_name, cid)
        hit = self._unpacked.get(key)
        if hit is None:
            packed = self.lists[list_name].get(cid)
            if packed is None:
                raise WemError(f"codebook {cid} not in list {list_name}")
            w = BitWriter()
            unpack_codebook(packed, w)
            hit = (int.from_bytes(w.getvalue(), "little"), w.bits)
            self._unpacked[key] = hit
        return hit


def unpack_codebook(packed: bytes, w: BitWriter) -> None:
    """Expand one packed Wwise codebook into a standard Vorbis codebook.

    Packed differences: 4-bit dimensions (16), 14-bit entries (24), no sync
    pattern, unordered lengths use a 3-bit field width (always 5), 1-bit lookup
    type (4). Raises WemError if the packed size does not match what was read.
    """
    r = BitReader(packed)
    dims = r.read(4)
    entries = r.read(14)
    w.write(0x564342, 24)
    w.write(dims, 16)
    w.write(entries, 24)
    ordered = r.read(1)
    w.write(ordered, 1)
    if ordered:
        length = r.read(5)
        w.write(length, 5)
        cur = 0
        while cur < entries:
            nb = ilog(entries - cur)
            count = r.read(nb)
            w.write(count, nb)
            cur += count
            length += 1
            if length > 33:
                raise WemError("ordered codebook lengths overflow")
        if cur > entries:
            raise WemError("ordered codebook has too many entries")
    else:
        width = r.read(3)
        if not 1 <= width <= 5:
            raise WemError(f"codeword length width {width}")
        sparse = r.read(1)
        w.write(sparse, 1)
        if sparse:
            for _ in range(entries):
                present = r.read(1)
                w.write(present, 1)
                if present:
                    w.write(r.read(width), 5)
        else:
            for _ in range(entries):
                w.write(r.read(width), 5)
    lookup = r.read(1)
    w.write(lookup, 4)
    if lookup == 1:
        w.write(r.read(32), 32)
        w.write(r.read(32), 32)
        value_bits = r.read(4)
        w.write(value_bits, 4)
        w.write(r.read(1), 1)
        nb = value_bits + 1
        for _ in range(lookup1_values(entries, dims)):
            w.write(r.read(nb), nb)
    used = r.pos
    if len(packed) not in ((used + 7) >> 3, (used >> 3) + 1):
        raise WemError(f"codebook size mismatch ({len(packed)} bytes, {used} bits used)")


# ---------------------------------------------------------------- setup header

def rebuild_setup(packet: bytes, channels: int, codebooks: CodebookLibrary, list_name: str) -> tuple[bytes, list[int]]:
    """Wwise setup packet -> standard Vorbis setup header and mode block flags."""
    r = BitReader(packet)
    w = BitWriter()
    w.write(5, 8)
    for c in b"vorbis":
        w.write(c, 8)

    nbooks = r.read(8) + 1
    w.write(nbooks - 1, 8)
    for _ in range(nbooks):
        value, nbits = codebooks.unpacked(list_name, r.read(10))
        w.write(value, nbits)

    w.write(0, 6)      # time domain count - 1
    w.write(0, 16)     # the one (unused) time domain value

    nfloors = r.read(6) + 1
    w.write(nfloors - 1, 6)
    for _ in range(nfloors):
        w.write(1, 16)  # floor type 1, the only one Wwise uses
        parts = r.read(5)
        w.write(parts, 5)
        classes = []
        for _ in range(parts):
            c = r.read(4)
            w.write(c, 4)
            classes.append(c)
        dims = []
        for _ in range(max(classes) + 1 if classes else 0):
            d = r.read(3)
            w.write(d, 3)
            dims.append(d + 1)
            sub = r.read(2)
            w.write(sub, 2)
            if sub:
                master = r.read(8)
                if master >= nbooks:
                    raise WemError("floor masterbook out of range")
                w.write(master, 8)
            for _ in range(1 << sub):
                book = r.read(8)
                if book - 1 >= nbooks:
                    raise WemError("floor subclass book out of range")
                w.write(book, 8)
        w.write(r.read(2), 2)      # multiplier - 1
        rangebits = r.read(4)
        w.write(rangebits, 4)
        values = 2
        for c in classes:
            for _ in range(dims[c]):
                w.write(r.read(rangebits), rangebits)
                values += 1
        if values > 65:
            raise WemError("floor1 has too many values")

    nres = r.read(6) + 1
    w.write(nres - 1, 6)
    for _ in range(nres):
        rtype = r.read(2)
        if rtype > 2:
            raise WemError(f"residue type {rtype}")
        w.write(rtype, 16)
        w.write(r.read(24), 24)    # begin
        w.write(r.read(24), 24)    # end
        w.write(r.read(24), 24)    # partition size - 1
        ncls = r.read(6) + 1
        w.write(ncls - 1, 6)
        classbook = r.read(8)
        if classbook >= nbooks:
            raise WemError("residue classbook out of range")
        w.write(classbook, 8)
        cascade = []
        for _ in range(ncls):
            low = r.read(3)
            w.write(low, 3)
            flag = r.read(1)
            w.write(flag, 1)
            high = 0
            if flag:
                high = r.read(5)
                w.write(high, 5)
            cascade.append(high * 8 + low)
        for bits in cascade:
            for k in range(8):
                if bits & (1 << k):
                    book = r.read(8)
                    if book >= nbooks:
                        raise WemError("residue book out of range")
                    w.write(book, 8)

    nmaps = r.read(6) + 1
    w.write(nmaps - 1, 6)
    chbits = ilog(channels - 1)
    for _ in range(nmaps):
        w.write(0, 16)  # mapping type 0
        submaps = 1
        flag = r.read(1)
        w.write(flag, 1)
        if flag:
            submaps = r.read(4) + 1
            w.write(submaps - 1, 4)
        polar = r.read(1)
        w.write(polar, 1)
        if polar:
            steps = r.read(8) + 1
            w.write(steps - 1, 8)
            for _ in range(steps):
                mag = r.read(chbits)
                ang = r.read(chbits)
                if mag == ang or mag >= channels or ang >= channels:
                    raise WemError("invalid channel coupling")
                w.write(mag, chbits)
                w.write(ang, chbits)
        reserved = r.read(2)
        if reserved:
            raise WemError("mapping reserved bits set")
        w.write(0, 2)
        if submaps > 1:
            for _ in range(channels):
                mux = r.read(4)
                if mux >= submaps:
                    raise WemError("mapping mux out of range")
                w.write(mux, 4)
        for _ in range(submaps):
            w.write(r.read(8), 8)  # unused time config
            fl = r.read(8)
            if fl >= nfloors:
                raise WemError("mapping floor out of range")
            w.write(fl, 8)
            rs = r.read(8)
            if rs >= nres:
                raise WemError("mapping residue out of range")
            w.write(rs, 8)

    nmodes = r.read(6) + 1
    w.write(nmodes - 1, 6)
    blockflags = []
    for _ in range(nmodes):
        bf = r.read(1)
        w.write(bf, 1)
        w.write(0, 16)  # window type
        w.write(0, 16)  # transform type
        m = r.read(8)
        if m >= nmaps:
            raise WemError("mode mapping out of range")
        w.write(m, 8)
        blockflags.append(bf)

    if len(packet) * 8 - r.pos >= 8:
        raise WemError(f"setup has {len(packet) * 8 - r.pos} unread bits")
    w.write(1, 1)  # framing
    return w.getvalue(), blockflags


# ---------------------------------------------------------------- RIFF / wem info

def _chunks(data: bytes):
    off = 12
    n = len(data)
    while off + 8 <= n:
        cid = data[off:off + 4]
        size = struct.unpack_from("<I", data, off + 4)[0]
        yield cid, off + 8, size
        off += 8 + size + (size & 1)


def wem_info(data: bytes) -> dict:
    """RIFF chunk summary of a .wem.

    Keys: channels, rate, samples, cues [(pos, label)], label, codec, layout,
    and for Vorbis "vorbis" (stream offsets, block size exponents) plus
    data_offset, data_size, truncated.
    """
    if len(data) < 12 or data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise WemError("not a little-endian RIFF/WAVE file")
    fmt = None
    data_off = data_size = None
    cue_pos: dict[int, int] = {}
    labels: dict[int, str] = {}
    for cid, off, size in _chunks(data):
        if cid == b"fmt ":
            fmt = (off, size)
        elif cid == b"data":
            data_off, data_size = off, size
        elif cid == b"cue " and size >= 4:
            count = struct.unpack_from("<I", data, off)[0]
            for i in range(min(count, (size - 4) // 24)):
                cue_id, pos = struct.unpack_from("<II", data, off + 4 + 24 * i)
                cue_pos[cue_id] = pos
        elif cid == b"LIST" and size >= 4 and data[off:off + 4] == b"adtl":
            sub = off + 4
            end = min(off + size, len(data))
            while sub + 8 <= end:
                sid = data[sub:sub + 4]
                ssz = struct.unpack_from("<I", data, sub + 4)[0]
                body = data[sub + 8:min(sub + 8 + ssz, end)]
                if sid == b"labl" and len(body) >= 4:
                    text = body[4:].split(b"\0", 1)[0].decode("utf-8", "replace")
                    labels[struct.unpack_from("<I", body)[0]] = text
                sub += 8 + ssz + (ssz & 1)
    if fmt is None:
        raise WemError("no fmt chunk")
    foff, fsize = fmt
    if fsize < 16:
        raise WemError("fmt chunk too small")
    tag, channels, rate = struct.unpack_from("<HHI", data, foff)
    info: dict = {
        "channels": channels,
        "rate": rate,
        "samples": 0,
        "codec": {0xFFFF: "vorbis", 0x0001: "pcm", 0xFFFE: "pcm"}.get(tag, f"0x{tag:04x}"),
        "layout": struct.unpack_from("<I", data, foff + 0x14)[0] if fsize >= 0x18 else 0,
        "data_offset": data_off,
        "data_size": data_size,
        "truncated": data_off is None or data_off + data_size > len(data),
    }
    if tag == 0xFFFF:
        if fsize != 0x42:
            raise WemError(f"Wwise Vorbis fmt size 0x{fsize:x} (only 0x42 supported)")
        samples, loop_start, loop_end = struct.unpack_from("<III", data, foff + 0x18)
        seek_size, audio_off = struct.unpack_from("<II", data, foff + 0x28)
        max_packet = struct.unpack_from("<H", data, foff + 0x30)[0]
        info["samples"] = samples
        info["vorbis"] = {
            "seek_table_size": seek_size,
            "audio_offset": audio_off,
            "loop_start_packet": loop_start,
            "loop_end_packet": loop_end,
            "max_packet": max_packet,
            "blocksize_exps": (data[foff + 0x40], data[foff + 0x41]),
        }
    elif info["codec"] == "pcm" and data_size is not None:
        bits = struct.unpack_from("<H", data, foff + 14)[0]
        if channels and bits:
            info["samples"] = data_size // (channels * bits // 8)
    cues = sorted((pos, labels.get(cid, "")) for cid, pos in cue_pos.items())
    info["cues"] = cues
    label = next((t for p, t in cues if p == 0 and t), "") or next((t for _, t in cues if t), "")
    if not label and labels:
        label = labels[min(labels)]
    info["label"] = label
    return info


# ---------------------------------------------------------------- Ogg

REV8 = bytes(int(f"{i:08b}"[::-1], 2) for i in range(256))


def _rev32(x: int) -> int:
    return int(f"{x:032b}"[::-1], 2)


def ogg_crc(page: bytes) -> int:
    """Ogg page CRC (poly 0x04C11DB7, MSB-first, init 0, no final xor).

    zlib's CRC-32 is the bit-reflected form of the same polynomial, so
    reflecting every input byte and the final register gives the Ogg CRC.
    """
    return _rev32(zlib.crc32(page.translate(REV8), 0xFFFFFFFF) ^ 0xFFFFFFFF)


class OggWriter:
    """Packs packets into Ogg pages for one logical stream."""

    def __init__(self, serial: int):
        self.serial = serial & 0xFFFFFFFF
        self.seq = 0
        self.out = bytearray()
        self._segs: list[int] = []
        self._body: list[bytes] = []
        self._granule = -1
        self._continued = False

    def _emit(self, eos: bool = False) -> None:
        flags = (1 if self._continued else 0) | (2 if self.seq == 0 else 0) | (4 if eos else 0)
        header = struct.pack("<4sBBqIIIB", b"OggS", 0, flags, self._granule, self.serial, self.seq, 0,
                             len(self._segs)) + bytes(self._segs)
        page = header + b"".join(self._body)
        crc = ogg_crc(page)
        self.out += page[:22] + struct.pack("<I", crc) + page[26:]
        self.seq += 1
        self._segs = []
        self._body = []
        self._granule = -1

    def packet(self, data: bytes, granule: int, flush: bool = False, eos: bool = False) -> None:
        n = len(data)
        lacing = [255] * (n // 255) + [n % 255]
        pos = 0
        for i, lv in enumerate(lacing):
            if len(self._segs) == 255:
                self._emit()
                self._continued = True
            self._segs.append(lv)
            self._body.append(data[pos:pos + lv])
            pos += lv
            if i == len(lacing) - 1:
                self._continued = False
                self._granule = granule
        if eos:
            self._emit(eos=True)
        elif flush or sum(map(len, self._body)) >= _PAGE_BODY_TARGET:
            self._emit()

    def getvalue(self) -> bytes:
        if self._segs:
            self._emit()
        return bytes(self.out)


def identification_header(channels: int, rate: int, small_exp: int, big_exp: int) -> bytes:
    return (b"\x01vorbis" + struct.pack("<IBIiiiB", 0, channels, rate, 0, 0, 0, (big_exp << 4) | small_exp)
            + b"\x01")


def comment_header(comments: list[str]) -> bytes:
    vendor = VENDOR.encode()
    out = bytearray(b"\x03vorbis")
    out += struct.pack("<I", len(vendor)) + vendor + struct.pack("<I", len(comments))
    for c in comments:
        b = c.encode()
        out += struct.pack("<I", len(b)) + b
    out += b"\x01"
    return bytes(out)


# ---------------------------------------------------------------- remux

_SETUP_CACHE: dict[tuple, tuple[bytes, list[int]]] = {}


def _setup_for(packet: bytes, channels: int, codebooks: CodebookLibrary) -> tuple[bytes, list[int]]:
    key = (id(codebooks), channels, packet)
    hit = _SETUP_CACHE.get(key)
    if hit is not None:
        return hit
    errors = []
    for name in codebooks.lists:
        try:
            hit = rebuild_setup(packet, channels, codebooks, name)
            break
        except WemError as e:
            errors.append(f"{name}: {e}")
    else:
        raise WemError("setup rebuild failed (" + "; ".join(errors) + ")")
    if len(_SETUP_CACHE) > 4096:
        _SETUP_CACHE.clear()
    _SETUP_CACHE[key] = hit
    return hit


def wem_to_ogg(data: bytes, codebooks: CodebookLibrary, serial: int = 1, *, wwise_id: int | None = None) -> bytes:
    """Remux a Wwise Vorbis .wem into an Ogg Vorbis file. Raises WemError."""
    info = wem_info(data)
    v = info.get("vorbis")
    if v is None:
        raise WemError(f"codec {info['codec']} is not Wwise Vorbis")
    if info["truncated"]:
        raise WemError("data chunk is truncated (prefetch media?)")
    channels, rate, samples = info["channels"], info["rate"], info["samples"]
    if not 1 <= channels <= 255 or rate <= 0:
        raise WemError(f"bad channels/rate {channels}/{rate}")
    small, big = v["blocksize_exps"]
    if not 6 <= small <= big <= 13:
        raise WemError(f"bad block sizes 2^{small}/2^{big}")
    d0, dend = info["data_offset"], info["data_offset"] + info["data_size"]

    setup_at = d0 + v["seek_table_size"]
    if setup_at + 2 > dend:
        raise WemError("setup offset outside data")
    setup_size = struct.unpack_from("<H", data, setup_at)[0]
    if setup_size == 0 or setup_at + 2 + setup_size > dend:
        raise WemError("bad setup packet size")
    setup, blockflags = _setup_for(data[setup_at + 2:setup_at + 2 + setup_size], channels, codebooks)
    nmodes = len(blockflags)
    mode_bits = ilog(nmodes - 1)
    mode_mask = (1 << mode_bits) - 1
    modified = small != big

    # split audio packets
    pos = d0 + v["audio_offset"]
    if pos < setup_at + 2 + setup_size or pos > dend:
        raise WemError("audio offset outside data")
    spans = []
    while pos + 2 <= dend:
        size = struct.unpack_from("<H", data, pos)[0]
        if size == 0:
            if any(data[pos:dend]):
                raise WemError(f"zero-size packet at 0x{pos:x}")
            break
        if pos + 2 + size > dend:
            raise WemError(f"packet at 0x{pos:x} runs past the data chunk")
        spans.append((pos + 2, size))
        pos += 2 + size
    if not spans:
        raise WemError("no audio packets")

    modes = []
    for start, _ in spans:
        b = data[start]
        m = (b & mode_mask) if modified else ((b >> 1) & mode_mask)
        if m >= nmodes:
            raise WemError(f"packet mode {m} >= {nmodes}")
        if not modified and b & 1:
            raise WemError("audio packet with header type bit set")
        modes.append(m)

    ogg = OggWriter(serial)
    ogg.packet(identification_header(channels, rate, small, big), 0, flush=True)
    comments = [] if wwise_id is None else [f"WWISE_ID={wwise_id}"]
    ogg.packet(comment_header(comments), 0)
    ogg.packet(setup, 0, flush=True)

    sizes = (1 << small, 1 << big)
    last = len(spans) - 1
    granule = 0
    prev_bs = None
    prev_flag = 0
    for i, (start, size) in enumerate(spans):
        flag = blockflags[modes[i]]
        if modified:
            pv = int.from_bytes(data[start:start + size], "little")
            head = (pv & mode_mask) << 1
            rest = pv >> mode_bits
            if flag:
                next_flag = blockflags[modes[i + 1]] if i < last else 0
                out = head | ((prev_flag | (next_flag << 1)) << (1 + mode_bits)) | (rest << (3 + mode_bits))
            else:
                out = head | (rest << (1 + mode_bits))
            pkt = out.to_bytes(size + 1, "little")
        else:
            pkt = data[start:start + size]
        prev_flag = flag
        bs = sizes[flag]
        if prev_bs is not None:
            granule += prev_bs // 4 + bs // 4
        prev_bs = bs
        if i == last:
            ogg.packet(pkt, samples, eos=True)
        else:
            ogg.packet(pkt, granule)
    return ogg.getvalue()


def main(argv=None) -> int:
    import argparse
    ap = argparse.ArgumentParser(description="Remux one Wwise Vorbis .wem to Ogg Vorbis")
    ap.add_argument("wem", type=Path)
    ap.add_argument("--codebooks", type=Path, required=True)
    ap.add_argument("-o", "--output", type=Path, required=True)
    a = ap.parse_args(argv)
    lib = CodebookLibrary.from_vgmstream_header(a.codebooks)
    stem = a.wem.stem
    a.output.write_bytes(wem_to_ogg(a.wem.read_bytes(), lib, wwise_id=int(stem) if stem.isdigit() else None))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
