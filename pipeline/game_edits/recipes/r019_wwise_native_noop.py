"""019-wwise-native-noop."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/AkSoundEnginePINVOKE.cs",
    ]),
]

RECIPE = Recipe("019-wwise-native-noop", STEPS)
