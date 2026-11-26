"""Build-time edits of the exported game project (replaces the private local patches).

The game's scripts come out of the user's own copy as decompiled C# (AssetRipper, on their
machine). The port changes some of them: removed Unity APIs, PS4-only calls, the network
transport, a few bugs. Each change is a recipe in pipeline/game_edits/recipes/: a program that
names files, types and members and says what to do with them. Recipes hold no game code: they
locate things by name (and, inside one member, by short identifier patterns) and the code they
insert is the port's own.

    python3 -m pipeline.game_edits --project DIR       apply every recipe to an exported project
    python3 -m pipeline.game_edits --verify            (owner only) compare with the old patches

Operations work on text. A member is found by its dotted name ("Type.Member", nested types
"Outer.Inner.Member", overloads "Type.Member#2" in file order) with a small C# scanner that
skips strings, chars and comments.
"""
from __future__ import annotations

import fnmatch
import os
import re
from dataclasses import dataclass, field
from pathlib import Path


class EditError(Exception):
    pass


# ------------------------------------------------------------------ C# scanning

def _code_mask(src: str) -> list[bool]:
    """True for characters that are code (not inside strings, chars or comments)."""
    mask = [True] * len(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        two = src[i:i + 2]
        if two == "//":
            j = src.find("\n", i)
            j = n if j < 0 else j
        elif two == "/*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
        elif c == '@' and i + 1 < n and src[i + 1] == '"':
            j = i + 2
            while j < n:
                if src[j] == '"':
                    if j + 1 < n and src[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
        elif c in "\"'":
            j = i + 1
            while j < n and src[j] != c:
                j += 2 if src[j] == "\\" else 1
            j += 1
        else:
            i += 1
            continue
        for k in range(i, min(j, n)):
            mask[k] = False
        i = j
    return mask


def _match_brace(src: str, mask: list[bool], open_at: int) -> int:
    depth = 0
    for i in range(open_at, len(src)):
        if not mask[i]:
            continue
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return i
    raise EditError("unbalanced braces")


def _top_level(src: str, mask: list[bool], start: int, end: int):
    """Yield (pos, char) of code characters at brace depth 0 within [start, end)."""
    depth = 0
    for i in range(start, end):
        if not mask[i]:
            continue
        ch = src[i]
        if ch == "{":
            if depth == 0:
                yield i, ch
            depth += 1
        elif ch == "}":
            depth -= 1
        elif depth == 0:
            yield i, ch


def _line_start(src: str, pos: int) -> int:
    return src.rfind("\n", 0, pos) + 1


def _type_body(src: str, mask, start: int, end: int, name: str) -> tuple[int, int]:
    """(open brace, close brace) of the type `name` declared at depth 0 in [start, end)."""
    pat = re.compile(r"\b(class|struct|interface|enum)\s+" + re.escape(name) + r"\b")
    for m in pat.finditer(src, start, end):
        if not mask[m.start()] or _depth_at(src, mask, start, m.start()) != 0:
            continue
        ob = src.find("{", m.end())
        while ob >= 0 and not mask[ob]:
            ob = src.find("{", ob + 1)
        if ob < 0 or ob >= end:
            break
        return ob, _match_brace(src, mask, ob)
    raise EditError(f"type {name} not found")


def _depth_at(src, mask, start, pos) -> int:
    d = 0
    for i in range(start, pos):
        if mask[i]:
            if src[i] == "{":
                d += 1
            elif src[i] == "}":
                d -= 1
    return d


def _members(src: str, mask, ob: int, cb: int, name: str) -> list[tuple[int, int]]:
    """Spans (line start .. end incl. newline) of members called `name` directly in a type body."""
    out = []
    decl_start = ob + 1
    i = ob + 1
    depth = 0
    pat = re.compile(r"(?<![\w.])" + re.escape(name) + r"\s*(<[^<>()]*>)?\s*[({=;]")
    while i < cb:
        if not mask[i]:
            i += 1
            continue
        ch = src[i]
        if ch == "{" and depth == 0:
            close = _match_brace(src, mask, i)
            header = src[decl_start:i]
            if _declares(header, pat):
                out.append((_member_start(src, decl_start), _line_end(src, close)))
            j = close + 1
            # expression-bodied or property initialiser tail
            decl_start = j
            i = j
            continue
        if ch == ";" and depth == 0:
            header = src[decl_start:i + 1]
            if _declares(header, pat):
                out.append((_member_start(src, decl_start), _line_end(src, i)))
            decl_start = i + 1
        i += 1
    return out


def _declares(header: str, pat) -> bool:
    h = re.sub(r"\[[^\]]*\]", " ", header)  # attributes
    h = h.split("=>", 1)[0] if "=>" in h and "(" not in h.split("=>", 1)[0] else h
    m = pat.search(h)
    if not m:
        return False
    before = h[:m.start()].strip()
    # a declaration has a type (or modifiers) before the name, not an operator or a dot
    return bool(before) and not before.endswith((".", "=", "(", ",", "return", "new"))


def _member_start(src: str, decl_start: int) -> int:
    # skip blank lines after the previous member, keep attributes/comments directly above
    i = decl_start
    while i < len(src) and src[i] in " \t\r\n":
        i += 1
    return _line_start(src, i)


def _line_end(src: str, pos: int) -> int:
    j = src.find("\n", pos)
    return len(src) if j < 0 else j + 1


def type_span(src: str, dotted: str) -> tuple[int, int]:
    """(open brace, close brace) of a (nested) type."""
    mask = _code_mask(src)
    start, end = 0, len(src)
    ob = cb = None
    for part in dotted.split("."):
        ob, cb = _type_body(src, mask, start, end, part)
        start, end = ob + 1, cb
    return ob, cb


def member_span(src: str, dotted: str) -> tuple[int, int]:
    """Text span of a member: from its first line to the end of its last line."""
    path, _, idx = dotted.partition("#")
    parts = path.split(".")
    if len(parts) < 2:
        raise EditError(f"member name needs a type: {dotted}")
    mask = _code_mask(src)
    ob, cb = type_span(src, ".".join(parts[:-1]))
    spans = _members(src, mask, ob, cb, parts[-1])
    k = int(idx or 1)
    if len(spans) < k:
        raise EditError(f"member {dotted} not found ({len(spans)} with that name)")
    return spans[k - 1]


# ------------------------------------------------------------------ operations

class Op:
    def apply_text(self, text: str) -> str:
        raise NotImplementedError


def _sub(text: str, pattern: str, repl: str, count: int | None, where: str) -> str:
    rx = re.compile(pattern, re.M)
    n = len(rx.findall(text))
    if n == 0 or (count is not None and n != count):
        raise EditError(f"{where}: pattern {pattern!r} matched {n} times, expected {count or 'at least 1'}")
    return rx.sub(repl, text)


@dataclass
class Rename(Op):
    """Regex substitution over the whole file (must match at least once unless optional)."""
    pattern: str
    repl: str
    count: int | None = None
    optional: bool = False

    def apply_text(self, text):
        if self.optional and not re.search(self.pattern, text, re.M):
            return text
        return _sub(text, self.pattern, self.repl, self.count, "rename")


@dataclass
class InMember(Op):
    """Substitutions inside one member; each (pattern, repl[, count]) must match (count times)."""
    member: str
    subs: list

    def apply_text(self, text):
        s, e = member_span(text, self.member)
        body = text[s:e]
        for sub in self.subs:
            pattern, repl = sub[0], sub[1]
            count = sub[2] if len(sub) > 2 else None
            body = _sub(body, pattern, repl, count, self.member)
        return text[:s] + body + text[e:]


@dataclass
class ReplaceMember(Op):
    member: str
    code: str

    def apply_text(self, text):
        s, e = member_span(text, self.member)
        return text[:s] + self.code + text[e:]


@dataclass
class RemoveMember(Op):
    member: str

    def apply_text(self, text):
        s, e = member_span(text, self.member)
        return text[:s] + text[e:]


@dataclass
class AddMembers(Op):
    """Insert code at the end of a type body (before its closing brace line)."""
    type: str
    code: str

    def apply_text(self, text):
        _ob, cb = type_span(text, self.type)
        at = _line_start(text, cb)
        return text[:at] + self.code + text[at:]


@dataclass
class ReplaceText(Op):
    """Whole-file replacement with the port's own code (a file the port rewrites completely)."""
    code: str

    def apply_text(self, text):
        return self.code


_SHADER_RX = re.compile(r"(m_Shader: \{fileID: )(-?\d+)(, guid: )([0-9a-f]+)(, type: )(\d+)(\})")


@dataclass
class MaterialShaderMap(Op):
    """Point materials at other shaders: {(old fileID, old guid): (new guid, new fileID, new type)}.
    Unity's built-in shaders share one guid and differ by fileID, hence the pair."""
    mapping: dict

    def apply_text(self, text):
        def repl(m):
            new = self.mapping.get((int(m.group(2)), m.group(4)))
            if not new:
                return m.group(0)
            guid, fid, typ = new
            return f"{m.group(1)}{fid}{m.group(3)}{guid}{m.group(5)}{typ}{m.group(7)}"
        return _SHADER_RX.sub(repl, text)


_DOC_RX = re.compile(r"^--- !u!(\d+) &(-?\d+)[^\n]*\n", re.M)
_SCRIPT_RX = re.compile(r"(m_Script: \{fileID: )(-?\d+)(, guid: )(\w+)?(, type: \d+)?(\})")


def _yaml_docs(text: str) -> list[tuple[int, int, str, str]]:
    """(start, end, class id, file id) of each document of a Unity YAML file."""
    ms = list(_DOC_RX.finditer(text))
    return [(m.start(), ms[i + 1].start() if i + 1 < len(ms) else len(text), m.group(1), m.group(2))
            for i, m in enumerate(ms)]


@dataclass
class RemoveScripts(Op):
    """Remove MonoBehaviours whose script guid is in `guids` (scripts the build does not have),
    or, with empty=True, whose script reference is empty; and their m_Component entries."""
    guids: set
    empty: bool = False

    def apply_text(self, text):
        gone = set()
        for s, e, cls, fid in _yaml_docs(text):
            if cls != "114":
                continue
            m = re.search(r"^  m_Script: \{fileID: (-?\d+)(?:, guid: (\w+))?", text[s:e], re.M)
            if m and ((m.group(2) in self.guids) or (self.empty and m.group(1) == "0" and not m.group(2))):
                gone.add(fid)
        if not gone:
            raise EditError("no MonoBehaviour with those scripts")
        out, last = [], 0
        for s, e, cls, fid in _yaml_docs(text):
            if cls == "114" and fid in gone:
                out.append(text[last:s])
                last = e
        out.append(text[last:])
        text = "".join(out)
        return re.sub(r"^  - 114: \{fileID: (-?\d+)\}\n",
                      lambda m: "" if m.group(1) in gone else m.group(0), text, flags=re.M)


@dataclass
class ScriptRemap(Op):
    """Point script references elsewhere: {(old fileID, old guid): (new fileID, new guid)}."""
    mapping: dict

    def apply_text(self, text):
        hit = 0

        def repl(m):
            nonlocal hit
            new = self.mapping.get((int(m.group(2)), m.group(4)))
            if not new:
                return m.group(0)
            hit += 1
            return f"{m.group(1)}{new[0]}{m.group(3)}{new[1]}{m.group(5) or ''}{m.group(6)}"
        out = _SCRIPT_RX.sub(repl, text)
        if not hit:
            raise EditError("no script reference to remap")
        return out


@dataclass
class File:
    """Operations on one file (path relative to the project root; glob allowed)."""
    path: str
    ops: list
    create: bool = False


@dataclass
class Delete:
    """Delete files or folders (relative paths, globs allowed); each entry must match something."""
    paths: list
    meta: bool = True  # also the Unity .meta next to each


@dataclass
class Custom:
    """A step implemented by a function (root: Path) -> list[str] of changed relative paths."""
    name: str
    fn: object


@dataclass
class Recipe:
    name: str
    steps: list
    note: str = ""


def _glob(root: Path, pattern: str) -> list[Path]:
    if any(ch in pattern for ch in "*?["):
        return sorted(p for p in root.glob(pattern))
    p = root / pattern
    return [p] if p.exists() else []


def _remove(p: Path) -> None:
    if p.is_dir():
        import shutil
        shutil.rmtree(p)
    elif p.exists():
        p.unlink()


def apply_recipe(root: Path, recipe: Recipe) -> list[str]:
    """Apply one recipe in place. Returns the relative paths it changed or removed."""
    root = Path(root)
    changed: list[str] = []
    for step in recipe.steps:
        if isinstance(step, Delete):
            for pat in step.paths:
                hits = _glob(root, pat)
                if not hits:
                    raise EditError(f"{recipe.name}: nothing to delete at {pat}")
                for p in hits:
                    changed.append(p.relative_to(root).as_posix())
                    _remove(p)
                    m = p.with_name(p.name + ".meta")
                    if step.meta and m.exists():
                        changed.append(m.relative_to(root).as_posix())
                        m.unlink()
        elif isinstance(step, File):
            hits = [root / step.path] if step.create else _glob(root, step.path)
            if not hits:
                raise EditError(f"{recipe.name}: no file {step.path}")
            for p in hits:
                text = p.read_text(encoding="utf-8-sig") if p.exists() else ""
                had_bom = p.exists() and p.read_bytes().startswith(b"\xef\xbb\xbf")
                new = text
                for op in step.ops:
                    try:
                        new = op.apply_text(new)
                    except EditError as e:
                        raise EditError(f"{recipe.name}: {p.relative_to(root)}: {e}") from None
                if new != text or step.create:
                    p.parent.mkdir(parents=True, exist_ok=True)
                    data = new.encode("utf-8")
                    p.write_bytes((b"\xef\xbb\xbf" if had_bom else b"") + data)
                    changed.append(p.relative_to(root).as_posix())
        elif isinstance(step, Custom):
            changed += step.fn(root)
        else:
            raise EditError(f"{recipe.name}: unknown step {step!r}")
    return changed


def all_recipes() -> list[Recipe]:
    import importlib
    return importlib.import_module("pipeline.game_edits.recipes").ALL


def apply_all(root: Path, log=print) -> list[str]:
    changed: list[str] = []
    for rc in all_recipes():
        got = apply_recipe(root, rc)
        log(f"game edit {rc.name}: {len(got)} paths")
        changed += got
    return changed
