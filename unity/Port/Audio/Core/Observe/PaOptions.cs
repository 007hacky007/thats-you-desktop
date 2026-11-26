using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PortAudio
{
    /// <summary>
    /// Parses the PortAudio player flags into a <see cref="PaSettings"/>. Flag names match
    /// case-insensitively; the last occurrence wins. Paths are made absolute. A bad or missing value
    /// gives a warning and keeps the default. See unity/Port/Runtime/PortDebug/README.md (Audio).
    /// </summary>
    public static class PaOptions
    {
        public const string Trace = "-portAudioTrace";                       // <file.jsonl>
        public const string Record = "-portAudioRecord";                     // <file.wav>
        public const string Root = "-portAudioRoot";                         // <dir>
        public const string VoiceLanguage = "-portVoiceLanguage";            // <name or key>
        public const string Off = "-portAudioOff";
        public const string SuspendOnFocusLoss = "-portAudioSuspendOnFocusLoss";
        public const string Voices = "-portAudioVoices";                     // <n> real voices
        public const string TrimDb = "-portAudioTrimDb";                     // <dB> master trim
        public const string Seed = "-portAudioSeed";                         // <n> random seed

        public const int MinVoices = 1, MaxVoices = 4095;
        public const float MinTrimDb = -96f, MaxTrimDb = 24f;

        /// <summary>Parses and sends warnings to <see cref="PaLog.Warn"/>.</summary>
        public static PaSettings Parse(string[] args)
        {
            var warnings = new List<string>();
            PaSettings s = Parse(args, warnings);
            foreach (string w in warnings)
                PaLog.Warn(w);
            return s;
        }

        /// <summary>Parses; warnings are appended to <paramref name="warnings"/> (may be null).</summary>
        public static PaSettings Parse(string[] args, List<string> warnings)
        {
            var s = new PaSettings();
            if (args == null)
                return s;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a) || a[0] != '-')
                    continue;
                if (Is(a, Off)) { s.Off = true; continue; }
                if (Is(a, SuspendOnFocusLoss)) { s.SuspendOnFocusLoss = true; continue; }

                bool path = Is(a, Trace) || Is(a, Record) || Is(a, Root);
                bool text = Is(a, VoiceLanguage);
                bool number = Is(a, Voices) || Is(a, TrimDb) || Is(a, Seed);
                if (!path && !text && !number)
                    continue;

                string value = i + 1 < args.Length ? args[i + 1] : null;
                // a path or name never starts with '-' and a number never starts with '-<letter>':
                // that is the next flag, so the value is missing (and the flag is not swallowed)
                if (value == null || value.Trim().Length == 0 || LooksLikeFlag(value, number))
                {
                    Warn(warnings, a + ": missing value, ignored");
                    continue;
                }
                i++;
                value = value.Trim();

                if (path)
                {
                    string full;
                    try { full = Path.GetFullPath(value); }
                    catch (Exception e) { Warn(warnings, a + ": bad path '" + value + "' (" + e.Message + "), ignored"); continue; }
                    if (Is(a, Trace)) s.TracePath = full;
                    else if (Is(a, Record)) s.RecordPath = full;
                    else s.Root = full;
                }
                else if (text)
                {
                    s.VoiceLanguage = value;
                }
                else if (Is(a, Voices))
                {
                    int n;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= MinVoices && n <= MaxVoices)
                        s.RealVoices = n;
                    else
                        Warn(warnings, a + ": expected an integer " + MinVoices + ".." + MaxVoices + ", got '" + value + "'; keeping " + s.RealVoices);
                }
                else if (Is(a, TrimDb))
                {
                    float db;
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out db) && !float.IsNaN(db)
                        && db >= MinTrimDb && db <= MaxTrimDb)
                        s.TrimDb = db;
                    else
                        Warn(warnings, a + ": expected dB " + MinTrimDb.ToString(CultureInfo.InvariantCulture) + ".."
                             + MaxTrimDb.ToString(CultureInfo.InvariantCulture) + ", got '" + value + "'; keeping "
                             + s.TrimDb.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    uint seed;
                    if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                        s.Seed = seed;
                    else
                        Warn(warnings, a + ": expected an integer 0.." + uint.MaxValue + ", got '" + value + "'; keeping " + s.Seed);
                }
            }
            return s;
        }

        static bool Is(string arg, string flag)
        {
            return string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);
        }

        static bool LooksLikeFlag(string value, bool number)
        {
            if (value.Length == 0 || value[0] != '-')
                return false;
            return !number || (value.Length > 1 && char.IsLetter(value[1]));
        }

        static void Warn(List<string> warnings, string msg)
        {
            if (warnings != null)
                warnings.Add("options: " + msg);
        }
    }
}
