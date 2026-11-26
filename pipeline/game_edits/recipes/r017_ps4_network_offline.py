"""017-ps4-network-offline."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PS4SceNetCtl.cs",
        "Assets/Scripts/Assembly-CSharp/Wish/Core/PS4SceNetCtlAp.cs",
        "Assets/Scripts/Assembly-CSharp/Wish/Core/otg_peeper.cs",
    ]),
]

RECIPE = Recipe("017-ps4-network-offline", STEPS)
