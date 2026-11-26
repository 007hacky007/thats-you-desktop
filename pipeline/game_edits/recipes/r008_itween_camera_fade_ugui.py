"""008-itween-camera-fade-ugui."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/iTween.cs", [
        Lines(3283, 6, "c05a2fd06a156e4c", '\t\treturn CameraFadeCreate(CameraTexture(Color.black), Defaults.cameraFadeDepth);\n'),
        Lines(3269, 6, "7b6c0cf3ed5c348d", '\t\treturn CameraFadeCreate(texture, Defaults.cameraFadeDepth);\n'),
        Lines(3255, 6, "8de06288c6744d79", '\t\treturn CameraFadeCreate(texture, depth);\n'),
        Lines(3245, 1, "9a0f9baeead3d75c", '\t\t\tCameraFadeImage(cameraFade).texture = texture;\n'),
        Lines(3230, 0, "e3b0c44298fc1c14", (
            '\t\t\tCanvas canvas = cameraFade.GetComponent<Canvas>();\n'
            '\t\t\tif ((bool)canvas)\n'
            '\t\t\t{\n'
            '\t\t\t\tcanvas.sortingOrder = CameraFadeSortingOrder(depth);\n'
            '\t\t\t}\n'
        )),
        Lines(3225, 0, "e3b0c44298fc1c14", (
            '\t// Port: GUITexture was removed in Unity 2019+. The camera fade overlay is rebuilt as a\n'
            '\t// Screen Space Overlay Canvas with a RawImage on the same GameObject. Depth (formerly the\n'
            "\t// GUITexture's z position) maps to Canvas.sortingOrder, clamped to its 16-bit range.\n"
            '\t// GUITexture used 0.5 grey as neutral tint; RawImage uses white, so the initial tint is\n'
            '\t// white with alpha 0 and the fade tweens animate RawImage.color alpha.\n'
            '\tprivate static RawImage CameraFadeImage(GameObject go)\n'
            '\t{\n'
            '\t\tif (go == null || go != cameraFade)\n'
            '\t\t{\n'
            '\t\t\treturn null;\n'
            '\t\t}\n'
            '\t\treturn go.GetComponent<RawImage>();\n'
            '\t}\n'
            '\n'
            '\tprivate static int CameraFadeSortingOrder(int depth)\n'
            '\t{\n'
            '\t\treturn Mathf.Clamp(depth, short.MinValue, short.MaxValue);\n'
            '\t}\n'
            '\n'
            '\tprivate static GameObject CameraFadeCreate(Texture2D texture, int depth)\n'
            '\t{\n'
            '\t\tcameraFade = new GameObject("iTween Camera Fade");\n'
            '\t\tcameraFade.transform.position = new Vector3(0.5f, 0.5f, depth);\n'
            '\t\tCanvas canvas = cameraFade.AddComponent<Canvas>();\n'
            '\t\tcanvas.renderMode = RenderMode.ScreenSpaceOverlay;\n'
            '\t\tcanvas.sortingOrder = CameraFadeSortingOrder(depth);\n'
            '\t\tRawImage rawImage = cameraFade.AddComponent<RawImage>();\n'
            '\t\trawImage.raycastTarget = false;\n'
            '\t\trawImage.texture = texture;\n'
            '\t\trawImage.color = new Color(1f, 1f, 1f, 0f);\n'
            '\t\treturn cameraFade;\n'
            '\t}\n'
            '\n'
        )),
        Lines(2543, 5, "a65f2733499f23ff", '\t\t\tCameraFadeImage(target).color = array[3];\n'),
        Lines(2541, 1, "c166bb7b8e75a034", '\t\tif ((bool)CameraFadeImage(target))\n'),
        Lines(2500, 5, "714de73d9f601db8", '\t\t\tarray[0] = (array[1] = CameraFadeImage(target).color);\n'),
        Lines(2498, 1, "c166bb7b8e75a034", '\t\tif ((bool)CameraFadeImage(target))\n'),
        Lines(1970, 5, "5d4cc255958d7429", '\t\t\tCameraFadeImage(base.gameObject).color = colors[0, 1];\n'),
        Lines(1968, 1, "854c6be97b13ef2a", '\t\tif ((bool)CameraFadeImage(base.gameObject))\n'),
        Lines(1947, 5, "22666eef680228ec", '\t\t\tCameraFadeImage(base.gameObject).color = colors[0, 2];\n'),
        Lines(1945, 1, "854c6be97b13ef2a", '\t\tif ((bool)CameraFadeImage(base.gameObject))\n'),
        Lines(1181, 6, "6cac6113646e8e92", '\t\t\tcolors[0, 0] = (colors[0, 1] = CameraFadeImage(base.gameObject).color);\n'),
        Lines(1178, 1, "854c6be97b13ef2a", '\t\tif ((bool)CameraFadeImage(base.gameObject))\n'),
        Lines(365, 5, "49e612c6bd8ca4c6", '\t\t\tCameraFadeImage(target).color = color;\n'),
        Lines(363, 1, "c166bb7b8e75a034", '\t\tif ((bool)CameraFadeImage(target))\n'),
        Lines(316, 5, "f8b68390295b22dd", '\t\t\tcolor2 = (color = CameraFadeImage(target).color);\n'),
        Lines(314, 1, "c166bb7b8e75a034", '\t\tif ((bool)CameraFadeImage(target))\n'),
        Lines(4, 0, "e3b0c44298fc1c14", 'using UnityEngine.UI;\n'),
    ]),
]

RECIPE = Recipe("008-itween-camera-fade-ugui", STEPS)
