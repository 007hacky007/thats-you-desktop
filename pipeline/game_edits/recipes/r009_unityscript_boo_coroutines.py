"""009-unityscript-boo-coroutines."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    File("Assets/Scripts/Assembly-UnityScript/LightCake.cs", [
        Lines(138, 1, "5fc4b67d6778c2db", (
            '\t\tyield return new WaitForSeconds(1f / fps);\n'
            '\t\tAnimateTexture();\n'
        )),
        Lines(136, 0, "e3b0c44298fc1c14", '\t// Converted from a Boo GenericGenerator state machine to a C# iterator.\n'),
        Lines(132, 1, "b4ef3fb4dbe3c7e3", '\t\t\tStartCoroutine(Loop());\n'),
        Lines(119, 1, "0f4142be2a60c798", (
            '\t\tif (CookieBatch.Length != 0)\n'
            '\t\t{\n'
            '\t\t\tyield return typeof(WaitForEndOfFrame);\n'
            '\t\t\tAnimateTexture();\n'
            '\t\t}\n'
        )),
        Lines(117, 0, "e3b0c44298fc1c14", (
            '\t// Converted from a Boo GenericGenerator state machine to a C# iterator.\n'
            '\t// The original yields the Type object typeof(WaitForEndOfFrame) (not an instance),\n'
            '\t// which Unity treats as a plain one-frame wait; kept as-is to preserve timing.\n'
        )),
        Lines(10, 96, "9e8ef69e51993656", ""),
        Lines(3, 2, "1316126993d8ac36", ""),
    ]),
]

RECIPE = Recipe("009-unityscript-boo-coroutines", STEPS)
