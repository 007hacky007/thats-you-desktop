using System;
using System.Collections.Generic;
using System.IO;

namespace PortAudio.Tests
{
    // Each [PaTest] method is one test; a failure throws.
    public static class PaOptionsTests
    {
        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("check failed: " + what);
        }

        [PaTest]
        public static void Defaults()
        {
            var w = new List<string>();
            PaSettings s = PaOptions.Parse(new[] { "/app/exe", "-logFile", "x.log" }, w);
            Check(s.TracePath == null && s.RecordPath == null && s.Root == null && s.VoiceLanguage == null, "null paths");
            Check(!s.Off && !s.SuspendOnFocusLoss, "bools off");
            Check(s.RealVoices == 128 && s.TrimDb == 0f && s.Seed == -1, "numeric defaults");
            Check(w.Count == 0, "no warnings");
            Check(PaOptions.Parse(null, w) != null, "null args");
        }

        [PaTest]
        public static void AllNineFlagsCaseInsensitive()
        {
            var w = new List<string>();
            PaSettings s = PaOptions.Parse(new[] {
                "-PORTAUDIOTRACE", "run/audio.jsonl", "-portaudiorecord", "run/mix.wav", "-portAudioRoot", "data",
                "-portvoicelanguage", "English(US)", "-portAudioOFF", "-portAudioSuspendOnFocusLoss",
                "-portAudioVoices", "64", "-portAudioTrimDb", "-6.5", "-portAudioSeed", "4294967295" }, w);
            Check(w.Count == 0, "no warnings: " + string.Join("; ", w));
            Check(s.TracePath == Path.GetFullPath("run/audio.jsonl"), "trace absolute");
            Check(Path.IsPathRooted(s.TracePath), "trace rooted");
            Check(s.RecordPath == Path.GetFullPath("run/mix.wav"), "record absolute");
            Check(s.Root == Path.GetFullPath("data"), "root absolute");
            Check(s.VoiceLanguage == "English(US)", "voice language kept verbatim");
            Check(s.Off && s.SuspendOnFocusLoss, "bools");
            Check(s.RealVoices == 64, "voices");
            Check(s.TrimDb == -6.5f, "trim");
            Check(s.Seed == 4294967295L, "seed");
        }

        [PaTest]
        public static void BadValuesWarnAndKeepDefaults()
        {
            var w = new List<string>();
            PaSettings s = PaOptions.Parse(new[] {
                "-portAudioVoices", "lots", "-portAudioTrimDb", "1e9", "-portAudioSeed", "-3",
                "-portAudioTrimDb", "NaN" }, w);
            Check(s.RealVoices == 128 && s.TrimDb == 0f && s.Seed == -1, "defaults kept");
            Check(w.Count == 4, "four warnings, got " + w.Count);
            s = PaOptions.Parse(new[] { "-portAudioVoices", "0" }, w);
            Check(s.RealVoices == 128, "voices 0 rejected");
        }

        [PaTest]
        public static void MissingValueDoesNotSwallowNextFlag()
        {
            var w = new List<string>();
            PaSettings s = PaOptions.Parse(new[] { "-portAudioTrace", "-portAudioOff", "-portAudioVoices", "-portAudioSuspendOnFocusLoss", "-portAudioRoot" }, w);
            Check(s.TracePath == null, "trace not set to a flag");
            Check(s.Off, "next flag still parsed");
            Check(s.SuspendOnFocusLoss, "flag after numeric flag still parsed");
            Check(s.RealVoices == 128 && s.Root == null, "defaults");
            Check(w.Count == 3, "three warnings, got " + w.Count);
        }

        [PaTest]
        public static void LastOccurrenceWins()
        {
            PaSettings s = PaOptions.Parse(new[] { "-portAudioVoices", "16", "-portAudioVoices", "32", "-portVoiceLanguage", " German " }, null);
            Check(s.RealVoices == 32, "last wins");
            Check(s.VoiceLanguage == "German", "trimmed");
        }
    }
}
