"""Install the repo's git hooks and set up the local-patches repository."""
import subprocess
from pathlib import Path

from pipeline.config import REPO_ROOT
from pipeline.patches import PATCH_DIR

README = """# local-patches - DO NOT PUBLISH

These patches are made against decompiled game code and contain it.
This folder is its own git repository and is ignored by the main repo.
Before anything is published, every patch here must be rewritten as a
build-time compiled-assembly edit (Plan 7), and this folder stays private.
Each NNN-name.patch has a NNN-name.md saying what it changes and why.
"""


def main() -> None:
    subprocess.run(["git", "config", "core.hooksPath", "hooks"], cwd=REPO_ROOT, check=True)
    PATCH_DIR.mkdir(exist_ok=True)
    if not (PATCH_DIR / ".git").exists():
        subprocess.run(["git", "init", "-q", "-b", "master"], cwd=PATCH_DIR, check=True)
        for k, v in [("user.name", "local"), ("user.email", "local@localhost"), ("commit.gpgsign", "false"),
                     ("remote.pushDefault", "none")]:
            subprocess.run(["git", "config", k, v], cwd=PATCH_DIR, check=True)
        (PATCH_DIR / "README.md").write_text(README)
        subprocess.run(["git", "add", "README.md"], cwd=PATCH_DIR, check=True)
        subprocess.run(["git", "commit", "-qm", "Start local patches"], cwd=PATCH_DIR, check=True)
    print("hooks installed; set the private page host with: git config iwyk.privateUrl <host>")


if __name__ == "__main__":
    main()
