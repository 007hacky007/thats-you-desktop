"""011-ngui-removed-apis."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/NGUITools.cs", [
        Lines(211, 1, "9dfcc12bfa171265", '\t\t\ttextEditor.text = value;\n'),
        Lines(206, 1, "31303e55a4258e6b", '\t\t\treturn textEditor.text;\n'),
        Lines(196, 1, "19785f3268496ece", '\t\t\treturn true;\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/UIInput.cs", [
        Lines(628, 1, "6212b8e48fe6ec0b", '\t\tbool flag = ((platform != RuntimePlatform.OSXEditor && platform != RuntimePlatform.OSXPlayer) ? ((ev.modifiers & EventModifiers.Control) != 0) : ((ev.modifiers & EventModifiers.Command) != 0));\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/UIPanel.cs", [
        Lines(763, 1, "225de04a4ad45aaa", '\t\tmHalfPixelOffset = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.XBOX360 || Application.platform == RuntimePlatform.WindowsEditor;\n'),
        Lines(501, 1, "d0314d0aa23499cc", '\t\t\treturn (a.GetEntityId() >= b.GetEntityId()) ? 1 : (-1);\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/UIWidget.cs", [
        Lines(768, 1, "b2fbd560e5bf08d2", '\t\treturn (material.GetEntityId() >= material2.GetEntityId()) ? 1 : (-1);\n'),
    ]),
]

RECIPE = Recipe("011-ngui-removed-apis", STEPS)
