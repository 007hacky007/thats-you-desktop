"""Parse a Unity editor log into grouped, categorized C# compile errors."""
from __future__ import annotations

import json
import re
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path

ERR = re.compile(r"^(?P<file>[^\r\n()]+\.cs)\((?P<line>\d+),(?P<col>\d+)\): error (?P<code>CS\d+): (?P<msg>.*)$")
THIRD_PARTY = {"PlayMaker", "AmplifyColor", "AmplifyOcclusion", "ConditionalExpression"}
PS4_WORDS = re.compile(r"\bPS4\b|'PS4|\bSony\b|'Sony|\bSce[A-Z]|UnityEngine\.PS4")


@dataclass
class Group:
    code: str
    assembly: str
    file: str
    category: str
    count: int = 0
    examples: list[str] = field(default_factory=list)


@dataclass
class Report:
    groups: dict[tuple[str, str, str], Group]

    @property
    def total(self) -> int:
        return sum(g.count for g in self.groups.values())

    def by_category(self) -> Counter:
        c = Counter()
        for g in self.groups.values():
            c[g.category] += g.count
        return c

    def markdown(self, top: int = 40) -> str:
        if not self.total:
            return "# Compile report\n\nNo compile errors.\n"
        lines = ["# Compile report", "", f"Total errors: {self.total} in {len(self.groups)} groups", "",
                 "| Category | Errors |", "|---|---|"]
        lines += [f"| {k} | {v} |" for k, v in self.by_category().most_common()]
        lines += ["", f"## Top {top} groups", "", "| Count | Code | Category | File | Example |", "|---|---|---|---|---|"]
        for g in sorted(self.groups.values(), key=lambda g: -g.count)[:top]:
            ex = g.examples[0].replace("|", "\\|")[:160]
            lines.append(f"| {g.count} | {g.code} | {g.category} | {g.file} | {ex} |")
        return "\n".join(lines) + "\n"

    def to_json(self) -> str:
        return json.dumps({"total": self.total, "by_category": dict(self.by_category()),
                           "groups": [g.__dict__ for g in sorted(self.groups.values(), key=lambda g: -g.count)]},
                          indent=1)


def _assembly(path: str) -> str:
    parts = path.replace("\\", "/").split("/")
    if "Scripts" in parts and parts.index("Scripts") + 1 < len(parts):
        return parts[parts.index("Scripts") + 1]
    return parts[1] if len(parts) > 2 else "Assembly-CSharp"


def _category(assembly: str, path: str, code: str, msg: str) -> str:
    if assembly == "Assembly-UnityScript":
        return "unityscript"
    if assembly in THIRD_PARTY or "NGUI" in path:
        return "third_party"
    if PS4_WORDS.search(msg) or "PS4" in Path(path).stem:
        return "ps4_api"
    if code == "CS0619" or "has been removed" in msg or "obsolete" in msg.lower():
        return "removed_unity_api"
    return "other"


def parse(log: Path) -> Report:
    groups: dict[tuple[str, str, str], Group] = {}
    seen = set()
    for raw in Path(log).read_text(errors="replace").splitlines():
        m = ERR.match(raw.strip())
        if not m:
            continue
        key_line = (m["file"], m["line"], m["col"], m["code"], m["msg"])
        if key_line in seen:
            continue
        seen.add(key_line)
        asm = _assembly(m["file"])
        key = (m["code"], asm, m["file"])
        g = groups.get(key)
        if g is None:
            g = groups[key] = Group(m["code"], asm, m["file"], _category(asm, m["file"], m["code"], m["msg"]))
        g.count += 1
        if len(g.examples) < 3:
            g.examples.append(m["msg"])
    return Report(groups)


def write(report: Report, out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "compile.md").write_text(report.markdown())
    (out_dir / "compile.json").write_text(report.to_json())
