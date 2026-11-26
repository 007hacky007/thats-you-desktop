// Host stand-in for the PS4 debug-TTY plugin ("TTY").
//
// The PS4 build wrote developer messages to the devkit's user TTY channels
// and pushed Razor performance markers. Here messages go to the Unity log,
// prefixed with their channel. Performance markers have no host equivalent
// that is safe from any thread, so they are no-ops.

using UnityEngine;

namespace Wish.PS4
{
    public class TTY
    {
        public enum SceDbgUserChannel
        {
            SCE_DBG_USER_CHANNEL_2 = 0,
            SCE_DBG_USER_CHANNEL_3 = 1,
            SCE_DBG_USER_CHANNEL_4 = 2,
            SCE_DBG_USER_CHANNEL_5 = 3
        }

        public static void tty(SceDbgUserChannel channel, string str)
        {
            Debug.Log("[TTY" + ((int)channel + 2) + "] " + str);
        }

        public static void tty(string str)
        {
            tty(SceDbgUserChannel.SCE_DBG_USER_CHANNEL_2, str);
        }

        public static void PushPerfMarker(string label, uint abgrColour)
        {
        }

        public static void PopPerfMarker()
        {
        }
    }
}
