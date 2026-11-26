"""029-party-master-missing-script-cleanup.

Party-Master still carries components of scripts the PS4 build stripped (debug helpers), which
Unity 6 reports as missing scripts, and its canvas points at the old UnityEngine.UI.dll. Remove
those components and point the canvas at the com.unity.ugui package scripts.
"""
from pipeline.game_edits import File, Recipe, RemoveScripts, ScriptRemap

MISSING = {
    "42a79b22e48a343a2321989ac4cfda2a",
    "7df4402bc4c7ca835bf7ccd369f3758e",
    "ea9beff0d77339c0842cbabe75c18c6b",
}
OLD_UI_DLL = "f5f67c52d1564df4a8936ccd202a3bd8"
UGUI = {
    (1301386320, OLD_UI_DLL): (11500000, "dc42784cf147c0c48a680349fa168899"),  # GraphicRaycaster
    (1980459831, OLD_UI_DLL): (11500000, "0cd44c1031e13a943bb63640046fad76"),  # CanvasScaler
}

STEPS = [
    File("Assets/_Scene/Party-Master.unity", [RemoveScripts(MISSING, empty=True), ScriptRemap(UGUI)]),
]

RECIPE = Recipe("029-party-master-missing-script-cleanup", STEPS)
