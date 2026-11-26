"""Serialize synthetic Wwise v113 bank objects for tests (the inverse of pipeline.wwise_bank).

Every helper returns the object body (without the HIRC type byte and size); `hirc()` wraps a list
of (type id, body) pairs and `bank()` assembles a whole .bnk file. Only synthetic values are used.
"""
from __future__ import annotations

import struct

INT_PROPS = {14, 15, 58}

T_SOUND, T_ACTION, T_EVENT, T_RANSEQ, T_SWITCH, T_ACTOR, T_BUS, T_LAYER = 2, 3, 4, 5, 6, 7, 8, 9
T_SEGMENT, T_TRACK, T_MSWITCH, T_MRANSEQ, T_ATTEN, T_FXSHARE, T_FXCUSTOM, T_AUXBUS = 10, 11, 12, 13, 14, 18, 19, 20

VORBIS, EXTERNAL, AUDIO_INPUT, METER, GAIN = 0x00040001, 0x00080001, 0x00C80002, 0x00810003, 0x008B0003


def u8(v): return struct.pack("<B", v)
def s8(v): return struct.pack("<b", v)
def u16(v): return struct.pack("<H", v)
def s16(v): return struct.pack("<h", v)
def u32(v): return struct.pack("<I", v)
def s32(v): return struct.pack("<i", v)
def f32(v): return struct.pack("<f", v)
def f64(v): return struct.pack("<d", v)


def var(v: int) -> bytes:
    groups = [v & 0x7F]
    v >>= 7
    while v:
        groups.append(v & 0x7F)
        v >>= 7
    groups.reverse()
    return bytes([g | 0x80 for g in groups[:-1]] + [groups[-1]])


def props(values: dict | None = None) -> bytes:
    values = values or {}
    out = u8(len(values)) + bytes(values)
    for pid, v in values.items():
        out += s32(v) if pid in INT_PROPS else f32(v)
    return out


def ranged(values: dict | None = None) -> bytes:
    values = values or {}
    out = u8(len(values)) + bytes(values)
    for pid, (lo, hi) in values.items():
        out += (s32(lo) + s32(hi)) if pid in INT_PROPS else (f32(lo) + f32(hi))
    return out


def points(pts) -> bytes:
    return b"".join(f32(x) + f32(y) + u32(i) for x, y, i in pts)


def rtpc(rtpc_id, param_id, pts, scaling=0, accum=1, rtype=0, curve_id=0) -> bytes:
    return (u32(rtpc_id) + u8(rtype) + u8(accum) + var(param_id) + u32(curve_id) + u8(scaling)
            + u16(len(pts)) + points(pts))


def rtpcs(items=()) -> bytes:
    items = list(items)
    return u16(len(items)) + b"".join(items)


def states(groups=()) -> bytes:
    out = u32(len(groups))
    for gid, sync, sts in groups:
        out += u32(gid) + u8(sync) + u16(len(sts)) + b"".join(u32(s) + u32(i) for s, i in sts)
    return out


def fx_list(fx=(), bypass=0, override=None) -> bytes:
    out = b"" if override is None else u8(override)
    out += u8(len(fx))
    if fx:
        out += u8(bypass)
        for index, fid, share, rendered in fx:
            out += u8(index) + u32(fid) + u8(share) + u8(rendered)
    return out


def positioning(bits=0xC0, bits3d=1, attenuation=0, path=None) -> bytes:
    out = u8(bits)
    if bits & 1 and bits & 8:
        out += u8(bits3d) + u32(attenuation)
        if not bits3d & 1:
            mode, trans, verts, plist, ranges_ = path
            out += u8(mode) + s32(trans) + u32(len(verts))
            out += b"".join(f32(x) + f32(y) + f32(z) + s32(d) for x, y, z, d in verts)
            out += u32(len(plist)) + b"".join(u32(a) + u32(b) for a, b in plist)
            out += b"".join(f32(x) + f32(y) + f32(z) for x, y, z in ranges_)
    return out


def aux(bits=0, busses=(0, 0, 0, 0)) -> bytes:
    return u8(bits) + (b"".join(u32(b) for b in busses) if bits & 8 else b"")


