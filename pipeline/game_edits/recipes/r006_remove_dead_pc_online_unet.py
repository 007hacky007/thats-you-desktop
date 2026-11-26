"""006-remove-dead-pc-online-unet."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PCOnlineManager.cs",
        "Assets/Scripts/Assembly-CSharp/PCOnlineMessage.cs",
    ]),
]

RECIPE = Recipe("006-remove-dead-pc-online-unet", STEPS)
