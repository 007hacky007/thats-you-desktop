// Shared constants for the managed stand-ins of the PS4 networking natives.
//
// The PS4 build loaded several native plugins (matching, reliable UDP,
// sockets, WebAPI, network control, Wi-Fi Direct access point, UPnP probe).
// None of them exist on desktop, so every binding is replaced by a managed
// method that reports "offline": no connection, no pending events, and a
// negative error code for every operation. Callers already handle native
// failures, so the game settles into a clean idle offline state instead of
// throwing DllNotFoundException. Real networking arrives in a later plan and
// will replace these bodies behind the same entry points.

public static class PS4NetworkOffline
{
    // Negative, SCE style (high bit set) result returned by every stand-in
    // operation that would have needed the native library. Chosen to be easy
    // to spot in logs; no PS4 SDK error uses this value.
    public const int ErrorNotAvailable = unchecked((int)0x80FF0001);
}
