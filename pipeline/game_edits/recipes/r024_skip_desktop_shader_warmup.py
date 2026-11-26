"""024-skip-desktop-shader-warmup."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/ComposeScene.cs", [
        InMember("ComposeScene.Flow", [(
            r"(\t\tShader\.WarmupAllShaders\(\);\n)",
            "#if UNITY_PS4\n\t\t// Port: on Metal/D3D this compiles every variant synchronously and freezes first "
            "launch; desktop compiles lazily.\n\\1#endif\n", 1)]),
    ]),
]

RECIPE = Recipe("024-skip-desktop-shader-warmup", STEPS)
