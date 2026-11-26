"""022-unity6-scene-unload-guard."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/LoadScene.cs", [
        InMember("LoadScene.LoadLevel", [(
            r"\t\tSceneManager\.UnloadScene\(0\);\n",
            "\t\t// Port: Unity 6 throws on unloading a scene that is not loaded; 5.3 returned false.\n"
            "\t\tif (SceneManager.GetSceneByBuildIndex(0).isLoaded)\n\t\t{\n"
            "\t\t\tSceneManager.UnloadScene(0);\n\t\t}\n", 1)]),
    ]),
    File("Assets/Scripts/Assembly-CSharp/RefreshScenes.cs", [
        InMember("RefreshScenes.MemoryRefresh", [(
            r"\t\tSceneManager\.UnloadScene\(\"Home\"\);\n",
            "\t\tif (SceneManager.GetSceneByName(\"Home\").isLoaded)\n\t\t{\n"
            "\t\t\tSceneManager.UnloadScene(\"Home\");\n\t\t}\n", 1)]),
    ]),
]

RECIPE = Recipe("022-unity6-scene-unload-guard", STEPS)
