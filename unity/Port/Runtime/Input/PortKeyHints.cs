// Button hints for keyboard players: the game shows PS4 pad glyphs (Cross_Button, L1_Button...).
// While no gamepad is connected, every on-screen glyph (NGUI UITexture / UI2DSprite, uGUI Image)
// is swapped for a generated keycap naming the key that PortPadKeyboard maps to that button.
// With a gamepad connected the original glyphs are put back. No game code or assets changed;
// the keycaps are drawn at run time with a small built-in pixel font.

using System.Collections.Generic;
using UnityEngine;

namespace PortPad
{
    public sealed class PortKeyHints : MonoBehaviour
    {
        public static bool Enabled = true;

        // glyph texture/sprite name -> key label (see PortPadKeyboard / PortDebug/README.md)
        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "Cross_Button", "ENTER" }, { "Circle_Button", "ESC" }, { "Square_Button", "H" },
            { "Triangle_Button", "P" }, { "L1_Button", "K" }, { "R1_Button", "L" }, { "Options_Button", "TAB" },
        };

        static PortKeyHints s_instance;
        readonly Dictionary<string, Texture2D> m_caps = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Sprite> m_capSprites = new Dictionary<string, Sprite>();
        readonly Dictionary<Object, Object> m_original = new Dictionary<Object, Object>();
        float m_next;
        bool m_keyboard = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Ensure()
        {
            if ((object)s_instance != null || Application.isBatchMode)
                return;
            var go = new GameObject("PortKeyHints") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PortKeyHints>();
        }

        void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }

        public static bool GamepadConnected()
        {
            foreach (string n in Input.GetJoystickNames())
                if (!string.IsNullOrEmpty(n))
                    return true;
            return false;
        }

        void Update()
        {
            if (Time.unscaledTime < m_next)
                return;
            m_next = Time.unscaledTime + 0.2f;
            m_keyboard = Enabled && PortPadKeyboard.Enabled && !GamepadConnected();
            foreach (var t in FindObjectsByType<UITexture>(FindObjectsInactive.Include))
            {
                Texture n = Swap(t, t.mainTexture);
                if (n != t.mainTexture)
                    t.mainTexture = n;
            }
            foreach (var s in FindObjectsByType<UI2DSprite>(FindObjectsInactive.Include))
            {
                Sprite n = Swap(s, s.sprite2D);
                if (n != s.sprite2D)
                    s.sprite2D = n;
            }
            foreach (var i in FindObjectsByType<UnityEngine.UI.Image>(FindObjectsInactive.Include))
            {
                Sprite n = Swap(i, i.sprite);
                if (n != i.sprite)
                    i.sprite = n;
            }
        }

        // Returns what the component should show now.
        T Swap<T>(Object owner, T current) where T : Object
        {
            if (current == null)
                return current;
            m_original.TryGetValue(owner, out Object orig);
            bool isCap = orig != null && current != orig && IsCap(current);
            if (!isCap)
            {
                if (!Labels.TryGetValue(current.name, out string label))
                    return current;
                if (!m_keyboard)
                    return current;
                if (!m_original.ContainsKey(owner))
                    Debug.Log("[PortPad] key hint " + current.name + " -> " + label + " on " + owner.name);
                m_original[owner] = current;
                return Cap<T>(label, current, current.name.StartsWith("L1") || current.name.StartsWith("R1") ? 0.22f : 0f);
            }
            return m_keyboard ? current : (T)orig;
        }

        bool IsCap(Object o) => o != null && o.name.StartsWith("PortKeyCap ");

        T Cap<T>(string label, T like, float inset) where T : Object
        {
            string key = label + "@" + inset;
            if (!m_caps.TryGetValue(key, out Texture2D tex))
            {
                tex = KeyCap.Draw(label, inset);
                m_caps[key] = tex;
            }
            if (typeof(T) == typeof(Sprite))
            {
                if (!m_capSprites.TryGetValue(key, out Sprite sp))
                {
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    sp.name = tex.name;
                    m_capSprites[key] = sp;
                }
                return sp as T;
            }
            return tex as T;
        }
    }

    // Keycap texture: light rounded key, dark border, label in a 5x7 pixel font.
    public static class KeyCap
    {
        const int Size = 256;

        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
            { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
            { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
            { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
            { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
            { 'N', new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" } },
            { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
            { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
            { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
            { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
        };

        // inset: empty border as a fraction of the size (some glyphs fill their texture, some don't)
        public static Texture2D Draw(string label, float inset = 0f)
        {
            var px = new Color32[Size * Size];
            var clear = new Color32(0, 0, 0, 0);
            var edge = new Color32(30, 30, 30, 255);
            var face = new Color32(245, 242, 232, 255);
            var ink = new Color32(25, 25, 25, 255);
            float m = Size * (0.06f + inset), r = (Size - 2 * m) * 0.2f, b = (Size - 2 * m) * 0.05f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = RoundRect(x + 0.5f, y + 0.5f, m, Size - m, r);
                    px[y * Size + x] = d > 0 ? clear : (d > -b ? edge : face);
                }

            int n = label.Length;
            int cell = Mathf.Max(3, Mathf.Min((int)((Size - 2 * m - 2 * b - 24) / (n * 6 - 1)), 22));
            int w = (n * 6 - 1) * cell, h = 7 * cell;
            int x0 = (Size - w) / 2, y0 = (Size - h) / 2;
            for (int i = 0; i < n; i++)
            {
                if (!Font.TryGetValue(label[i], out string[] rows))
                    continue;
                for (int ry = 0; ry < 7; ry++)
                    for (int rx = 0; rx < 5; rx++)
                    {
                        if (rows[ry][rx] != '#')
                            continue;
                        int gx = x0 + (i * 6 + rx) * cell, gy = y0 + (6 - ry) * cell; // rows go bottom-up
                        for (int yy = 0; yy < cell; yy++)
                            for (int xx = 0; xx < cell; xx++)
                                px[(gy + yy) * Size + gx + xx] = ink;
                    }
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "PortKeyCap " + label,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        // signed distance to a square with rounded corners spanning [lo, hi]
        static float RoundRect(float x, float y, float lo, float hi, float r)
        {
            float cx = Mathf.Clamp(x, lo + r, hi - r), cy = Mathf.Clamp(y, lo + r, hi - r);
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }
    }
}
