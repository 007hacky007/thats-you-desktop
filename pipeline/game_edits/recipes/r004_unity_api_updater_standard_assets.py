"""004-unity-api-updater-standard-assets: Unity 6 API renames in the files that use removed members."""
from pipeline.game_edits import File, Recipe
from pipeline.game_edits.api_renames import ops

FILES = [
       'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/CinematicEffects/AmbientOcclusion.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/CinematicEffects/DepthOfField.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/ImageEffects/Bloom.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/ImageEffects/BloomAndFlares.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/ImageEffects/DepthOfField.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/ImageEffects/SunShafts.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/Utility/DragRigidbody.cs',
    'Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/Utility/ObjectResetter.cs',
]

STEPS = [File(f, ops()) for f in FILES]

RECIPE = Recipe("004-unity-api-updater-standard-assets", STEPS)
