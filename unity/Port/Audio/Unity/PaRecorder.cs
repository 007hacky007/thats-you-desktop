using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PortAudio.Unity
{
    /// <summary>
    /// Records the final mix to a PCM16 WAV (<c>-portAudioRecord</c>). <see cref="Start"/> creates a hidden
    /// DontDestroyOnLoad host that, once a second, finds the enabled AudioListener and attaches (or moves)
    /// a <see cref="PaRecorderTap"/> there. The tap's OnAudioFilterRead copies the mix into a preallocated
    /// single-producer ring; a writer thread drains it into <see cref="PaWavWriter"/> (rate
    /// AudioSettings.outputSampleRate, the callback's channel count) and emits one <c>level</c> trace
    /// event per second. Start and Stop are main-thread only.
    /// </summary>
    public static class PaRecorder
    {
        public const float ListenerCheckSeconds = 1f;
        public const int RingSeconds = 4;
        public const int MaxChannels = 8;

        static PaRecorderHost s_Host;
        static volatile Session s_Session;             // read by the audio thread

        public static bool Recording { get { return s_Session != null; } }
        public static string Path { get { Session s = s_Session; return s != null ? s.Path : null; } }

        internal static Session Current { get { return s_Session; } }

        /// <summary>Starts recording to <paramref name="path"/> (replacing any recording in progress).</summary>
        public static void Start(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            Stop();
            int rate = AudioSettings.outputSampleRate;
            if (rate <= 0)
                rate = 48000;
            var session = new Session(System.IO.Path.GetFullPath(path), rate);
            s_Session = session;
            var go = new GameObject("PortAudio.Recorder");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_Host = go.AddComponent<PaRecorderHost>();
            session.StartWriter();
            PaLog.Trace("record", "status", "start", "path", session.Path, "rate", rate);
            PaLog.Info("recording the mix to " + session.Path + " at " + rate + " Hz");
            s_Host.AttachToListener();
        }

        /// <summary>Detaches the tap, drains the ring and finalizes the WAV. Safe to call twice.</summary>
        public static void Stop()
        {
            Session session = s_Session;
            PaRecorderHost host = s_Host;
            s_Session = null;
            s_Host = null;
            if (host != null)
            {
                host.Detach();
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(host.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
            if (session != null)
                session.Finish();
        }

        /// <summary>One recording: the ring the audio thread fills and the thread that writes the file.</summary>
        internal sealed class Session
        {
            public readonly string Path;
            public readonly int Rate;
            readonly float[] m_Ring;
            readonly int m_Mask;
            long m_WritePos, m_ReadPos;              // samples; written by one thread each
            int m_Channels;                          // set by the first callback
            long m_Overruns, m_Mismatched, m_Callbacks;
            volatile bool m_Stopping;
            Thread m_Thread;
            PaWavWriter m_Wav;
            PaLevelMeter m_Meter;
            readonly List<PaLevel> m_Levels = new List<PaLevel>();
            string m_Error;

            public Session(string path, int rate)
            {
                Path = path;
                Rate = rate;
                int want = rate * MaxChannels * RingSeconds;
                int cap = 1;
                while (cap < want) cap <<= 1;
                m_Ring = new float[cap];
                m_Mask = cap - 1;
            }

            public void StartWriter()
            {
                m_Thread = new Thread(WriterLoop) { IsBackground = true, Name = "PortAudio recorder" };
                m_Thread.Start();
            }

            /// <summary>Audio thread. No allocation, no locks.</summary>
            public void Push(float[] data, int channels)
            {
                if (m_Stopping || data == null || channels <= 0 || channels > MaxChannels)
                    return;
                Interlocked.Increment(ref m_Callbacks);
                int known = Volatile.Read(ref m_Channels);
                if (known == 0)
                {
                    Interlocked.CompareExchange(ref m_Channels, channels, 0);
                    known = Volatile.Read(ref m_Channels);
                }
                if (known != channels)
                {
                    Interlocked.Increment(ref m_Mismatched);
                    return;
                }
                int n = data.Length - data.Length % channels;
                long w = m_WritePos;
                long r = Volatile.Read(ref m_ReadPos);
                if (n > m_Ring.Length - (w - r))
                {
                    Interlocked.Increment(ref m_Overruns);
                    return;
                }
                int start = (int)(w & m_Mask);
                int first = Math.Min(n, m_Ring.Length - start);
                Array.Copy(data, 0, m_Ring, start, first);
                if (first < n)
                    Array.Copy(data, first, m_Ring, 0, n - first);
                Volatile.Write(ref m_WritePos, w + n);
            }

            int Pop(float[] dst, int channels)
            {
                long r = m_ReadPos;
                long avail = Volatile.Read(ref m_WritePos) - r;
                int max = dst.Length - dst.Length % channels;
                int n = (int)Math.Min(avail, max);
                n -= n % channels;
                if (n <= 0)
                    return 0;
                int start = (int)(r & m_Mask);
                int first = Math.Min(n, m_Ring.Length - start);
                Array.Copy(m_Ring, start, dst, 0, first);
                if (first < n)
                    Array.Copy(m_Ring, 0, dst, first, n - first);
                Volatile.Write(ref m_ReadPos, r + n);
                return n;
            }

            void WriterLoop()
            {
                var scratch = new float[16384];
                while (true)
                {
                    bool stopping = m_Stopping;
                    int got = 0;
                    try
                    {
                        got = Drain(scratch);
                    }
                    catch (Exception e)
                    {
                        if (m_Error == null)
                        {
                            m_Error = e.GetType().Name + ": " + e.Message;
                            PaLog.Warn("recorder: " + m_Error + "; recording stopped");
                        }
                        m_Stopping = true;
                        return;
                    }
                    if (stopping && got == 0)
                        return;
                    if (got == 0)
                        Thread.Sleep(20);
                }
            }

            int Drain(float[] scratch)
            {
                int channels = Volatile.Read(ref m_Channels);
                if (channels == 0)
                    return 0;
                if (m_Wav == null)
                {
                    m_Wav = PaWavWriter.Create(Path, Rate, channels);
                    m_Meter = new PaLevelMeter(Rate, channels);
                    PaLog.Trace("record", "status", "data", "channels", channels, "rate", Rate);
                }
                int total = 0, n;
                while ((n = Pop(scratch, channels)) > 0)
                {
                    m_Wav.Write(scratch, 0, n);
                    m_Meter.Feed(scratch, 0, n, m_Levels);
                    total += n;
                    EmitLevels();
                }
                return total;
            }

            void EmitLevels()
            {
                for (int i = 0; i < m_Levels.Count; i++)
                {
                    PaLevel l = m_Levels[i];
                    PaLog.Trace("level", "sec", l.StartSec, "rmsDb", Math.Round(l.RmsDb, 1), "peakDb", Math.Round(l.PeakDb, 1),
                        "frames", l.Frames);
                }
                m_Levels.Clear();
            }

            /// <summary>Main thread: stop accepting audio, let the writer drain, close the file.</summary>
            public void Finish()
            {
                m_Stopping = true;
                Thread th = m_Thread;
                if (th != null && !th.Join(3000))
                {
                    // the header was patched at the last whole second, so the file is still valid
                    PaLog.Warn("recorder: writer thread did not finish in 3 s; " + Path + " left as last patched");
                    return;
                }
                double seconds = 0;
                long clipped = 0;
                try
                {
                    if (m_Wav != null)
                    {
                        if (m_Meter != null)
                        {
                            m_Meter.Finish(m_Levels);
                            EmitLevels();
                        }
                        seconds = m_Wav.Seconds;
                        clipped = m_Wav.Clipped;
                        m_Wav.Close();
                    }
                }
                catch (Exception e)
                {
                    PaLog.Warn("recorder: closing " + Path + " failed: " + e.Message);
                }
                long callbacks = Interlocked.Read(ref m_Callbacks);
                PaLog.Trace("record", "status", m_Wav != null ? "stop" : "noData", "path", Path, "seconds", Math.Round(seconds, 3),
                    "callbacks", callbacks, "overruns", Interlocked.Read(ref m_Overruns),
                    "mismatched", Interlocked.Read(ref m_Mismatched), "clipped", clipped, "error", m_Error);
                if (m_Wav == null)
                    PaLog.Warn("recorder: no audio reached the AudioListener (Unity audio disabled, or no enabled listener); "
                               + Path + " was not written");
                else
                    PaLog.Info("recorder: wrote " + seconds.ToString("0.0") + " s to " + Path);
            }
        }
    }

    /// <summary>Hidden host: keeps the tap on the enabled AudioListener and stops the recording on quit.</summary>
    [AddComponentMenu("")]
    public sealed class PaRecorderHost : MonoBehaviour
    {
        PaRecorderTap m_Tap;
        float m_NextCheck;

        void Update()
        {
            if (Time.unscaledTime < m_NextCheck)
                return;
            m_NextCheck = Time.unscaledTime + PaRecorder.ListenerCheckSeconds;
            AttachToListener();
        }

        internal void AttachToListener()
        {
            AudioListener listener = FindListener();
            if (listener == null)
                return;                                                  // keep the old tap until a listener appears
            if (m_Tap != null && m_Tap.gameObject == listener.gameObject)
                return;
            Detach();
            m_Tap = listener.gameObject.AddComponent<PaRecorderTap>();
            m_Tap.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            PaLog.Trace("record", "status", "listener", "name", listener.gameObject.name, "scene", listener.gameObject.scene.name);
        }

        internal void Detach()
        {
            if (m_Tap != null)
            {
                if (Application.isPlaying)
                    Destroy(m_Tap);
                else
                    DestroyImmediate(m_Tap);
            }
            m_Tap = null;
        }

        static AudioListener FindListener()
        {
            AudioListener[] all = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].isActiveAndEnabled)
                    return all[i];
            return null;
        }

        void OnApplicationQuit()
        {
            PaRecorder.Stop();
        }
    }

    /// <summary>Lives on the AudioListener's GameObject; passes the final mix through unchanged.</summary>
    [AddComponentMenu("")]
    public sealed class PaRecorderTap : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels)
        {
            PaRecorder.Session s = PaRecorder.Current;
            if (s != null)
                s.Push(data, channels);
        }
    }
}
