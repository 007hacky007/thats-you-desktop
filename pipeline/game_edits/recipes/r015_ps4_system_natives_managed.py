"""015-ps4-system-natives-managed."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PS4AppContent.cs",
        "Assets/Scripts/Assembly-CSharp/PS4EventProcessor.cs",
        "Assets/Scripts/Assembly-CSharp/PluginDLLInterface.cs",
        "Assets/Scripts/Assembly-CSharp/SystemCache.cs",
        "Assets/Scripts/LowLevelTTY/Wish",
    ]),
    File("Assets/Scripts/Assembly-CSharp/MachineName.cs", [
        Lines(15, 2, "e8c871a76bb3fa7d", (
            '\tprivate static string GetSystemName()\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4SystemPlatform.GetSystemName();\n'
            '\t}\n'
        )),
        Lines(1, 1, "6beada9004011398", ""),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4SystemServiceEnableMusicPlayer.cs", [
        Lines(6, 2, "d432a47c18eae03c", (
            '\tprivate static void SystemServiceEnableMusicPlayer(bool enable)\n'
            '\t{\n'
            '\t\tPortPlatform.PS4SystemPlatform.SystemServiceEnableMusicPlayer(enable);\n'
            '\t}\n'
        )),
        Lines(1, 1, "6beada9004011398", ""),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4SystemServicePowerTick.cs", [
        Lines(9, 2, "192925519863405b", (
            '\tprivate static uint SystemServicePowerTick()\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4SystemPlatform.SystemServicePowerTick();\n'
            '\t}\n'
        )),
        Lines(2, 1, "6beada9004011398", ""),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PlayGo.cs", [
        Lines(101, 2, "886837fa97bff1ff", (
            '\tprivate static ulong PlayGo_GetLanguageMask()\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4PlayGoNative.GetLanguageMask();\n'
            '\t}\n'
        )),
        Lines(98, 2, "e90f721e8246ee65", (
            '\tprivate static float PlayGo_GetChunkProgress(int chunkID)\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4PlayGoNative.GetChunkProgress(chunkID);\n'
            '\t}\n'
        )),
        Lines(95, 2, "9e9d1ecb9124f6ed", (
            '\tprivate static void Prefetch_Internal(int chunkID)\n'
            '\t{\n'
            '\t\tPortPlatform.PS4PlayGoNative.Prefetch(chunkID);\n'
            '\t}\n'
        )),
        Lines(92, 2, "a326fb786d773556", (
            '\tprivate static int SetInstallSpeed_Internal(ScePlayGoInstallSpeedValue installSpeed)\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4PlayGoNative.SetInstallSpeed((int)installSpeed);\n'
            '\t}\n'
        )),
        Lines(89, 2, "5f067a46117ce787", (
            '\tprivate static int GetChunkLocus_Internal(int chunkID)\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4PlayGoNative.GetChunkLocus(chunkID);\n'
            '\t}\n'
        )),
        Lines(86, 2, "f4587f63dfb26883", (
            '\tprivate static bool CheckFileStat(string filename)\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4PlayGoNative.CheckFileStat(filename);\n'
            '\t}\n'
        )),
        Lines(83, 2, "9d9097f61aa4251b", (
            '\tprivate static void StopPlugin()\n'
            '\t{\n'
            '\t\tPortPlatform.PS4PlayGoNative.Terminate();\n'
            '\t}\n'
        )),
        Lines(80, 2, "7c186c62f0763769", (
            '\tprivate static int Initialize()\n'
            '\t{\n'
            '\t\treturn PortPlatform.PS4PlayGoNative.Initialize();\n'
            '\t}\n'
        )),
        Lines(5, 1, "6beada9004011398", ""),
    ]),
]

RECIPE = Recipe("015-ps4-system-natives-managed", STEPS)
