"""005-fix-decompiler-artifacts."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/ClientServerContentSend.cs", [
        Lines(80, 1, "e8622ad198aa1d2d", '\t\t\t\tSendQueue<string>.Priority priority = SendQueue<string>.Priority.Default;\n'),
        Lines(37, 1, "e8622ad198aa1d2d", '\t\t\t\tSendQueue<string>.Priority priority = SendQueue<string>.Priority.Default;\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/GameAudioManager.cs", [
        Lines(35, 1, "88d927c110a89d28", ""),
        Lines(8, 1, "88d927c110a89d28", ""),
    ]),
    File("Assets/Scripts/Assembly-CSharp/OnlineContentSend.cs", [
        Lines(154, 1, "e0722136b3431666", '\t\tSendQueue<string>.Priority priority = SendQueue<string>.Priority.Default;\n'),
        Lines(128, 1, "e8622ad198aa1d2d", '\t\t\t\tSendQueue<string>.Priority priority = SendQueue<string>.Priority.Default;\n'),
        Lines(88, 1, "e8622ad198aa1d2d", '\t\t\t\tSendQueue<string>.Priority priority = SendQueue<string>.Priority.Default;\n'),
        Lines(40, 1, "e8622ad198aa1d2d", '\t\t\t\tSendQueue<string>.Priority priority = SendQueue<string>.Priority.Default;\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/ScenarioTransitionerAsyncLoads.cs", [
        Lines(1616, 1, "f4df52d5faf53ccd", '\t\tthis.PauseAllCoroutines(paused);\n'),
        Lines(1614, 1, "a50ebb9b6c37b130", '\tvoid IScenarioTransitionerAsyncLoads.PauseAllCoroutines(bool paused)\n'),
        Lines(128, 1, "88d927c110a89d28", ""),
    ]),
]

RECIPE = Recipe("005-fix-decompiler-artifacts", STEPS)
