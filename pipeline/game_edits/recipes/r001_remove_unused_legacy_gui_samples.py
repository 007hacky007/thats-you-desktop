"""001-remove-unused-legacy-gui-samples."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Plugins/Assembly-CSharp-firstpass/ForcedReset.cs",
        "Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/Utility/FPSCounter.cs",
        "Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/Utility/SimpleActivatorMenu.cs",
    ]),
]

RECIPE = Recipe("001-remove-unused-legacy-gui-samples", STEPS)
