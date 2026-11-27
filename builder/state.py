"""Which builder steps are finished, so a closed or crashed builder resumes instead of starting over."""
from __future__ import annotations

import json
from pathlib import Path

FILE = "builder-state.json"


class State:
    def __init__(self, work: Path):
        self.path = Path(work) / FILE
        try:
            self.data = json.loads(self.path.read_text())
            if not isinstance(self.data, dict):
                self.data = {}
        except (OSError, ValueError):
            self.data = {}

    def done(self, name: str, digest: str | None) -> bool:
        return digest is not None and self.data.get(name) == digest

    def mark(self, name: str, digest: str | None) -> None:
        if digest is None:
            return
        self.data[name] = digest
        self.path.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps(self.data, indent=1))
        tmp.replace(self.path)

    def forget(self, name: str) -> None:
        if self.data.pop(name, None) is not None:
            self.path.write_text(json.dumps(self.data, indent=1))
