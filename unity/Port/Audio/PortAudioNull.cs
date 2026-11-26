// Audio-input entry points kept under their old class name.
//
// Why the name: this class used to be the silent Wwise backend. Two decompiled game files were
// patched (local patch 019) to call it directly for the game's own additions to the native sound
// module: AkInitializer.cs calls SetWishAudioInputCallbacks, and OnlineAudioClips.cs calls the
// four playback methods for voice clips received from phones. Keeping the class name and these
// five signatures means no decompiled file has to change again. The engine now lives in the
// PortAudio runtime (Core/), and these methods forward to PaEngine.

using PortAudio;

public static class PortAudioNull
{
    public static void SetWishAudioInputCallbacks()
    {
        PaEngine.AudioInputSetCallbacks();
    }

    public static bool GetIsAudioInputPlayingSample()
    {
        return PaEngine.AudioInputIsPlaying();
    }

    public static bool PlayAudioInputSampleData(byte[] data, int dataSizeBytes)
    {
        return PaEngine.AudioInputPlay(data, dataSizeBytes);
    }

    public static int GetAudioInputSamplePosition()
    {
        return PaEngine.AudioInputPosition();
    }

    public static void StopAudioInputPlaying()
    {
        PaEngine.AudioInputStop();
    }
}
