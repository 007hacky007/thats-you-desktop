// Synthetic key / button / axis state for scripted input. The global Input shim (InputShim.cs)
// ORs this state into what UnityEngine.Input reports. PortObserver drives it from -portKeys.
//
// Semantics follow the legacy input manager:
//   GetKeyDown is true only on the frame a press starts, GetKey from that frame until the frame
//   before release, GetKeyUp only on the release frame.
//   Joystick1ButtonN and JoystickButtonN are treated as the same physical button.
//   GetButton*/GetAxis(name) use the InputManager axis table that PortBuild writes to
//   StreamingAssets/PortInputAxes.json: a held key that is the positive/negative (or alt) button
//   of an axis entry with that name counts as that button and pushes the axis to +1/-1.
//   Explicit axis overrides (axis:<name>=<value>) win over everything for that name.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PortDebug
{
    public static class PortInput
    {
        sealed class KeyState
        {
            public int downFrame = -1;
            public int upFrame = -1;
            public int holders;
        }

        [Serializable]
        sealed class AxisEntry
        {
            public string m_Name;
            public string negativeButton;
            public string positiveButton;
            public string altNegativeButton;
            public string altPositiveButton;
            public int type;
            public int axis;
            public bool invert;
            [NonSerialized] public KeyCode pos, neg, altPos, altNeg;
        }

        [Serializable]
        sealed class AxisTable
        {
            public AxisEntry[] axes;
        }

        static readonly Dictionary<KeyCode, KeyState> s_Keys = new Dictionary<KeyCode, KeyState>();
        static readonly Dictionary<string, float> s_AxisOverride = new Dictionary<string, float>();
        static readonly Dictionary<string, List<AxisEntry>> s_Axes = new Dictionary<string, List<AxisEntry>>();
        static bool s_AxesLoaded;

        public static bool Active { get; private set; }

        static int Frame { get { return Time.frameCount; } }

        // ---- driving ------------------------------------------------------------------------

        public static void Press(KeyCode key)
        {
            Active = true;
            foreach (KeyCode k in Same(key))
            {
                KeyState s;
                if (!s_Keys.TryGetValue(k, out s))
                    s_Keys[k] = s = new KeyState();
                if (s.holders++ == 0)
                    s.downFrame = Frame;
            }
        }

        public static void Release(KeyCode key)
        {
            foreach (KeyCode k in Same(key))
            {
                KeyState s;
                if (s_Keys.TryGetValue(k, out s) && s.holders > 0 && --s.holders == 0)
                    s.upFrame = Frame;
            }
        }

        public static void SetAxis(string name, float value)
        {
            Active = true;
            s_AxisOverride[name] = value;
        }

        public static void ClearAxis(string name)
        {
            s_AxisOverride.Remove(name);
        }

        // ---- queries (called through the Input shim) ----------------------------------------

        public static bool Key(KeyCode key)
        {
            KeyState s;
            return Active && key != KeyCode.None && s_Keys.TryGetValue(key, out s) && s.holders > 0;
        }

        public static bool KeyDown(KeyCode key)
        {
            KeyState s;
            return Active && key != KeyCode.None && s_Keys.TryGetValue(key, out s) && s.downFrame == Frame;
        }

        public static bool KeyUp(KeyCode key)
        {
            KeyState s;
            return Active && key != KeyCode.None && s_Keys.TryGetValue(key, out s) && s.upFrame == Frame;
        }

        public static bool AnyKey
        {
            get
            {
                if (!Active) return false;
                foreach (var kv in s_Keys)
                    if (kv.Value.holders > 0) return true;
                return false;
            }
        }

        public static bool AnyKeyDown
        {
            get
            {
                if (!Active) return false;
                foreach (var kv in s_Keys)
                    if (kv.Value.downFrame == Frame) return true;
                return false;
            }
        }

        public static bool Button(string name) { return ButtonQuery(name, Key); }
        public static bool ButtonDown(string name) { return ButtonQuery(name, KeyDown); }
        public static bool ButtonUp(string name) { return ButtonQuery(name, KeyUp); }

        static bool ButtonQuery(string name, Func<KeyCode, bool> q)
        {
            if (!Active || name == null) return false;
            List<AxisEntry> list = Entries(name);
            if (list == null) return false;
            foreach (AxisEntry e in list)
                if (q(e.pos) || q(e.neg) || q(e.altPos) || q(e.altNeg))
                    return true;
            return false;
        }

        // true when injected state decides the value of this axis
        public static bool Axis(string name, out float value)
        {
            value = 0f;
            if (!Active || name == null) return false;
            if (s_AxisOverride.TryGetValue(name, out value))
                return true;
            List<AxisEntry> list = Entries(name);
            if (list == null) return false;
            foreach (AxisEntry e in list)
            {
                float v = (Key(e.pos) || Key(e.altPos) ? 1f : 0f) - (Key(e.neg) || Key(e.altNeg) ? 1f : 0f);
                if (v != 0f)
                {
                    value = e.invert ? -v : v;
                    return true;
                }
            }
            return false;
        }

        // ---- key names ----------------------------------------------------------------------

        static IEnumerable<KeyCode> Same(KeyCode key)
        {
            yield return key;
            int k = (int)key;
            if (k >= (int)KeyCode.JoystickButton0 && k <= (int)KeyCode.JoystickButton19)
                yield return (KeyCode)(k - (int)KeyCode.JoystickButton0 + (int)KeyCode.Joystick1Button0);
            else if (k >= (int)KeyCode.Joystick1Button0 && k <= (int)KeyCode.Joystick1Button19)
                yield return (KeyCode)(k - (int)KeyCode.Joystick1Button0 + (int)KeyCode.JoystickButton0);
        }

        static readonly Dictionary<string, KeyCode> s_Names = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase)
        {
            { "up", KeyCode.UpArrow }, { "down", KeyCode.DownArrow }, { "left", KeyCode.LeftArrow }, { "right", KeyCode.RightArrow },
            { "return", KeyCode.Return }, { "enter", KeyCode.KeypadEnter }, { "escape", KeyCode.Escape },
            { "space", KeyCode.Space }, { "tab", KeyCode.Tab }, { "backspace", KeyCode.Backspace },
            { "delete", KeyCode.Delete }, { "insert", KeyCode.Insert }, { "home", KeyCode.Home }, { "end", KeyCode.End },
            { "page up", KeyCode.PageUp }, { "page down", KeyCode.PageDown },
            { "left shift", KeyCode.LeftShift }, { "right shift", KeyCode.RightShift },
            { "left ctrl", KeyCode.LeftControl }, { "right ctrl", KeyCode.RightControl },
            { "left alt", KeyCode.LeftAlt }, { "right alt", KeyCode.RightAlt },
            { "left cmd", KeyCode.LeftCommand }, { "right cmd", KeyCode.RightCommand },
            { "equals", KeyCode.Equals }, { "[+]", KeyCode.KeypadPlus }, { "[-]", KeyCode.KeypadMinus },
        };

        // Input-manager button name ("joystick button 0", "return", "a", "[5]", "mouse 0") -> KeyCode
        public static KeyCode KeyFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            string n = name.Trim().ToLowerInvariant();
            KeyCode k;
            if (s_Names.TryGetValue(n, out k)) return k;
            string[] p = n.Split(' ');
            int a, b;
            if (p.Length == 3 && p[0] == "joystick" && p[1] == "button" && int.TryParse(p[2], out a) && a >= 0 && a < 20)
                return (KeyCode)((int)KeyCode.JoystickButton0 + a);
            if (p.Length == 4 && p[0] == "joystick" && p[2] == "button" && int.TryParse(p[1], out a) && int.TryParse(p[3], out b)
                && a >= 1 && a <= 8 && b >= 0 && b < 20)
                return (KeyCode)((int)KeyCode.Joystick1Button0 + (a - 1) * 20 + b);
            if (p.Length == 2 && p[0] == "mouse" && int.TryParse(p[1], out a) && a >= 0 && a <= 6)
                return (KeyCode)((int)KeyCode.Mouse0 + a);
            if (n.Length == 1 && n[0] >= 'a' && n[0] <= 'z') return (KeyCode)((int)KeyCode.A + (n[0] - 'a'));
            if (n.Length == 1 && n[0] >= '0' && n[0] <= '9') return (KeyCode)((int)KeyCode.Alpha0 + (n[0] - '0'));
            if (n.Length == 3 && n[0] == '[' && n[2] == ']' && n[1] >= '0' && n[1] <= '9')
                return (KeyCode)((int)KeyCode.Keypad0 + (n[1] - '0'));
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), n.Replace(" ", ""), true); }
            catch (ArgumentException) { return KeyCode.None; }
        }

        // ---- axis table ---------------------------------------------------------------------

        static List<AxisEntry> Entries(string name)
        {
            if (!s_AxesLoaded) LoadAxes();
            List<AxisEntry> list;
            return s_Axes.TryGetValue(name, out list) ? list : null;
        }

        static void LoadAxes()
        {
            s_AxesLoaded = true;
            string path = Path.Combine(Application.streamingAssetsPath, "PortInputAxes.json");
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[PortObserve] no " + path + "; scripted keys will not drive GetButton/GetAxis");
                    return;
                }
                AxisTable t = JsonUtility.FromJson<AxisTable>(File.ReadAllText(path));
                if (t == null || t.axes == null) return;
                foreach (AxisEntry e in t.axes)
                {
                    if (string.IsNullOrEmpty(e.m_Name) || e.type != 0) continue; // key/mouse button entries only
                    e.pos = KeyFromName(e.positiveButton);
                    e.neg = KeyFromName(e.negativeButton);
                    e.altPos = KeyFromName(e.altPositiveButton);
                    e.altNeg = KeyFromName(e.altNegativeButton);
                    List<AxisEntry> list;
                    if (!s_Axes.TryGetValue(e.m_Name, out list))
                        s_Axes[e.m_Name] = list = new List<AxisEntry>();
                    list.Add(e);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PortObserve] could not read " + path + ": " + ex.Message);
            }
        }
    }
}
