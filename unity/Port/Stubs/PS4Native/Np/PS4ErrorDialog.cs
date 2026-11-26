// Host stand-in for the PS4 "PS4ErrorDialog" native plugin (the system
// dialog that explains a PSN or network error code).
//
// Nothing is drawn on the host: the dialog opens, logs the error code and
// reports finished on the next status poll, as if the player dismissed it.
//
// Type and member names match the PS4 plugin.

public class PS4ErrorDialog
{
    public enum SceErrorDialogStatus
    {
        SCE_ERROR_DIALOG_STATUS_NONE = 0,
        SCE_ERROR_DIALOG_STATUS_INITIALIZED = 1,
        SCE_ERROR_DIALOG_STATUS_RUNNING = 2,
        SCE_ERROR_DIALOG_STATUS_FINISHED = 3
    }

    private static readonly PortCommonDialogState s_state = new PortCommonDialogState("PS4ErrorDialog");

    public static int LoadModule()
    {
        return 0;
    }

    public static int UnloadModule()
    {
        return 0;
    }

    public static int Initialize()
    {
        return s_state.Initialize();
    }

    public static int Terminate()
    {
        return s_state.Terminate();
    }

    public static int Close()
    {
        return s_state.Close();
    }

    public static uint GetStatus()
    {
        return (uint)s_state.Status;
    }

    public static int Open(int errorCode)
    {
        return s_state.Open(string.Format("error 0x{0:X8}", errorCode));
    }

    public static uint UpdateStatus()
    {
        return (uint)s_state.Update();
    }
}
