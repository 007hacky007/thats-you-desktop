"""025-builtin-shader-remap."""
from pipeline.game_edits import *  # noqa: F401,F403

# (old fileID, old guid) -> (new guid, new fileID, new type): the export's placeholder copies of
# Unity built-in shaders (deleted above), pointed back at the built-in shaders themselves.
SHADERS = {
    (4800000, "01629f74d1266614dae1362e16859627"): ("0000000000000000f000000000000000", 46, 0),
    (4800000, "1b3c21fe2eac9cc49883d514bcc0910b"): ("0000000000000000f000000000000000", 14001, 0),
    (4800000, "359c147afc62a0246b803a03e6ae37e8"): ("0000000000000000f000000000000000", 10751, 0),
    (4800000, "3dd5f16cd56ad724081ee5b1f32e6369"): ("0000000000000000f000000000000000", 10752, 0),
    (4800000, "435aaa03409a19f4bb358f7f29293578"): ("0000000000000000f000000000000000", 51, 0),
    (4800000, "6e9cb3ad7f33c694a90875ba53b6b26e"): ("0000000000000000f000000000000000", 203, 0),
    (4800000, "7694cfc0cabb91b4fb05122719075e71"): ("0000000000000000f000000000000000", 103, 0),
    (4800000, "9157dbf74bfb1f44799770f5491f32b3"): ("0000000000000000f000000000000000", 202, 0),
    (4800000, "b1280a655bdfcd84f9fb255050f87dba"): ("0000000000000000f000000000000000", 10755, 0),
    (4800000, "c2a550b4b5dc5b146b5ff9d12cc7eac6"): ("0000000000000000f000000000000000", 14000, 0),
    (4800000, "c69404448424cd9449b91d9403674b31"): ("0000000000000000f000000000000000", 45, 0),
    (4800000, "f25e60a3145512646884a3321d88d98d"): ("0000000000000000f000000000000000", 30, 0),
    (4800000, "f62c7735e5f33a140ac1df94a9d006d2"): ("0000000000000000f000000000000000", 10750, 0),
}

STEPS = [
    Delete([
        "Assets/Shader/AddPass.shader",
        "Assets/Shader/AfsBillboardTree v4.shader",
        "Assets/Shader/AfsFoliageShader Terrain v4.shader",
        "Assets/Shader/AfsGrassShader Terrain v4.shader",
        "Assets/Shader/Alpha-Diffuse.shader",
        "Assets/Shader/Alpha-VertexLit.shader",
        "Assets/Shader/AlphaTest-Diffuse.shader",
        "Assets/Shader/AlphaTest-VertexLit.shader",
        "Assets/Shader/FirstPass.shader",
        "Assets/Shader/Normal-Glossy.shader",
        "Assets/Shader/Particle AddSmooth.shader",
        "Assets/Shader/Particle Alpha Blend.shader",
        "Assets/Shader/Shader.shader",
        "Assets/Shader/Skybox-Cubed.shader",
        "Assets/Shader/Skybox-Procedural.shader",
        "Assets/Shader/Specular-AddPass.shader",
        "Assets/Shader/Specular-Base.shader",
        "Assets/Shader/Specular-FirstPass.shader",
        "Assets/Shader/SpeedTree.shader",
        "Assets/Shader/SpeedTreeBillboard.shader",
        "Assets/Shader/Standard-AddPass.shader",
        "Assets/Shader/Standard-Base.shader",
        "Assets/Shader/Standard-FirstPass.shader",
        "Assets/Shader/StandardSpecular.shader",
        "Assets/Shader/TreeSoftOcclusionBarkRendertex.shader",
        "Assets/Shader/TreeSoftOcclusionLeavesRendertex.shader",
        "Assets/Shader/Unlit-Alpha.shader",
        "Assets/Shader/Unlit-AlphaTest.shader",
        "Assets/Shader/Unlit-Color.shader",
        "Assets/Shader/Unlit-Normal.shader",
        "Assets/Shader/WavingGrassBillboard.shader",
    ]),
    File("Assets/**/*.mat", [MaterialShaderMap(SHADERS)]),
]

RECIPE = Recipe("025-builtin-shader-remap", STEPS)
