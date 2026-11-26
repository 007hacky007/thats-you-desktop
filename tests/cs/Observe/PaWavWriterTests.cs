using System;
using System.Collections.Generic;
using System.IO;

namespace PortAudio.Tests
{
    // PaWavWriter and PaLevelMeter. Each [PaTest] method is one test; a failure throws.
    public static class PaWavWriterTests
    {
        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("check failed: " + what);
        }

        static void Near(double expected, double actual, double tol, string what)
        {
            if (Math.Abs(expected - actual) > tol) throw new Exception(what + ": expected " + expected + " got " + actual);
        }

        static uint U32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }
        static int U16(byte[] b, int o) { return b[o] | b[o + 1] << 8; }
        static string Tag(byte[] b, int o) { return new string(new[] { (char)b[o], (char)b[o + 1], (char)b[o + 2], (char)b[o + 3] }); }

        static void CheckHeader(byte[] b, int rate, int channels, uint dataBytes)
        {
            Check(b.Length >= 44, "header present");
            Check(Tag(b, 0) == "RIFF" && Tag(b, 8) == "WAVE" && Tag(b, 12) == "fmt " && Tag(b, 36) == "data", "chunk tags");
            Check(U32(b, 4) == 36 + dataBytes, "riff size " + U32(b, 4));
            Check(U32(b, 16) == 16 && U16(b, 20) == 1, "PCM fmt");
            Check(U16(b, 22) == channels && U32(b, 24) == rate, "channels and rate");
            Check(U32(b, 28) == rate * channels * 2 && U16(b, 32) == channels * 2 && U16(b, 34) == 16, "byte rate, align, bits");
            Check(U32(b, 40) == dataBytes, "data size " + U32(b, 40) + " expected " + dataBytes);
            Check(b.Length >= 44 + dataBytes, "declared data exists");
        }

        [PaTest]
        public static void EmptyFileIsValid()
        {
            var ms = new MemoryStream();
            var w = new PaWavWriter(ms, 48000, 2, false);
            CheckHeader(ms.ToArray(), 48000, 2, 0);
            w.Close();
            w.Close();
            CheckHeader(ms.ToArray(), 48000, 2, 0);
        }

        [PaTest]
        public static void HeaderPatchedEverySecondDuringPartialWrites()
        {
            var ms = new MemoryStream();
            var w = new PaWavWriter(ms, 1000, 2, false);
            var buf = new float[300];                                  // 150 frames per call
            for (int i = 0; i < buf.Length; i++) buf[i] = 0.5f;
            for (int i = 0; i < 6; i++) w.Write(buf, 0, buf.Length);   // 900 frames: < 1 s
            CheckHeader(ms.ToArray(), 1000, 2, 0);
            w.Write(buf, 0, buf.Length);                               // 1050 frames: patched at 1000
            byte[] b = ms.ToArray();
            CheckHeader(b, 1000, 2, 4000);
            Check(b.Length == 44 + 1050 * 4, "all data written");
            for (int i = 0; i < 7; i++) w.Write(buf, 0, buf.Length);   // 2100 frames
            CheckHeader(ms.ToArray(), 1000, 2, 8000);
            w.Close();
            CheckHeader(ms.ToArray(), 1000, 2, 2100 * 4);
            Near(2.1, w.Seconds, 1e-9, "seconds");
        }

        [PaTest]
        public static void FileReadableWhileOpen()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa-wav-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "mix.wav");
            PaWavWriter w = PaWavWriter.Create(path, 100, 1);
            w.Write(new float[250], 0, 250);
            byte[] b;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                b = new byte[fs.Length];
                int got = 0;
                while (got < b.Length) got += fs.Read(b, got, b.Length - got);
            }
            CheckHeader(b, 100, 1, 400);                               // 2 whole seconds patched, flushed
            w.Close();
            CheckHeader(File.ReadAllBytes(path), 100, 1, 500);
            Directory.Delete(dir, true);
        }

        [PaTest]
        public static void SampleConversionAndClipping()
        {
            var ms = new MemoryStream();
            var w = new PaWavWriter(ms, 10, 1, false);
            w.Write(new[] { 0f, 1f, -1f, 2f, -3f, 0.5f, float.NaN }, 0, 7);
            w.Close();
            byte[] b = ms.ToArray();
            short S(int i) { return (short)(b[44 + 2 * i] | b[45 + 2 * i] << 8); }
            Check(S(0) == 0 && S(1) == 32767 && S(2) == -32768 && S(3) == 32767 && S(4) == -32768, "clamp");
            Check(S(5) == 16384 && S(6) == 0, "scale and NaN");
            Check(w.Clipped == 2, "clipped count " + w.Clipped);
        }

        [PaTest]
        public static void CloseDropsPartialFrame()
        {
            var ms = new MemoryStream();
            var w = new PaWavWriter(ms, 10, 2, false);
            w.Write(new float[5], 0, 5);
            w.Close();
            byte[] b = ms.ToArray();
            CheckHeader(b, 10, 2, 8);
            Check(b.Length == 52, "partial frame trimmed");
        }

        [PaTest]
        public static void MeterMath()
        {
            Check(PaLevelMeter.ToDb(0) == PaLevelMeter.SilentDb, "silence");
            Near(0, PaLevelMeter.ToDb(1), 1e-6, "full scale");
            Near(-6.0206, PaLevelMeter.ToDb(0.5), 1e-3, "half");

            var m = new PaLevelMeter(100, 2);
            var levels = new List<PaLevel>();
            var sq = new float[150 * 2];                               // square wave +-0.5, 1.5 s
            for (int i = 0; i < sq.Length; i++) sq[i] = (i / 2) % 2 == 0 ? 0.5f : -0.5f;
            m.Feed(sq, 0, 70, levels);
            m.Feed(sq, 70, sq.Length - 70, levels);
            Check(levels.Count == 1, "one full window");
            Near(-6.0206, levels[0].RmsDb, 1e-3, "square rms");
            Near(-6.0206, levels[0].PeakDb, 1e-3, "square peak");
            Check(levels[0].Frames == 100 && levels[0].StartSec == 0, "window 0");
            m.Finish(levels);
            Check(levels.Count == 2 && levels[1].Frames == 50 && levels[1].Index == 1, "partial window");
            Near(1.0, levels[1].StartSec, 1e-9, "start sec");

            var sine = new PaLevelMeter(1000, 1);
            var s = new float[1000];
            for (int i = 0; i < s.Length; i++) s[i] = (float)(0.25 * Math.Sin(2 * Math.PI * 10 * i / 1000.0));
            var l2 = new List<PaLevel>();
            sine.Feed(s, 0, s.Length, l2);
            Near(20 * Math.Log10(0.25 / Math.Sqrt(2)), l2[0].RmsDb, 1e-2, "sine rms");
            Near(20 * Math.Log10(0.25), l2[0].PeakDb, 1e-2, "sine peak");

            var z = new PaLevelMeter(10, 1);
            var l3 = new List<PaLevel>();
            z.Feed(new float[10], 0, 10, l3);
            Check(l3[0].RmsDb == PaLevelMeter.SilentDb && l3[0].PeakDb == PaLevelMeter.SilentDb, "zero window");
        }
    }
}
