"""007-remove-legacy-gui-element-code."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/DebugInfo.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/GUIElementHitTest.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/SetGUIText.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/SetGUITexture.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/SetGUITextureAlpha.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/SetGUITextureColor.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/SetGUITexturePixelInsetPosition.cs",
        "Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/TouchGUIEvent.cs",
    ]),
]

RECIPE = Recipe("007-remove-legacy-gui-element-code", STEPS)
