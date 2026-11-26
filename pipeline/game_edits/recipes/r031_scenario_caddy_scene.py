"""031-scenario-caddy-scene."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/ScenarioTransitionerAsyncLoads.cs", [
        InMember("ScenarioTransitionerAsyncLoads.CaddyDisownNodeKeepTransform", [(
            r"(\t+)(cameraCaddyForDolly\.transform\.parent = null;\n)",
            "\\1\\2\\1// Unity 6 keeps an unparented object in its old parent's scene; move it back to ours so\n"
            "\\1// unloading the old scenario does not destroy it.\n"
            "\\1UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraCaddyForDolly, base.gameObject.scene);\n", 1)]),
    ]),
]

RECIPE = Recipe("031-scenario-caddy-scene", STEPS)
