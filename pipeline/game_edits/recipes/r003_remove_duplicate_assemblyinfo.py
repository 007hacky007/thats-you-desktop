"""003-remove-duplicate-assemblyinfo."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Plugins/Assembly-CSharp-firstpass/Properties",
        "Assets/Scripts/AmplifyColor",
        "Assets/Scripts/AmplifyOcclusion",
        "Assets/Scripts/AnimationEventDispatcher/Properties",
        "Assets/Scripts/Assembly-UnityScript/Properties",
        "Assets/Scripts/ConditionalExpression/Properties",
        "Assets/Scripts/LowLevelTTY/Properties",
        "Assets/Scripts/PlayMaker/Properties",
        "Assets/Scripts/SceneAnimation/Properties",
        "Assets/Scripts/SonyPS4MemorySave/Properties",
    ]),
]

RECIPE = Recipe("003-remove-duplicate-assemblyinfo", STEPS)
