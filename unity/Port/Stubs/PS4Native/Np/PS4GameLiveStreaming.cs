// Host stand-in for the PS4 "PS4GameLiveStreaming" native plugin.
//
// On PS4 the game marked screen regions to hide from the system's live
// broadcast. The host has no system broadcaster, so every call succeeds and
// does nothing.
//
// Type and member names match the PS4 plugin.

using System;

namespace Wish.Core
{
    public class PS4GameLiveStreaming
    {
        public struct SceGameLiveStreamingGuardArea
        {
            public float x1;

            public float y1;

            public float x2;

            public float y2;
        }

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
            return 0;
        }

        public static int Terminate()
        {
            return 0;
        }

        public static int SetGuardAreas(uint areaCounts, IntPtr areas)
        {
            return 0;
        }

        public static int SetGuardArea(float x1, float y1, float x2, float y2)
        {
            return 0;
        }

        public static int ClearGuardArea()
        {
            return 0;
        }
    }
}
