// Host stand-in for the PS4 "PS4IMEDialog" native plugin (the system
// on-screen keyboard).
//
// The host has no system on-screen keyboard here. Loading the module fails
// with an error code (it never throws), the keyboard never opens and any
// result reads as "cancelled". Text entry from a physical keyboard is a
// separate, later piece of work.
//
// The class stays a MonoBehaviour with its original script GUID. Member
// names match the PS4 plugin.

using System;
using UnityEngine;

public class PS4IMEDialog : MonoBehaviour
{
    // System module "unavailable" error (0x805A1000).
    private const int ErrorUnavailable = unchecked((int)0x805A1000);

    // Non-zero result: the dialog did not end with text accepted.
    private const int ResultCancelled = 1;

    public static int LoadModule()
    {
        return ErrorUnavailable;
    }

    public static int UnloadModule()
    {
        return ErrorUnavailable;
    }

    public static int ShowImeDialogBox(string dialogTitle, string placeholderText, int maxTextLength, float xPos, float yPos, IntPtr outputMessageText, string previousMessage)
    {
        return ErrorUnavailable;
    }

    public static bool ImeDialogBoxOpen()
    {
        return false;
    }

    public static int ImeDialogResult()
    {
        return ResultCancelled;
    }

    public static int TerminateDialog()
    {
        return 0;
    }
}