def advanced(bits=0, vq=1, max_inst=0, below=0, bits2=0) -> bytes:
    return u8(bits) + u8(vq) + u16(max_inst) + u8(below) + u8(bits2)


def node_base(bus=0, parent=0, values=None, ranges=None, pos=None, fx=(), fx_bypass=0, override_fx=0,
              aux_=None, adv=None, state_groups=(), rtpc_items=()) -> bytes:
    return (fx_list(fx, fx_bypass, override_fx) + u8(0) + u32(bus) + u32(parent) + u8(0)
            + props(values) + ranged(ranges) + (pos if pos is not None else positioning())
            + (aux_ if aux_ is not None else aux()) + (adv if adv is not None else advanced())
            + states(state_groups) + rtpcs(rtpc_items))


def source(plugin=VORBIS, sid=0, stream=0, mem=0, bits=0, params=b"") -> bytes:
    out = u32(plugin) + u8(stream) + u32(sid) + u32(mem) + u8(bits)
    if plugin & 0xF == 2:
        out += u32(len(params)) + params
    return out


def children(ids=()) -> bytes:
    return u32(len(ids)) + b"".join(u32(i) for i in ids)


# ---------------------------------------------------------------- objects

def sound(oid, src=None, **nb) -> bytes:
    return u32(oid) + (src if src is not None else source()) + node_base(**nb)


def event(oid, actions=()) -> bytes:
    return u32(oid) + u32(len(actions)) + b"".join(u32(a) for a in actions)


def exceptions(items=()) -> bytes:
    return u32(len(items)) + b"".join(u32(i) + u8(b) for i, b in items)


def action(oid, raw_type, target, values=None, ranges=None, tail=b"", is_bus=0) -> bytes:
    return u32(oid) + u16(raw_type) + u32(target) + u8(is_bus) + props(values) + ranged(ranges) + tail


def play_tail(curve=4, bank=0) -> bytes:
    return u8(curve) + u32(bank)


def stop_tail(curve=4, exc=()) -> bytes:
    return u8(curve) + exceptions(exc)


def pause_tail(curve=4, flags=0, exc=()) -> bytes:
    return u8(curve) + u8(flags) + exceptions(exc)


def set_param_tail(curve=4, bypass=0, meaning=1, base=0.0, lo=0.0, hi=0.0, exc=()) -> bytes:
    return u8(curve) + u8(bypass) + u8(meaning) + f32(base) + f32(lo) + f32(hi) + exceptions(exc)


def group_tail(group, value) -> bytes:
    return u32(group) + u32(value)


def ranseq(oid, kids=(), playlist=(), loop=1, loop_min=0, loop_max=0, trans=0.0, trans_min=0.0, trans_max=0.0,
           avoid=0, trans_mode=0, random_mode=0, mode=0, bits=0, **nb) -> bytes:
    return (u32(oid) + node_base(**nb) + u16(loop) + u16(loop_min) + u16(loop_max) + f32(trans)
            + f32(trans_min) + f32(trans_max) + u16(avoid) + u8(trans_mode) + u8(random_mode) + u8(mode)
            + u8(bits) + children(kids) + u16(len(playlist)) + b"".join(u32(i) + s32(w) for i, w in playlist))


def switch_cntr(oid, group_type=0, group=0, default=0, continuous=0, kids=(), assigns=(), params=(), **nb) -> bytes:
    out = u32(oid) + node_base(**nb) + u8(group_type) + u32(group) + u32(default) + u8(continuous) + children(kids)
    out += u32(len(assigns))
    for sw, nodes in assigns:
        out += u32(sw) + u32(len(nodes)) + b"".join(u32(n) for n in nodes)
    out += u32(len(params))
    for node, b1, b2, fo, fi in params:
        out += u32(node) + u8(b1) + u8(b2) + s32(fo) + s32(fi)
    return out


def actor_mixer(oid, kids=(), **nb) -> bytes:
    return u32(oid) + node_base(**nb) + children(kids)


