"""012-misc-removed-unity-apis."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/NavMeshAgentSetDestination.cs", [
        Lines(2, 0, "e3b0c44298fc1c14", 'using UnityEngine.AI;\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/HutongGames/PlayMaker/Actions/TakeScreenshot.cs", [
        Lines(40, 1, "08f64201b9154dfc", '\t\t\tScreenCapture.CaptureScreenshot(path);\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PlayMakerUtils.cs", [
        Lines(311, 5, "d8196b5cfd1417d4", ""),
        Lines(278, 1, "cb4fbea7507a1c8d", '\t\tif (!type2.Equals(type) && (!type.Equals(typeof(AudioClip)) || !type2.Equals(typeof(UnityEngine.Object))))\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/ScreenShot.cs", [
        Lines(19, 1, "91383136b1fd4431", '\t\tScreenCapture.CaptureScreenshot(fileName);\n'),
    ]),
]

RECIPE = Recipe("012-misc-removed-unity-apis", STEPS)
