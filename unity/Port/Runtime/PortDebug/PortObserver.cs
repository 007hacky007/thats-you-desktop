// Observation hooks for automated runs of the player. Inert unless the player is started with
//   -portObserve <dir>          screenshots + event log go to <dir>
//   -portKeys "<t>:<key>,..."   optional scripted input (see PortKeyScript.cs); a "<t>:shot"
//                               entry presses nothing and saves a screenshot (reason "shot") at t
//   -portShotInterval <sec>     timer screenshot interval (default 2 s, at least 0.1 s)
//   -portKeyShots               also save a screenshot (reason "key") with each key probe
//
// When active it:
//   * logs "[PortObserve] scene loaded: <name> (index <n>, <mode>) t=<s>" for every scene load;
//   * saves <dir>/<seq>_<scene>_<reason>.png every 2 s or -portShotInterval (reason "timer") and on every scene
//     load (reason "scene") through ScreenCapture.CaptureScreenshot;
//   * appends one JSON object per line to <dir>/log.jsonl: {"t","kind","type","msg"} for scene
//     loads, screenshots, key presses and every Error / Assert / Exception the player logs;
//   * appends a "probe" event with the game's UI state (PortStateProbe.cs: Menu, MenuDisplay,
//     FrontEndFlow, IWYKLogo, PauseGame and the visible NGUI label texts) on every scene load,
//     with every timer screenshot and 0.5 s after each scripted key is released, and logs a
//     "[PortObserve] probe (<reason>): <digest>" line when the digest changes;
//   * plays the -portKeys schedule into PortInput (merged into Input by InputShim.cs);
//   * hides the development build's on-screen error console so it does not cover screenshots.
// Installed with [RuntimeInitializeOnLoadMethod], so no scene is modified.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PortDebug
{
    [DefaultExecutionOrder(-32000)]
    public sealed class PortObserver : MonoBehaviour
    {
        const float k_ShotInterval = 2f;
        const float k_MinShotInterval = 0.1f;
        const float k_KeyProbeDelay = 0.5f;
        const string k_ShotKey = "shot";

        static PortObserver s_Instance;

        string m_Dir;
        int m_Seq;
        float m_NextShot;
        float m_ShotInterval = k_ShotInterval;
        bool m_KeyShots;
        readonly List<float> m_ShotTimes = new List<float>();
        // one capture request per frame; later ones in the same frame wait for the next frames
        readonly List<KeyValuePair<string, string>> m_DeferredShots = new List<KeyValuePair<string, string>>();
        int m_LastShotFrame = -1;
        List<PortKeyEvent> m_Keys = new List<PortKeyEvent>();
        StreamWriter m_Events;
        readonly object m_Lock = new object();
        readonly System.Diagnostics.Stopwatch m_Clock = System.Diagnostics.Stopwatch.StartNew();
        double m_T0;
        readonly List<KeyValuePair<float, string>> m_PendingProbes = new List<KeyValuePair<float, string>>();
        string m_LastSummary;

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            string dir = Arg(args, "-portObserve");
            if (string.IsNullOrEmpty(dir) || s_Instance != null)
                return;
            var go = new GameObject("PortObserver");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<PortObserver>();
            s_Instance.m_KeyShots = HasFlag(args, "-portKeyShots");
            string interval = Arg(args, "-portShotInterval");
            if (interval != null)
            {
                float v;
                if (float.TryParse(interval, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f)
                    s_Instance.m_ShotInterval = Mathf.Max(k_MinShotInterval, v);
                else
                    Debug.LogWarning("[PortObserve] bad -portShotInterval: " + interval);
            }
            s_Instance.Init(Path.GetFullPath(dir), Arg(args, "-portKeys"));
        }

        static bool HasFlag(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // Moves the "<t>:shot" entries out of the key list into m_ShotTimes; returns the rest.
        string TakeShotEntries(string keys)
        {
            if (string.IsNullOrEmpty(keys)) return keys;
            var rest = new List<string>();
            foreach (string raw in keys.Split(','))
            {
                string item = raw.Trim();
                int colon = item.IndexOf(':');
                float t;
                if (colon > 0 && string.Equals(item.Substring(colon + 1).Trim(), k_ShotKey, StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(item.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out t))
                    m_ShotTimes.Add(t);
                else if (item.Length > 0)
                    rest.Add(item);
            }
            m_ShotTimes.Sort();
            return string.Join(",", rest.ToArray());
        }

        void Init(string dir, string keys)
        {
            m_Dir = dir;
            m_T0 = Time.realtimeSinceStartup;
            m_Clock.Restart();
            Directory.CreateDirectory(m_Dir);
            try
            {
                m_Events = new StreamWriter(Path.Combine(m_Dir, "log.jsonl"), true, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortObserve] cannot write log.jsonl: " + e.Message);
            }
            // The development build's on-screen error console would cover the screenshots;
            // everything it shows is in player.log and log.jsonl anyway.
            Debug.developerConsoleEnabled = false;
            Application.logMessageReceivedThreaded += OnLog;
            SceneManager.sceneLoaded += OnSceneLoaded;

            var errors = new List<string>();
            m_Keys = PortKeyScript.Parse(TakeShotEntries(keys), errors);
            foreach (string err in errors)
                Debug.LogWarning("[PortObserve] -portKeys: " + err);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                                    "[PortObserve] started: dir={0} keys={1} ({2} events, {3} shots) interval={4:0.###}s keyShots={5}",
                                    m_Dir, keys ?? "-", m_Keys.Count, m_ShotTimes.Count, m_ShotInterval, m_KeyShots));
            Event("start", null, string.Format(CultureInfo.InvariantCulture, "dir={0} keys={1} interval={2:0.###} keyShots={3}",
                                               m_Dir, keys ?? "", m_ShotInterval, m_KeyShots));
            m_NextShot = Time.realtimeSinceStartup + m_ShotInterval;
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            lock (m_Lock)
            {
                if (m_Events != null) m_Events.Dispose();
                m_Events = null;
            }
            if (s_Instance == this) s_Instance = null;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (m_DeferredShots.Count > 0 && Time.frameCount != m_LastShotFrame)
            {
                var d = m_DeferredShots[0];
                m_DeferredShots.RemoveAt(0);
                Shot(d.Key, d.Value);
            }
            if (m_Keys.Count > 0)
                PortKeyScript.Tick(m_Keys, now, OnKey);
            for (int i = 0; i < m_PendingProbes.Count; i++)
            {
                if (now < m_PendingProbes[i].Key) continue;
                string reason = m_PendingProbes[i].Value;
                m_PendingProbes.RemoveAt(i--);
                if (m_KeyShots) Shot("key");
                Probe(reason);
            }
            if (m_ShotTimes.Count > 0 && now >= m_ShotTimes[0])
            {
                // several entries due in one frame give one screenshot
                while (m_ShotTimes.Count > 0 && now >= m_ShotTimes[0])
                    m_ShotTimes.RemoveAt(0);
                Shot(k_ShotKey);
                Probe(k_ShotKey);
            }
            if (now >= m_NextShot)
            {
                m_NextShot = now + m_ShotInterval;
                Shot("timer");
                Probe("timer");
            }
        }

        void OnKey(PortKeyEvent e, bool down)
        {
            string msg = (down ? "key down: " : "key up: ") + e.spec;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PortObserve] {0} t={1:F2} frame={2}",
                                    msg, Time.realtimeSinceStartup, Time.frameCount));
            Event("key", null, msg);
            if (!down)
                m_PendingProbes.Add(new KeyValuePair<float, string>(Time.realtimeSinceStartup + k_KeyProbeDelay, "key " + e.spec));
        }

        // Records the game's UI state; see PortStateProbe.cs for the fields.
        void Probe(string reason)
        {
            string state = PortStateProbe.Capture();
            string summary = PortStateProbe.Summary();
            if (summary != m_LastSummary)
            {
                m_LastSummary = summary;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PortObserve] probe ({0}): {1} t={2:F2}",
                                        reason, summary, Time.realtimeSinceStartup));
            }
            var tail = new StringBuilder(state.Length + 32);
            tail.Append(",\"reason\":\"");
            Escape(tail, reason);
            tail.Append("\",\"state\":").Append(state);
            Event("probe", null, summary, tail.ToString());
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // the first scene of the player arrives with an undocumented mode value
            string m = mode == LoadSceneMode.Single || mode == LoadSceneMode.Additive ? mode.ToString() : "First";
            string desc = string.Format("{0} (index {1}, {2})", scene.name, scene.buildIndex, m);
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PortObserve] scene loaded: {0} t={1:F2}",
                                    desc, Time.realtimeSinceStartup));
            Event("scene", null, desc);
            Shot("scene", scene.name);
            Probe("scene " + scene.name);
        }

        void Shot(string reason, string sceneName = null)
        {
            string scene = sceneName ?? SceneManager.GetActiveScene().name;
            if (Time.frameCount == m_LastShotFrame)
            {
                m_DeferredShots.Add(new KeyValuePair<string, string>(reason, scene));
                return;
            }
            m_LastShotFrame = Time.frameCount;
            string file = string.Format("{0:0000}_{1}_{2}.png", ++m_Seq, Safe(scene), reason);
            string path = Path.Combine(m_Dir, file);
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                Event("shot", null, file);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortObserve] screenshot failed: " + e.Message);
            }
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "none";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            return sb.ToString();
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                string msg = condition;
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    string[] st = stackTrace.Split('\n');
                    msg += "\n" + string.Join("\n", st, 0, Math.Min(4, st.Length));
                }
                Event("log", type.ToString(), msg);
            }
        }

        // rawTail, when given, is appended verbatim before the closing brace (",\"key\":<json>...").
        void Event(string kind, string type, string msg, string rawTail = null)
        {
            // Time.* is main-thread only; the log callback can run on any thread
            double t = m_T0 + m_Clock.Elapsed.TotalSeconds;
            var sb = new StringBuilder(128);
            sb.Append("{\"t\":").Append(t.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(",\"kind\":\"").Append(kind).Append('"');
            if (type != null) sb.Append(",\"type\":\"").Append(type).Append('"');
            sb.Append(",\"msg\":\"");
            Escape(sb, msg ?? "");
            sb.Append('"');
            if (rawTail != null) sb.Append(rawTail);
            sb.Append('}');
            lock (m_Lock)
            {
                if (m_Events == null) return;
                try { m_Events.WriteLine(sb.ToString()); }
                catch (Exception) { }
            }
        }

        internal static void Escape(StringBuilder sb, string s)
        {
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
        }
    }
}