def layer_cntr(oid, kids=(), layers=(), **nb) -> bytes:
    out = u32(oid) + node_base(**nb) + children(kids) + u32(len(layers))
    for lid, rtpc_items, xrtpc, xtype, assoc in layers:
        out += u32(lid) + rtpcs(rtpc_items) + u32(xrtpc) + u8(xtype) + u32(len(assoc))
        for child, pts in assoc:
            out += u32(child) + u32(len(pts)) + points(pts)
    return out


def bus(oid, parent=0, values=None, bits=0, bits2=0, max_inst=0, channel=0, hdr=2, recovery=1000,
        max_duck=-96.0, ducks=(), fx=(), fx_bypass=0, mixer=0, mixer_share=0, rtpc_items=(), state_groups=()) -> bytes:
    out = u32(oid) + u32(parent) + props(values) + u8(bits) + u8(bits2) + u16(max_inst) + u32(channel) + u8(hdr)
    out += s32(recovery) + f32(max_duck) + u32(len(ducks))
    for b, vol, fo, fi, curve, target in ducks:
        out += u32(b) + f32(vol) + s32(fo) + s32(fi) + u8(curve) + u8(target)
    out += fx_list(fx, fx_bypass) + u32(mixer) + u8(mixer_share) + u8(0) + rtpcs(rtpc_items) + states(state_groups)
    return out


def music_node(kids=(), grid=(0.0, 0.0), tempo=120.0, sig=(4, 4), stingers=(), flags=0, **nb) -> bytes:
    out = u8(flags) + node_base(**nb) + children(kids) + f64(grid[0]) + f64(grid[1]) + f32(tempo)
    out += u8(sig[0]) + u8(sig[1]) + u8(0) + u32(len(stingers))
    for trig, seg, sync, cue, dont, look in stingers:
        out += u32(trig) + u32(seg) + u32(sync) + u32(cue) + s32(dont) + u32(look)
    return out


def fade(t=0, curve=4, off=0) -> bytes:
    return s32(t) + u32(curve) + s32(off)


def transition(src=(-1,), dst=(-1,), src_fade=(0, 4, 0), sync=0, src_cue=0, post_exit=0,
               dst_fade=(0, 4, 0), dst_cue=0, jump=0, entry=0, pre_entry=0, match_cue=0, tobj=None) -> bytes:
    out = u32(len(src)) + b"".join(s32(s) for s in src) + u32(len(dst)) + b"".join(s32(d) for d in dst)
    out += fade(*src_fade) + u32(sync) + u32(src_cue) + u8(post_exit)
    out += fade(*dst_fade) + u32(dst_cue) + u32(jump) + u16(entry) + u8(pre_entry) + u8(match_cue)
    if tobj is None:
        out += u8(0)
    else:
        seg, fin, fout, pre, post = tobj
        out += u8(1) + u32(seg) + fade(*fin) + fade(*fout) + u8(pre) + u8(post)
    return out


def music_segment(oid, duration=1000.0, markers=(), **mn) -> bytes:
    out = u32(oid) + music_node(**mn) + f64(duration) + u32(len(markers))
    for mid, pos, name in markers:
        b = name.encode() if name else b""
        out += u32(mid) + f64(pos) + u32(len(b)) + b
    return out


def music_track(oid, sources=(), clips=(), sub_tracks=1, automation=(), track_type=0, switch=None,
                look_ahead=0, flags=0, **nb) -> bytes:
    out = u32(oid) + u8(flags) + u32(len(sources)) + b"".join(sources) + u32(len(clips))
    for sub, sid, play, begin, end, dur in clips:
        out += u32(sub) + u32(sid) + f64(play) + f64(begin) + f64(end) + f64(dur)
    if clips:
        out += u32(sub_tracks)
    out += u32(len(automation))
    for clip, typ, pts in automation:
        out += u32(clip) + u32(typ) + u32(len(pts)) + points(pts)
    out += node_base(**nb) + u8(track_type)
    if track_type == 3:
        gtype, group, default, assoc = switch
        out += u8(gtype) + u32(group) + u32(default) + u32(len(assoc)) + b"".join(u32(a) for a in assoc)
        out += fade(100, 4, 0) + u32(0) + u32(0) + fade(200, 4, 0)
    out += s32(look_ahead)
    return out


