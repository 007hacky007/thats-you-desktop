"""007-remove-legacy-gui-element-code."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

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
    File("Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/ComponentAction.cs", [
        Lines(51, 16, "90cbd581af9520ce", ""),
    ]),
    File("Assets/Scripts/PlayMaker/PlayMakerFSM.cs", [
        Lines(255, 2, "cbc279af3967f54e", ""),
        Lines(37, 4, "d24f49f024be1ac0", ""),
    ]),
    File("Assets/Scripts/PlayMaker/PlayMakerGUI.cs", [
        Lines(273, 0, "e3b0c44298fc1c14", (
            '\t\tif (Camera.main.transform.InverseTransformPoint(fsm.transform.position).z <= 0f)\n'
            '\t\t{\n'
            '\t\t\treturn;\n'
            '\t\t}\n'
            '\t\tvector = Camera.main.WorldToScreenPoint(fsm.transform.position);\n'
            '\t\tvector.x -= vector2.x * 0.5f;\n'
        )),
        Lines(270, 2, "7b04e4a19aeccc35", ""),
        Lines(248, 19, "618d07243f0c9471", (
            '\t\t\tfloat num = Vector3.Distance(Camera.main.transform.position, fsm.transform.position);\n'
            '\t\t\tif (num > maxLabelDistance)\n'
        )),
        Lines(246, 1, "89383d45fc60c61d", (
            '\t\t// GUIText/GUITexture were removed from Unity; labels always use world-space positioning (as later PlayMaker versions do).\n'
            '\t\tif (filterLabelsWithDistance)\n'
        )),
    ]),
]

RECIPE = Recipe("007-remove-legacy-gui-element-code", STEPS)
