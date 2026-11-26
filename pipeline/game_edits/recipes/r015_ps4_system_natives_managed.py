"""015-ps4-system-natives-managed."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PS4AppContent.cs",
        "Assets/Scripts/Assembly-CSharp/PS4EventProcessor.cs",
        "Assets/Scripts/Assembly-CSharp/PluginDLLInterface.cs",
        "Assets/Scripts/Assembly-CSharp/SystemCache.cs",
        "Assets/Scripts/LowLevelTTY/Wish",
    ]),
]

RECIPE = Recipe("015-ps4-system-natives-managed", STEPS)
