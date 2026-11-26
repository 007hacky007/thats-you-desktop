"""013-main-thread-background-image-decode."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    File("Assets/Scripts/Assembly-CSharp/BackgroundImageLoader.cs", [
        ReplaceMember("BackgroundImageLoader.BackgroundTextureLoad", (
            "\tpublic static void BackgroundTextureLoad(object obj)\n\t{\n"
            "\t\t// Port: the original called Texture2D.CreateTextureFromBufferOnBackgroundThread(data, false),\n"
            "\t\t// a PS4-only Unity extension. Desktop Unity can only create textures on the main thread, so\n"
            "\t\t// Update() decodes the buffer with LoadImage and invokes the callback on the next frame.\n"
            "\t\t// No worker thread: an earlier version started one before setting m_busy, and when the\n"
            "\t\t// thread finished first the flag stuck at true and the callback never came (game hang).\n"
            "\t\tThreadData threadData = (ThreadData)obj;\n"
            "\t\tthreadData.texture_ = null;\n"
            "\t\tm_busy = false;\n"
            "\t\tm_triggerCallback = true;\n\t}\n")),
        InMember("BackgroundImageLoader.LoadTextureFromBytes", [
            (r"\t\tThread \w+ = new Thread\(BackgroundTextureLoad\);\n", "", 1),
            (r"\t\t\w+\.Start\(m_threadData\);\n\t\tm_busy = true;\n", "\t\tBackgroundTextureLoad(m_threadData);\n", 1),
        ]),
        InMember("BackgroundImageLoader.Update", [(
            r"(if \(!m_busy && m_triggerCallback\)\n\t\t\{\n)",
            "\\1\t\t\tif (m_threadData.texture_ == null && m_threadData.data != null)\n\t\t\t{\n"
            "\t\t\t\tTexture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);\n"
            "\t\t\t\tif (texture.LoadImage(m_threadData.data, false))\n\t\t\t\t{\n"
            "\t\t\t\t\tm_threadData.texture_ = texture;\n\t\t\t\t}\n\t\t\t\telse\n\t\t\t\t{\n"
            "\t\t\t\t\t// Failed decode: pass a null texture, as the PS4 API did.\n"
            "\t\t\t\t\tObject.Destroy(texture);\n\t\t\t\t}\n\t\t\t}\n", 1)]),
    ]),
]

RECIPE = Recipe("013-main-thread-background-image-decode", STEPS)
