// Host stand-in for the PS4 "PS4NpCommerceDialog" native plugin (the
// PlayStation Store / PlayStation Plus upsell dialog).
//
// There is no store on the host. The dialog opens, reports finished on the
// next status poll and returns "user cancelled, not authorized", so the
// game records that no Plus entitlement was obtained.
//
// Member names match the PS4 plugin.

public class PS4NpCommerceDialog
{
    // SCE_COMMON_DIALOG_RESULT_USER_CANCELED
    private const int ResultUserCancelled = 1;

    // SCE_COMMON_DIALOG_RESULT_OK, used when the game closed the dialog.
    private const int ResultOk = 0;

    private static readonly PortCommonDialogState s_state = new PortCommonDialogState("PS4NpCommerceDialog");

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

    public static int GetResult(ref int result, ref bool authorized)
    {
        authorized = false;
        if (!s_state.IsFinished)
        {
            return PortCommonDialogState.ErrorInvalidState;
        }
        result = s_state.ClosedByApp ? ResultOk : ResultUserCancelled;
        return 0;
    }

    public static uint GetStatus()
    {
        return (uint)s_state.Status;
    }

    public static int Open_Plus(int features)
    {
        return s_state.Open("PlayStation Plus upsell (features " + features + ")");
    }

    public static uint UpdateStatus()
    {
        return (uint)s_state.Update();
    }
}
