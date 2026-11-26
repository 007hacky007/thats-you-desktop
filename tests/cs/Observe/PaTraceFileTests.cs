using System;
using System.IO;
using System.Text;
using System.Threading;

namespace PortAudio.Tests
{
    public static class PaTraceFileTests
    {
        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("check failed: " + what);
        }

        static void Same(string expected, string actual)
        {
            if (expected != actual) throw new Exception("expected <" + expected + "> got <" + actual + ">");
        }

        static string TempFile(string name)
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa-observe-tests-" + Guid.NewGuid().ToString("N"));
            return Path.Combine(dir, "sub", name);
        }

        public static void LineFormat()
        {
            Same("{\"t\":1.234,\"f\":56,\"kind\":\"post\",\"id\":123,\"name\":\"Play_X\",\"ok\":true}",
                PaTraceFile.FormatLine(1.2344, 56, "post", new object[] { "id", 123u, "name", "Play_X", "ok", true }));
            Same("{\"t\":0,\"f\":0,\"kind\":\"k\"}", PaTraceFile.FormatLine(0, 0, "k", null));
        }

        public static void ValueTypes()
        {
            Same("{\"t\":0,\"f\":0,\"kind\":\"v\",\"a\":-1.5,\"b\":null,\"c\":null,\"d\":18446744073709551615,\"e\":\"Linear\",\"g\":[1,\"x\",null],\"h\":0.25,\"z\":null}",
                PaTraceFile.FormatLine(0, 0, "v", new object[] {
                    "a", -1.5, "b", double.NaN, "c", float.PositiveInfinity, "d", ulong.MaxValue,
                    "e", PaTestEnum.Linear, "g", new object[] { 1, "x", null }, "h", 0.25f, "z" }));
        }

        enum PaTestEnum { Log3, Linear }

        public static void Escaping()
        {
            string ls = ((char)0x2028).ToString(), e = ((char)0xE9).ToString();
            Same("{\"t\":0,\"f\":0,\"kind\":\"we\\\"ird\",\"k\\\\ey\":\"a\\\"b\\\\c\\nd\\re\\tf\\u0001g\\u2028h" + e + "\"}",
                PaTraceFile.FormatLine(0, 0, "we\"ird", new object[] { "k\\ey", "a\"b\\c\nd\re\tf" + (char)1 + "g" + ls + "h" + e }));
            string pair = new string(new[] { (char)0xD83D, (char)0xDE00 });
            var sb = new StringBuilder();
            PaTraceFile.AppendString(sb, "x" + (char)0xD800 + "y" + pair);
            Same("\"x\\ud800y" + pair + "\"", sb.ToString());
        }

        public static void WritesFlushesAndClosesTwice()
        {
            string path = TempFile("audio.jsonl");
            PaTraceFile t = PaTraceFile.Open(path);
            t.Write("post", new object[] { "id", 1 });
            t.Write("unknown", new object[] { "name", "Play_Y" });
            t.Flush();
            t.Flush();
            string[] lines = ReadLines(path);
            Check(lines.Length == 2, "two lines after flush, got " + lines.Length);
            Check(lines[0].StartsWith("{\"t\":", StringComparison.Ordinal) && lines[0].EndsWith(",\"kind\":\"post\",\"id\":1}", StringComparison.Ordinal), lines[0]);
            t.Close();
            t.Close();
            t.Write("late", null);
            t.Flush();
            Check(ReadLines(path).Length == 2, "write after close dropped");
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)), true);
        }

        public static void BackgroundFlush()
        {
            string path = TempFile("bg.jsonl");
            PaTraceFile t = PaTraceFile.Open(path);
            t.Write("level", new object[] { "sec", 0 });
            bool seen = false;
            for (int i = 0; i < 40 && !seen; i++)
            {
                Thread.Sleep(50);
                seen = ReadLines(path).Length == 1;
            }
            Check(seen, "written by the background thread within 2 s");
            t.Close();
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)), true);
        }

        public static void ManyThreads()
        {
            string path = TempFile("mt.jsonl");
            PaTraceFile t = PaTraceFile.Open(path);
            var threads = new Thread[8];
            for (int n = 0; n < threads.Length; n++)
            {
                int id = n;
                threads[n] = new Thread(() => { for (int i = 0; i < 2000; i++) t.Write("x", new object[] { "th", id, "i", i }); });
                threads[n].Start();
            }
            foreach (Thread th in threads) th.Join();
            t.Close();
            string[] lines = ReadLines(path);
            Check(lines.Length == 16000, "all lines, got " + lines.Length);
            foreach (string l in lines)
                Check(l.StartsWith("{", StringComparison.Ordinal) && l.EndsWith("}", StringComparison.Ordinal), "whole line: " + l);
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)), true);
        }

        static string[] ReadLines(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new StreamReader(fs))
            {
                string all = r.ReadToEnd();
                return all.Length == 0 ? new string[0] : all.TrimEnd('\n').Split('\n');
            }
        }
    }
}
