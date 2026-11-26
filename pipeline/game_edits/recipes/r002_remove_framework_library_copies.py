"""002-remove-framework-library-copies."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Mono.Posix",
        "Assets/Scripts/System.Configuration",
        "Assets/Scripts/System.Runtime.CompilerServices.Unsafe",
        "Assets/Scripts/System.Runtime.InteropServices",
        "Assets/Scripts/System.Security",
    ]),
]

RECIPE = Recipe("002-remove-framework-library-copies", STEPS)