def music_switch(oid, rules=(), continue_playback=0, args=(), tree=(), mode=0, **mn) -> bytes:
    out = u32(oid) + music_node(**mn) + u32(len(rules)) + b"".join(rules) + u8(continue_playback)
    out += u32(len(args)) + b"".join(u32(g) for g, _t in args) + b"".join(u8(t) for _g, t in args)
    data = b"".join(u32(k) + u32(v) + u16(w) + u16(p) for k, v, w, p in tree)
    return out + u32(len(data)) + u8(mode) + data


def tree_branch(key, first, count, weight=50, prob=100):
    return (key, first | (count << 16), weight, prob)


def music_ranseq(oid, rules=(), items=(), **mn) -> bytes:
    """items: pre-order tuples (segment, id, child_count, type, loop, weight, avoid, use_weight, shuffle)."""
    out = u32(oid) + music_node(**mn) + u32(len(rules)) + b"".join(rules) + u32(len(items))
    for seg, iid, nc, typ, loop, weight, avoid, usew, shuf in items:
        out += u32(seg) + u32(iid) + u32(nc) + u32(typ) + s16(loop) + s16(0) + s16(0) + u32(weight)
        out += u16(avoid) + u8(usew) + u8(shuf)
    return out


def attenuation(oid, cone=None, uses=(0, 0, 0, -1, -1, -1, -1), curves=(), rtpc_items=()) -> bytes:
    out = u32(oid) + u8(1 if cone else 0)
    if cone:
        out += b"".join(f32(v) for v in cone)
    out += b"".join(s8(u) for u in uses) + u8(len(curves))
    for scaling, pts in curves:
        out += u8(scaling) + u16(len(pts)) + points(pts)
    return out + rtpcs(rtpc_items)


def fx(oid, plugin, params=b"", media=(), rtpc_items=()) -> bytes:
    out = u32(oid) + u32(plugin) + u32(len(params)) + params + u8(len(media))
    out += b"".join(u8(i) + u32(s) for i, s in media)
    return out + rtpcs(rtpc_items) + u16(0)


def meter_params(attack=0.1, release=1.0, mn=-48.0, mx=0.0, hold=0.0, mode=1, game_param=0) -> bytes:
    return struct.pack("<fffffBI", attack, release, mn, mx, hold, mode, game_param)


# ---------------------------------------------------------------- chunks

def chunk(cid: str, payload: bytes) -> bytes:
    return cid.encode("latin1") + u32(len(payload)) + payload


def hirc(objects) -> bytes:
    out = u32(len(objects))
    for t, body in objects:
        out += u8(t) + u32(len(body)) + body
    return chunk("HIRC", out)


def stmg(state_groups=(), switch_groups=(), game_params=(), threshold=-80.0, voices=256) -> bytes:
    out = f32(threshold) + u16(voices) + u32(len(state_groups))
    for gid, default_ms, trans in state_groups:
        out += u32(gid) + s32(default_ms) + u32(len(trans)) + b"".join(u32(a) + u32(b) + s32(t) for a, b, t in trans)
    out += u32(len(switch_groups))
    for gid, rtpc_id, pts in switch_groups:
        out += u32(gid) + u32(rtpc_id) + u32(len(pts)) + points(pts)
    out += u32(len(game_params))
    for gid, default, ramp, up, down, builtin in game_params:
        out += u32(gid) + f32(default) + u32(ramp) + f32(up) + f32(down) + u8(builtin)
    return chunk("STMG", out)


def bank(bank_id, language=0, objects=(), media=(), extra=(), version=113, plat="PS4") -> bytes:
    """media: [(id, bytes)] -> DIDX + DATA (16-byte aligned like the real banks)."""
    out = chunk("BKHD", u32(version) + u32(bank_id) + u32(language) + u32(0) + u32(1116) + u32(0))
    if media:
        didx, data = b"", b""
        for mid, blob in media:
            data += b"\0" * (-len(data) % 16)
            didx += u32(mid) + u32(len(data)) + u32(len(blob))
            data += blob
        out += chunk("DIDX", didx) + chunk("DATA", data)
    for c in extra:
        out += c
    if objects:
        out += hirc(objects)
    if plat:
        out += chunk("PLAT", u32(len(plat)) + plat.encode())
    return out
