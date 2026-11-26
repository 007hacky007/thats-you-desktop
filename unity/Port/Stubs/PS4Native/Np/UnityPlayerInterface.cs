// Host stand-in for the PS4 "UnityPlayerInterface" native plugin.
//
// On PS4 the plugin hooked the player runtime and queued system events for
// the game: PSN session invitations and "Play Together" requests. The host
// has no PSN, so setup and shutdown do nothing and both event queues are
// always empty (nothing pending, nothing lost). PS4EventManager polls these
// every frame and simply finds no work.

using System;

public class UnityPlayerInterface
{
    public static void SetupRuntimeInterfaces()
    {
    }

    public static void Shutdown()
    {
    }

    public static bool NPSessionInvitationEventParams_GetAreAny()
    {
        return false;
    }

    public static bool NPSessionInvitationEventParams_Get(IntPtr outItem)
    {
        return false;
    }

    public static bool NPSessionInvitationEventParams_GetLostFlag()
    {
        return false;
    }

    public static void NPSessionInvitationEventParams_ClearLostFlag()
    {
    }

    public static bool NPPlayTogetherEventParams_GetAreAny()
    {
        return false;
    }

    public static bool NPPlayTogetherEventParams_Get(IntPtr outItem)
    {
        return false;
    }

    public static bool NPPlayTogetherEventParams_GetLostFlag()
    {
        return false;
    }

    public static void NPPlayTogetherEventParams_ClearLostFlag()
    {
    }
}
