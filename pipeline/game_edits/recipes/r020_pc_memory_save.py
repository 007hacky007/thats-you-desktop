"""020-pc-memory-save."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/SonyPS4MemorySave/Sony",
    ]),
]

RECIPE = Recipe("020-pc-memory-save", STEPS)
