// Host stand-in for the PS4 "PS4MsgDialog" native plugin (system message,
// progress bar and system notice dialogs).
//
// Nothing is drawn on the host. A dialog the player would answer (OK,
// Yes/No, OK/Cancel, system messages) reports finished on the next status
// poll with a "user cancelled" result and no button pressed, so MsgDialog's
// queue keeps moving and its finished event fires. Wait messages and
// progress bars cannot be answered by the player, so they stay running
// until the game calls Close(), and then report OK.
// The message text goes to the Unity log so it is not lost.
//
// Type, constant and member names match the PS4 plugin.

using System;
using System.Runtime.InteropServices;

public class PS4MsgDialog
{
    public struct SceMsgDialogResult
    {
        public int mode;

        public int result;

        public int buttonId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string reserved;
    }

    public enum SceCommonDialogStatus
    {
        SCE_COMMON_DIALOG_STATUS_NONE = 0,
        SCE_COMMON_DIALOG_STATUS_INITIALIZED = 1,
        SCE_COMMON_DIALOG_STATUS_RUNNING = 2,
        SCE_COMMON_DIALOG_STATUS_FINISHED = 3
    }

    public enum SceMsgDialogSystemMessageType
    {
        SCE_MSG_DIALOG_SYSMSG_TYPE_TRC_EMPTY_STORE = 0,
        SCE_MSG_DIALOG_SYSMSG_TYPE_TRC_PSN_CHAT_RESTRICTION = 1,
        SCE_MSG_DIALOG_SYSMSG_TYPE_TRC_PSN_UGC_RESTRICTION = 2,
        SCE_MSG_DIALOG_SYSMSG_TYPE_TRC_WARNING_SWITCH_TO_SIMULVIEW = 3
    }

    public enum SceCommonDialogResultResult
    {
        SCE_COMMON_DIALOG_RESULT_OK = 0,
        SCE_COMMON_DIALOG_RESULT_USER_CANCELED = 1,
        SCE_NP_COMMERCE_DIALOG_RESULT_PURCHASED = 2
    }

    public const int SCE_MSG_DIALOG_PROGRESSBAR_TARGET_BAR_DEFAULT = 0;

    // Dialog kinds recorded in the result's mode field.
    private const int ModeUserMsg = 1;
    private const int ModeProgressBar = 2;
    private const int ModeSystemMsg = 3;

    private static readonly PortCommonDialogState s_state = new PortCommonDialogState("PS4MsgDialog");
    private static int s_mode;

    // SCE_MSG_DIALOG_BUTTON_TYPE_WAIT and _WAIT_CANCEL: progress messages
    // that the game closes itself when the operation completes.
    private const int ButtonTypeWait = 5;
    private const int ButtonTypeWaitCancel = 6;

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

    public static int GetStatus()
    {
        return s_state.Status;
    }

    public static int UpdateStatus()
    {
        return s_state.Update();
    }

    public static int Open_User_Msg(string msg, int buttonType)
    {
        s_mode = ModeUserMsg;
        bool hold = buttonType == ButtonTypeWait || buttonType == ButtonTypeWaitCancel;
        return s_state.Open(msg, hold);
    }

    public static int Open_Progress_Bar(int barType, string msg)
    {
        s_mode = ModeProgressBar;
        return s_state.Open(msg, true);
    }

    public static int Open_System_Msg(int sysMsgType)
    {
        s_mode = ModeSystemMsg;
        return s_state.Open(((SceMsgDialogSystemMessageType)sysMsgType).ToString());
    }

    public static int ProgressBarInc(int target, uint delta)
    {
        return 0;
    }

    public static int ProgressBarSetMsg(int target, string barMsg)
    {
        return 0;
    }

    public static int ProgressBarSetValue(int target, uint rate)
    {
        return 0;
    }

    private static SceMsgDialogResult CurrentResult()
    {
        SceMsgDialogResult r = default(SceMsgDialogResult);
        r.mode = s_mode;
        r.result = s_state.ClosedByApp
            ? (int)SceCommonDialogResultResult.SCE_COMMON_DIALOG_RESULT_OK
            : (int)SceCommonDialogResultResult.SCE_COMMON_DIALOG_RESULT_USER_CANCELED;
        r.buttonId = 0;
        return r;
    }

    public static int GetResult(IntPtr result)
    {
        if (!s_state.IsFinished || result == IntPtr.Zero)
        {
            return PortCommonDialogState.ErrorInvalidState;
        }
        Marshal.StructureToPtr(CurrentResult(), result, false);
        return 0;
    }

    public static int GetResult(ref SceMsgDialogResult result)
    {
        if (!s_state.IsFinished)
        {
            return PortCommonDialogState.ErrorInvalidState;
        }
        result = CurrentResult();
        return 0;
    }
}
