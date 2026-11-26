"""028-lightshafts-float-interpolation-target."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    # Keep the float interpolation target: the integer format chosen with DX11 support breaks on Metal/D3D11 here.
    File("Assets/Scripts/Assembly-CSharp/LightShafts.cs", [
        InMember("LightShafts.CheckMinRequirements", [(
            r"\t\tif \(m_DX11Support\)\n\t\t\{\n\t\t\tRTformatInterp2F = RenderTextureFormat\.RGInt;\n\t\t\}\n", "", 1)]),
    ]),
]

RECIPE = Recipe("028-lightshafts-float-interpolation-target", STEPS)
