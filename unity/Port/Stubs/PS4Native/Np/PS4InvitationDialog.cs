// Host stand-in for the PS4 "PS4InvitationDialog" native plugin (the PSN
// dialog for sending and receiving session invitations).
//
// There is no PSN on the host. The dialog opens, and the first status poll
// reports it finished, as if the player closed it without sending anything.
// Status values: 0 none, 1 initialized, 2 running, 3 finished.
//
// The class stays a MonoBehaviour with the original script GUID because a
// scene references it as a component. Member names match the PS4 plugin.

using System;
using UnityEngine;

public class PS4InvitationDialog : MonoBehaviour
{
    private static readonly PortCommonDialogState s_state = new PortCommonDialogState("PS4InvitationDialog");

    public static int LoadModule()
    {
        return 0;
    }

    public static int UnloadModule()
    {
        return 0;
    }

    public static int InitialiseInvitationDialog()
    {
        return s_state.Initialize();
    }

    public static int TerminateInvitationDialog()
    {
        return s_state.Terminate();
    }

    public static int OpenInvitationDialogSend(string userMessage, string sessionIDData, IntPtr id_list, int cnt)
    {
        return s_state.Open("send invitation to " + cnt + " player(s)");
    }

    public static int OpenInvitationDialogReceive()
    {
        return s_state.Open("received invitations");
    }

    public static int UpdateInvitationDialogStatus()
    {
        return s_state.Update();
    }

    public static int CloseInvitationDialog()
    {
        return s_state.Close();
    }
}
