// Parser and player for -portKeys "<t>:<key>[@<hold>],..." (t and hold in seconds since start).
//
// <key> is one of
//   * a pad alias:  cross circle square triangle l1 r1 touchpad options l3 r3
//                   (PS4 button numbers from the game's InputManager: 0..9)
//   * a direction:  up down left right  -> the arrow key plus the PS3DPadVert / PS3DPadHoriz
//                   axis at +-1 (what the game's Menu reads)
//   * a UnityEngine.KeyCode name, case-insensitive: Return, Escape, Joystick1Button0, UpArrow, A
//   * an input-manager button name: "joystick button 3", "return", "[2]"
//   * an axis override: axis:<axis name>=<value>, e.g. axis:PS4LeftStickY=-1
// A press is held for <hold> seconds (default 0.1) and always for at least one frame.

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PortDebug
{
    public sealed class PortKeyEvent
    {
        public float time;
        public float hold = 0.1f;
        public string spec;
        public readonly List<KeyCode> keys = new List<KeyCode>();
        public readonly List<KeyValuePair<string, float>> axes = new List<KeyValuePair<string, float>>();

        // runtime
        public bool started, finished;
        public int startFrame;
    }

    public static class PortKeyScript
    {
        static readonly Dictionary<string, int> s_Pad = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "cross", 0 }, { "circle", 1 }, { "square", 2 }, { "triangle", 3 }, { "l1", 4 }, { "r1", 5 },
            { "touchpad", 6 }, { "options", 7 }, { "l3", 8 }, { "r3", 9 },
        };

        public static List<PortKeyEvent> Parse(string spec, List<string> errors)
        {
            var list = new List<PortKeyEvent>();
            if (string.IsNullOrEmpty(spec)) return list;
            foreach (string raw in spec.Split(','))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;
                int colon = item.IndexOf(':');
                float t;
                if (colon <= 0 || !float.TryParse(item.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out t))
                {
                    errors.Add("bad entry (want <seconds>:<key>): " + item);
                    continue;
                }
                var e = new PortKeyEvent { time = t, spec = item };
                string key = item.Substring(colon + 1).Trim();
                int at = key.LastIndexOf('@');
                if (at > 0)
                {
                    float hold;
                    if (float.TryParse(key.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out hold))
                    {
                        e.hold = Mathf.Max(0f, hold);
                        key = key.Substring(0, at).Trim();
                    }
                }
                if (!Resolve(key, e))
                {
                    errors.Add("unknown key: " + key);
                    continue;
                }
                list.Add(e);
            }
            list.Sort((a, b) => a.time.CompareTo(b.time));
            return list;
        }

        static bool Resolve(string key, PortKeyEvent e)
        {
            int button;
            if (s_Pad.TryGetValue(key, out button))
            {
                e.keys.Add((KeyCode)((int)KeyCode.Joystick1Button0 + button));
                return true;
            }
            switch (key.ToLowerInvariant())
            {
                case "up": e.keys.Add(KeyCode.UpArrow); e.axes.Add(Ax("PS3DPadVert", 1f)); return true;
                case "down": e.keys.Add(KeyCode.DownArrow); e.axes.Add(Ax("PS3DPadVert", -1f)); return true;
                case "left": e.keys.Add(KeyCode.LeftArrow); e.axes.Add(Ax("PS3DPadHoriz", -1f)); return true;
                case "right": e.keys.Add(KeyCode.RightArrow); e.axes.Add(Ax("PS3DPadHoriz", 1f)); return true;
            }
            if (key.StartsWith("axis:", StringComparison.OrdinalIgnoreCase))
            {
                string body = key.Substring(5);
                int eq = body.LastIndexOf('=');
                float v;
                if (eq <= 0 || !float.TryParse(body.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    return false;
                e.axes.Add(Ax(body.Substring(0, eq).Trim(), v));
                return true;
            }
            KeyCode k;
            try { k = (KeyCode)Enum.Parse(typeof(KeyCode), key, true); }
            catch (ArgumentException) { k = KeyCode.None; }
            if (k == KeyCode.None || !Enum.IsDefined(typeof(KeyCode), k) || char.IsDigit(key[0]))
                k = PortInput.KeyFromName(key);
            if (k == KeyCode.None) return false;
            e.keys.Add(k);
            return true;
        }

        static KeyValuePair<string, float> Ax(string name, float v) { return new KeyValuePair<string, float>(name, v); }

        // Advance the schedule; call once per frame as early as possible. Returns events whose
        // state changed this frame (for logging).
        public static void Tick(List<PortKeyEvent> events, float now, Action<PortKeyEvent, bool> changed)
        {
            foreach (PortKeyEvent e in events)
            {
                if (e.finished) continue;
                if (!e.started)
                {
                    if (now < e.time) continue;
                    e.started = true;
                    e.startFrame = Time.frameCount;
                    foreach (KeyCode k in e.keys) PortInput.Press(k);
                    foreach (var a in e.axes) PortInput.SetAxis(a.Key, a.Value);
                    changed(e, true);
                }
                else if (now >= e.time + e.hold && Time.frameCount > e.startFrame)
                {
                    e.finished = true;
                    foreach (KeyCode k in e.keys) PortInput.Release(k);
                    foreach (var a in e.axes) PortInput.ClearAxis(a.Key);
                    changed(e, false);
                }
            }
        }
    }
}
