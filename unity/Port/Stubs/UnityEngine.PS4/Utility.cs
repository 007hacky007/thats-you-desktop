// PC stand-in for the PS4-only UnityEngine.PS4.Utility class.
//
// The PS4 player shipped this class inside UnityEngine.dll; desktop Unity has
// no UnityEngine.PS4 namespace. Only the members the game uses are declared
// here, with names and signatures matching the PS4 API. Bodies are original
// PC behaviour written for this port.

namespace UnityEngine.PS4
{
    public sealed class Utility
    {
        // Console system language codes. Names and numeric values must match
        // the PS4 UnityEngine.dll exactly: the game serializes a dictionary
        // keyed by this enum (Localisation.SystemLanguageMapping), so a
        // renumbering would silently break the saved locale mapping.
        public enum SystemLanguage
        {
            JAPANESE = 0,
            ENGLISH_US = 1,
            FRENCH = 2,
            SPANISH = 3,
            GERMAN = 4,
            ITALIAN = 5,
            DUTCH = 6,
            PORTUGUESE_PT = 7,
            RUSSIAN = 8,
            KOREAN = 9,
            CHINESE_T = 10,
            CHINESE_S = 11,
            FINNISH = 12,
            SWEDISH = 13,
            DANISH = 14,
            NORWEGIAN = 15,
            POLISH = 16,
            PORTUGUESE_BR = 17,
            ENGLISH_GB = 18,
            TURKISH = 19,
            SPANISH_LA = 20,
            ARABIC = 21,
            FRENCH_CA = 22,
        }

        // PC: there is no console system overlay (PS button menu, system
        // dialogs drawn over the game), so this is always false.
        public static bool isSystemUiOverlaid
        {
            get { return false; }
        }

        // PC: the PS4 suspends titles into "background execution" when the
        // user returns to the home screen. Desktop has no such state, so this
        // is always false. (Window focus loss is handled by Unity itself via
        // Application.runInBackground / OnApplicationFocus.)
        public static bool isInBackgroundExecution
        {
            get { return false; }
        }

        // PC: maps the OS language reported by Application.systemLanguage to
        // the nearest console language code. Regional variants the desktop
        // API cannot distinguish (US/GB English, PT/BR Portuguese, ES/LA
        // Spanish, FR/CA French) map to the first listed variant. Unknown or
        // unsupported languages fall back to ENGLISH_US.
        public static SystemLanguage systemLanguage
        {
            get { return FromDesktopLanguage(Application.systemLanguage); }
        }

        private static SystemLanguage FromDesktopLanguage(UnityEngine.SystemLanguage language)
        {
            switch (language)
            {
                case UnityEngine.SystemLanguage.Japanese: return SystemLanguage.JAPANESE;
                case UnityEngine.SystemLanguage.English: return SystemLanguage.ENGLISH_US;
                case UnityEngine.SystemLanguage.French: return SystemLanguage.FRENCH;
                case UnityEngine.SystemLanguage.Spanish: return SystemLanguage.SPANISH;
                case UnityEngine.SystemLanguage.German: return SystemLanguage.GERMAN;
                case UnityEngine.SystemLanguage.Italian: return SystemLanguage.ITALIAN;
                case UnityEngine.SystemLanguage.Dutch: return SystemLanguage.DUTCH;
                case UnityEngine.SystemLanguage.Portuguese: return SystemLanguage.PORTUGUESE_PT;
                case UnityEngine.SystemLanguage.Russian: return SystemLanguage.RUSSIAN;
                case UnityEngine.SystemLanguage.Korean: return SystemLanguage.KOREAN;
                case UnityEngine.SystemLanguage.ChineseTraditional: return SystemLanguage.CHINESE_T;
                case UnityEngine.SystemLanguage.Chinese:
                case UnityEngine.SystemLanguage.ChineseSimplified: return SystemLanguage.CHINESE_S;
                case UnityEngine.SystemLanguage.Finnish: return SystemLanguage.FINNISH;
                case UnityEngine.SystemLanguage.Swedish: return SystemLanguage.SWEDISH;
                case UnityEngine.SystemLanguage.Danish: return SystemLanguage.DANISH;
                case UnityEngine.SystemLanguage.Norwegian: return SystemLanguage.NORWEGIAN;
                case UnityEngine.SystemLanguage.Polish: return SystemLanguage.POLISH;
                case UnityEngine.SystemLanguage.Turkish: return SystemLanguage.TURKISH;
                case UnityEngine.SystemLanguage.Arabic: return SystemLanguage.ARABIC;
                default: return SystemLanguage.ENGLISH_US;
            }
        }
    }
}
