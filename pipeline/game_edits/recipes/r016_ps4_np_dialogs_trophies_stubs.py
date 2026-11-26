"""016-ps4-np-dialogs-trophies-stubs."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PS4ErrorDialog.cs",
        "Assets/Scripts/Assembly-CSharp/PS4IMEDialog.cs",
        "Assets/Scripts/Assembly-CSharp/PS4InvitationDialog.cs",
        "Assets/Scripts/Assembly-CSharp/PS4MsgDialog.cs",
        "Assets/Scripts/Assembly-CSharp/PS4NpCommerceDialog.cs",
        "Assets/Scripts/Assembly-CSharp/PS4NpFriendListDialog.cs",
        "Assets/Scripts/Assembly-CSharp/PS4NpLibrary.cs",
        "Assets/Scripts/Assembly-CSharp/PS4NpWordFilter.cs",
        "Assets/Scripts/Assembly-CSharp/PS4Trophies.cs",
        "Assets/Scripts/Assembly-CSharp/UnityPlayerInterface.cs",
        "Assets/Scripts/Assembly-CSharp/Wish/Core/PS4GameLiveStreaming.cs",
    ]),
]

RECIPE = Recipe("016-ps4-np-dialogs-trophies-stubs", STEPS)
