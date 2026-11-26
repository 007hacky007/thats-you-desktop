// Shared state for the host stand-ins of the PS4 system dialogs
// (message, error, commerce, friend list and invitation dialogs).
//
// On PS4 these dialogs were drawn by the system software. The host has no
// such UI, so every dialog goes through the usual lifecycle but never shows
// anything: Initialize -> Open (running) -> the first status poll reports
// finished -> Terminate. The game's own code then reads a "cancelled"
// result and continues exactly as if the player had backed out.
//
// Dialogs that the player cannot end on PS4 either (wait messages and
// progress bars) are opened with holdUntilClosed: they stay running until
// the game calls Close(). A dialog ended by Close() reports OK, because it
// was the application, not the player, that dismissed it.
//
// Status values follow the common dialog status numbering the game uses:
// 0 none, 1 initialized, 2 running, 3 finished.

using UnityEngine;

internal sealed class PortCommonDialogState
{
    public const int StatusNone = 0;
    public const int StatusInitialized = 1;
    public const int StatusRunning = 2;
    public const int StatusFinished = 3;

    // Generic failure returned when a call does not fit the current state
    // (for example reading a result before the dialog finished).
    public const int ErrorInvalidState = unchecked((int)0x80B80003);

    private readonly string m_name;
    private int m_status = StatusNone;
    private bool m_holdUntilClosed;
    private bool m_closedByApp;

    public PortCommonDialogState(string name)
    {
        m_name = name;
    }

    public int Status
    {
        get { return m_status; }
    }

    public bool IsFinished
    {
        get { return m_status == StatusFinished; }
    }

    // True when the last dialog was ended by Close() rather than by the
    // simulated player cancel. Callers use it to report OK instead of
    // USER_CANCELED.
    public bool ClosedByApp
    {
        get { return m_closedByApp; }
    }

    public int Initialize()
    {
        m_status = StatusInitialized;
        m_holdUntilClosed = false;
        m_closedByApp = false;
        return 0;
    }

    public int Terminate()
    {
        m_status = StatusNone;
        m_holdUntilClosed = false;
        m_closedByApp = false;
        return 0;
    }

    // Opening always "succeeds" so callers keep their bookkeeping balanced
    // (several of them block other dialogs until this one reports finished).
    public int Open(string what)
    {
        return Open(what, false);
    }

    public int Open(string what, bool holdUntilClosed)
    {
        m_status = StatusRunning;
        m_holdUntilClosed = holdUntilClosed;
        m_closedByApp = false;
        if (!string.IsNullOrEmpty(what))
        {
            Debug.Log("[" + m_name + " stub] not shown on this platform, "
                + (holdUntilClosed ? "held open until closed by the game: " : "reported as cancelled: ") + what);
        }
        return 0;
    }

    public int Close()
    {
        if (m_status == StatusRunning)
        {
            m_status = StatusFinished;
            m_closedByApp = true;
        }
        return 0;
    }

    // The first poll after Open reports the dialog as finished, unless it
    // is held until Close().
    public int Update()
    {
        if (m_status == StatusRunning && !m_holdUntilClosed)
        {
            m_status = StatusFinished;
        }
        return m_status;
    }
}
