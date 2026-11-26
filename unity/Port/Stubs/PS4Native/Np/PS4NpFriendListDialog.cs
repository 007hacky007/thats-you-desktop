// Host stand-in for the PS4 "PS4NpFriendListDialog" native plugin (the PSN
// friend picker used to invite friends to a party).
//
// There is no PSN friend list on the host. The dialog opens, reports
// finished on the next status poll and returns "cancelled" with nobody
// selected, so FriendListDialog raises its cancelled event.
//
// Type, constant and member names match the PS4 plugin.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class PS4NpFriendListDialog
{
    public struct SceNpFriendListDialogResult
    {
        public int result;

        public int userAction;

        public SceNpOnlineId onlineId;

        public int padding;

        public IntPtr userData;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string reserved;
    }

    public enum Mode
    {
        SCE_NP_FRIEND_LIST_DIALOG_MODE_INVALID = 0,
        SCE_NP_FRIEND_LIST_DIALOG_MODE_FRIEND_LIST = 1,
        SCE_NP_FRIEND_LIST_DIALOG_MODE_SELECT_MULTIPLE = 2
    }

    [Flags]
    public enum MenuItems
    {
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_INVALID = 0,
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_SEARCH = 1,
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_ALL_FRIENDS = 4,
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_PLAYERS_MET = 8,
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_PARTY_MEMBER = 0x10,
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_FAVORITE_GROUPS = 0x20,
        SCE_NP_FRIEND_LIST_DIALOG_MENU_ITEM_ONLINE = 2
    }

    [Flags]
    public enum MultipleOptions
    {
        SCE_NP_FRIEND_LIST_DIALOG_SELECT_OPTION_NONE = 0,
        SCE_NP_FRIEND_LIST_DIALOG_SELECT_OPTION_ENABLE_BLOCK_PLAYER = 1,
        SCE_NP_FRIEND_LIST_DIALOG_SELECT_OPTION_ENABLE_NO_ONE_SELECTED = 2
    }

    public enum SceNpFriendListDialogPlayerStatus
    {
        SCE_NP_FRIEND_LIST_PLAYER_STATUS_SELECTED = 0,
        SCE_NP_FRIEND_LIST_PLAYER_STATUS_DISABLE = 1,
        SCE_NP_FRIEND_LIST_PLAYER_STATUS_DISABLE_CANT_SELECT = 2,
        SCE_NP_FRIEND_LIST_PLAYER_STATUS_DISABLE_JOINED = 3,
        SCE_NP_FRIEND_LIST_PLAYER_STATUS_DISABLE_BLOCKED = 4
    }

    public struct SceNpFriendListDialogPlayerParam
    {
        public SceNpOnlineId onlineId;

        public int status;

        public byte reserved0;

        public byte reserved1;

        public byte reserved2;

        public byte reserved3;

        public byte reserved4;

        public byte reserved5;

        public byte reserved6;

        public byte reserved7;
    }

    public const int SCE_NP_FRIEND_LIST_DIALOG_SELECT_MAX = 256;

    // SCE_COMMON_DIALOG_RESULT_USER_CANCELED; user action 1 means "cancelled".
    private const int ResultUserCancelled = 1;

    // SCE_COMMON_DIALOG_RESULT_OK, used when the game closed the dialog.
    private const int ResultOk = 0;
    private const int UserActionCancelled = 1;

    private static readonly PortCommonDialogState s_state = new PortCommonDialogState("PS4NpFriendListDialog");

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

    public static int Open(int mode, string title, int menuItems, int selectMultipleOptions, int max)
    {
        return s_state.Open("friend list: " + title);
    }

    public static int Open_WithPlayerStatuses(int mode, string title, int menuItems, int selectMultipleOptions, int max, IntPtr playerList, int playerListNum)
    {
        return s_state.Open("friend list: " + title);
    }

    public static int Open(int mode, string title, int menuItems, int selectMultipleOptions, int max, List<string> alreadyJoinedPlayerIDs)
    {
        return s_state.Open("friend list: " + title);
    }

    public static int GetStatus()
    {
        return s_state.Status;
    }

    public static int UpdateStatus()
    {
        return s_state.Update();
    }

    public static int Close()
    {
        return s_state.Close();
    }

    private static SceNpFriendListDialogResult CurrentResult()
    {
        SceNpFriendListDialogResult r = default(SceNpFriendListDialogResult);
        r.result = s_state.ClosedByApp ? ResultOk : ResultUserCancelled;
        r.userAction = UserActionCancelled;
        return r;
    }

    private static int GetResult(IntPtr pResult)
    {
        if (!s_state.IsFinished || pResult == IntPtr.Zero)
        {
            return PortCommonDialogState.ErrorInvalidState;
        }
        Marshal.StructureToPtr(CurrentResult(), pResult, false);
        return 0;
    }

    public static int GetResult(ref SceNpFriendListDialogResult result)
    {
        if (!s_state.IsFinished)
        {
            return PortCommonDialogState.ErrorInvalidState;
        }
        result = CurrentResult();
        return 0;
    }

    // Nobody is ever selected.
    public static bool SelectedPlayers_GetAreAny()
    {
        return false;
    }

    public static bool SelectedPlayers_Get(IntPtr outItem)
    {
        return false;
    }

    public static bool SelectedPlayers_GetLostFlag()
    {
        return false;
    }

    public static void SelectedPlayers_ClearLostFlag()
    {
    }

    public static int SelectedPlayers_GetAll(IntPtr outItems)
    {
        return 0;
    }
}
