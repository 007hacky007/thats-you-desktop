"""019-wwise-native-noop."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/AkSoundEnginePINVOKE.cs",
    ]),
    File("Assets/Scripts/Assembly-CSharp/AkInitializer.cs", [
        Lines(52, 2, "1dafa92a6c58ebde", (
            '\tprivate static void SetWishAudioInputCallbacks()\n'
            '\t{\n'
            '\t\tPortAudioNull.SetWishAudioInputCallbacks();\n'
            '\t}\n'
        )),
    ]),
    File("Assets/Scripts/Assembly-CSharp/OnlineAudioClips.cs", [
        Lines(86, 2, "d66a4b429589230a", (
            '\tprivate static void StopAudioInputPlaying()\n'
            '\t{\n'
            '\t\tPortAudioNull.StopAudioInputPlaying();\n'
            '\t}\n'
        )),
        Lines(83, 2, "ad2daaa3d15c2b08", (
            '\tprivate static int GetAudioInputSamplePosition()\n'
            '\t{\n'
            '\t\treturn PortAudioNull.GetAudioInputSamplePosition();\n'
            '\t}\n'
        )),
        Lines(80, 2, "161a492367ff8d70", (
            '\tprivate static bool PlayAudioInputSampleData(byte[] data, int dataSizeBytes)\n'
            '\t{\n'
            '\t\treturn PortAudioNull.PlayAudioInputSampleData(data, dataSizeBytes);\n'
            '\t}\n'
        )),
        Lines(77, 2, "db45ec92c51907ee", (
            '\tprivate static bool GetIsAudioInputPlayingSample()\n'
            '\t{\n'
            '\t\treturn PortAudioNull.GetIsAudioInputPlayingSample();\n'
            '\t}\n'
        )),
    ]),
]

RECIPE = Recipe("019-wwise-native-noop", STEPS)
