// Host replacements for the small PS4 system-service natives (the
// "PS4General" plugin) and for the PS4 PlayGo plugin ("PS4PlayGo").
//
// The game classes that used these natives keep their own logic; only their
// extern declarations are redirected here by local patches. Every method is
// safe to call at any time and never throws.

using System;
using System.IO;
using UnityEngine;

namespace PortPlatform
{
    // Stand-ins for the "PS4General" system-service entry points.
    public static class PS4SystemPlatform
    {
        private const string FallbackName = "That's You";

        private static string s_cachedName;

        // The PS4 returned the console's user-visible name, which the game
        // shows so phones can tell which console to join. On a computer the
        // equivalent is the host's own name. It is read at runtime only.
        public static string GetSystemName()
        {
            if (s_cachedName != null)
            {
                return s_cachedName;
            }
            string name = null;
            try
            {
                name = SystemInfo.deviceName;
            }
            catch (Exception)
            {
            }
            if (string.IsNullOrEmpty(name) || name == SystemInfo.unsupportedIdentifier)
            {
                try
                {
                    name = Environment.MachineName;
                }
                catch (Exception)
                {
                    name = null;
                }
            }
            name = name == null ? string.Empty : name.Trim();
            if (name.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".local".Length);
            }
            if (name.Length == 0)
            {
                name = FallbackName;
            }
            s_cachedName = name;
            return name;
        }

        // Kept the PS4 from dimming or sleeping during play. Desktop players
        // already keep the display awake through Screen.sleepTimeout, so this
        // is a no-op that reports success.
        public static uint SystemServicePowerTick()
        {
            return 0u;
        }

        // Toggled the PS4 background music player. Nothing to do on a host.
        public static void SystemServiceEnableMusicPlayer(bool enable)
        {
        }
    }

    // Stand-ins for the "PS4PlayGo" streaming-install entry points. A desktop
    // install always has every chunk on local storage, so everything reports
    // fully installed and locally available at full speed.
    public static class PS4PlayGoNative
    {
        // Values of the SDK locus/speed constants used by the game.
        public const int LocusLocalFast = 3;
        public const int InstallSpeedFull = 2;

        public static int Initialize()
        {
            return 0;
        }

        public static void Terminate()
        {
        }

        public static bool CheckFileStat(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }
            try
            {
                return File.Exists(filename) || Directory.Exists(filename);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static int GetChunkLocus(int chunkID)
        {
            return LocusLocalFast;
        }

        public static int SetInstallSpeed(int installSpeed)
        {
            return 0;
        }

        public static void Prefetch(int chunkID)
        {
        }

        // Fraction installed, 1 meaning complete.
        public static float GetChunkProgress(int chunkID)
        {
            return 1f;
        }

        // Every language bit set: all languages are on disk.
        public static ulong GetLanguageMask()
        {
            return ulong.MaxValue;
        }
    }
}
