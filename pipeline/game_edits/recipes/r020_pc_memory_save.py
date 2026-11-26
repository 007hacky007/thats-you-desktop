"""020-pc-memory-save."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    Delete([
        "Assets/Scripts/SonyPS4MemorySave/Sony",
    ]),
    File("Assets/Scripts/Assembly-CSharp/GameMemorySave.cs", [
        InMember("GameMemorySave.GameData.GameData", [(
            r"(\t+)(writer = new BinaryWriter\(stream\);\n)",
            "\\1// Port: grow Length to the full buffer now (still all zero), so the\n"
            "\\1// SetLength calls in ReadFromBlob/WriteToBlob are no-ops. Newer Mono\n"
            "\\1// zero-fills on grow, which wiped the blob filled by MemoryLoad.\n"
            "\\1stream.SetLength(1048576L);\n\\1\\2", 1)]),
    ]),
]

RECIPE = Recipe("020-pc-memory-save", STEPS)
