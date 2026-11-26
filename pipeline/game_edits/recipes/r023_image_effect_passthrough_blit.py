"""023-image-effect-passthrough-blit."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    # Effects that only draw sometimes must still pass the image on (Unity 6 does not do it for them).
    File("Assets/Scripts/Assembly-CSharp/ExcludedEffectGrab.cs", [InMember("ExcludedEffectGrab.OnRenderImage", [(r"\n\t\}\n$", "\n\t\telse\n\t\t{\n\t\t\tGraphics.Blit(src, dest);\n\t\t}\n\t}\n", 1)])]),
    File("Assets/Scripts/Assembly-CSharp/MenuRenderer.cs", [InMember("MenuRenderer.OnRenderImage", [(r"\n\t\}\n$", "\n\t\telse\n\t\t{\n\t\t\tGraphics.Blit(src, dest);\n\t\t}\n\t}\n", 1)])]),
]

RECIPE = Recipe("023-image-effect-passthrough-blit", STEPS)
