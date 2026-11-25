"""Run the pinned Unity editor in batch mode."""
from __future__ import annotations

import subprocess
from pathlib import Path


def run_batch(editor: Path, project: Path, args: list[str], log: Path) -> int:
    argv = [str(editor), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
            *args, "-logFile", str(log)]
    return subprocess.run(argv).returncode
