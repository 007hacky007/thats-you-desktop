"""018-ps4-jpeg-managed."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PS4JpegDec.cs",
        "Assets/Scripts/Assembly-CSharp/PS4JpegEnc.cs",
    ]),
]

RECIPE = Recipe("018-ps4-jpeg-managed", STEPS)
