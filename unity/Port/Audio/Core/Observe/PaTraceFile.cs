using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PortAudio
{
    /// <summary>
    /// JSON-lines trace sink (<c>-portAudioTrace</c>). <see cref="Write"/> formats the line on the
    /// calling thread (any thread) and queues it; a background thread appends the queue to the file
    /// every 0.5 s. Each line: <c>{"t":12.345,"f":678,"kind":"post","id":123,"name":"Play_X"}</c>,
    /// t = realtime seconds, f = frame count (from PaRuntime.Platform). Flush and Close may be called
    /// more than once; writes after Close are dropped.
    /// </summary>
    public sealed class PaTraceFile : IPaTraceSink
    {
        public const int FlushIntervalMs = 500;
        public const int MaxPending = 200000;

        readonly ConcurrentQueue<string> m_Queue = new ConcurrentQueue<string>();
        readonly object m_FileLock = new object();
        readonly AutoResetEvent m_Wake = new AutoResetEvent(false);
        readonly Stopwatch m_Clock = Stopwatch.StartNew();
        readonly string m_Path;
        StreamWriter m_Writer;
        Thread m_Thread;
        int m_Pending;
        long m_Dropped;
        volatile bool m_Closed;

        // last main-thread reading of the platform clock; other threads extrapolate from it
        readonly object m_TimeLock = new object();
        double m_BaseT;
        int m_BaseF;
        double m_BaseClock;
        bool m_HaveBase;

        public string Path { get { return m_Path; } }
        public long Dropped { get { return Interlocked.Read(ref m_Dropped); } }

        PaTraceFile(string path, StreamWriter writer)
        {
            m_Path = path;
            m_Writer = writer;
        }

        /// <summary>Creates (truncates) the file, creating its directory, and starts the writer thread.</summary>
        public static PaTraceFile Open(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            string dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var t = new PaTraceFile(full, new StreamWriter(stream, new UTF8Encoding(false)));
            t.m_Thread = new Thread(t.Loop) { IsBackground = true, Name = "PortAudio trace" };
            t.m_Thread.Start();
            return t;
        }

        public void Write(string kind, object[] keyValues)
        {
            if (m_Closed)
                return;
            if (Interlocked.Increment(ref m_Pending) > MaxPending)
            {
                Interlocked.Decrement(ref m_Pending);
                Interlocked.Increment(ref m_Dropped);
                return;
            }
            double t;
            int f;
            Now(out t, out f);
            m_Queue.Enqueue(FormatLine(t, f, kind, keyValues));
        }

        public void Flush()
        {
            Drain(true);
        }

        public void Close()
        {
            if (m_Closed)
                return;
            m_Closed = true;
            m_Wake.Set();
            Thread th = m_Thread;
            if (th != null && th != Thread.CurrentThread)
                th.Join(2000);
            lock (m_FileLock)
            {
                DrainLocked(true);
                if (m_Writer != null)
                {
                    try { m_Writer.Dispose(); } catch (Exception) { }
                    m_Writer = null;
                }
            }
        }

        void Loop()
        {
            while (!m_Closed)
            {
                m_Wake.WaitOne(FlushIntervalMs);
                Drain(true);
            }
        }

        void Drain(bool flush)
        {
            lock (m_FileLock)
                DrainLocked(flush);
        }

        void DrainLocked(bool flush)
        {
            if (m_Writer == null)
                return;
            try
            {
                long dropped = Interlocked.Exchange(ref m_Dropped, 0);
                if (dropped > 0)
                {
                    double t; int f;
                    Now(out t, out f);
                    m_Writer.Write(FormatLine(t, f, "traceDropped", new object[] { "count", dropped }));
                    m_Writer.Write('\n');
                }
                string line;
                while (m_Queue.TryDequeue(out line))
                {
                    Interlocked.Decrement(ref m_Pending);
                    m_Writer.Write(line);
                    m_Writer.Write('\n');
                }
                if (flush)
                    m_Writer.Flush();
            }
            catch (Exception)
            {
                // disk full or file gone: tracing must never break audio
            }
        }

        void Now(out double t, out int f)
        {
            IPaPlatform p = PaRuntime.Platform;
            double clock = m_Clock.Elapsed.TotalSeconds;
            if (p != null)
            {
                bool main = false;
                try { main = p.IsMainThread; } catch (Exception) { }
                if (main)
                {
                    try
                    {
                        t = p.RealtimeSeconds;
                        f = p.FrameCount;
                        lock (m_TimeLock)
                        {
                            m_BaseT = t; m_BaseF = f; m_BaseClock = clock; m_HaveBase = true;
                        }
                        return;
                    }
                    catch (Exception) { }
                }
            }
            lock (m_TimeLock)
            {
                if (m_HaveBase)
                {
                    t = m_BaseT + (clock - m_BaseClock);
                    f = m_BaseF;
                    return;
                }
            }
            t = clock;
            f = 0;
        }

        // ---- minimal JSON writer ----

        /// <summary>One trace line without the newline. Keys and values alternate; a trailing key gets null.</summary>
        public static string FormatLine(double t, int frame, string kind, object[] keyValues)
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"t\":");
            AppendNumber(sb, Math.Round(t, 3));
            sb.Append(",\"f\":").Append(frame.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"kind\":");
            AppendString(sb, kind ?? "");
            if (keyValues != null)
            {
                for (int i = 0; i < keyValues.Length; i += 2)
                {
                    sb.Append(',');
                    object k = keyValues[i];
                    AppendString(sb, k == null ? "" : Convert.ToString(k, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    AppendValue(sb, i + 1 < keyValues.Length ? keyValues[i + 1] : null, 0);
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static void AppendValue(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { AppendString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is uint || v is long || v is ulong || v is short || v is ushort || v is byte || v is sbyte)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            if (v is float) { AppendNumber(sb, (float)v); return; }
            if (v is double) { AppendNumber(sb, (double)v); return; }
            if (v is decimal) { sb.Append(((decimal)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is Enum) { AppendString(sb, v.ToString()); return; }
            if (v is IEnumerable && depth < 4)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in (IEnumerable)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            AppendString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        /// <summary>Finite numbers in invariant round-trip form; NaN and infinities become null (not valid JSON numbers).</summary>
        public static void AppendNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void AppendNumber(StringBuilder sb, float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append("null"); return; }
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029 || char.IsSurrogate(c) && !ValidSurrogate(s, i))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static bool ValidSurrogate(string s, int i)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c))
                return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            return i > 0 && char.IsHighSurrogate(s[i - 1]);
        }
    }
}
