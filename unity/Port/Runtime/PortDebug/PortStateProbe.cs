// Game-state probe for PortObserver. Reads the front end's menu, title, pause and NGUI label
// state so an automated run can tell whether the menu opened and where the selection is, even
// while the screen renders black. Only called by PortObserver, so it is inert unless the player
// runs with -portObserve.
//
// Port Runtime compiles into Assembly-CSharp, so public and internal game members are read
// directly; private ones go through cached reflection and read as null when absent.
//
// Capture() returns one JSON object:
//   {"scene":"Home",
//    "menu":{"active":true,"transition":false,"ignoreInput":"None",
//            "page":{"name":..,"object":..,"class":..,"type":"kOptions","index":0,"count":5,
//                    "item":..,"itemObject":..,"itemClass":..}},
//    "display":{"page":..,"button":..,"title":..,"titleShown":..,"panel":..,"arrowScrolling":..,"buttonPressed":..},
//    "frontend":{"in":true,"starting":false,"home":..,"transition":..},
//    "logo":false,
//    "pause":{"canPause":false,"paused":false},
//    "labels":["text", ...]}
// Missing singletons are written as null.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PortDebug
{
    public static class PortStateProbe
    {
        public const int MaxLabels = 48;
        public const int MaxText = 80;

        const BindingFlags k_Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static readonly Dictionary<string, FieldInfo> s_Fields = new Dictionary<string, FieldInfo>();

        // NGUI inline markup: colours [rrggbb] / [rrggbbaa], [-], [b] [i] [u] [s] [c] [sub] [sup], [url=..]
        static readonly Regex s_Markup = new Regex(@"\[(?:[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?|-|/?[biusc]|/?sub|/?sup|/?url(?:=[^\]]*)?)\]",
                                                    RegexOptions.CultureInvariant);

        static object Field(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            string key = t.FullName + ":" + name;
            FieldInfo f;
            if (!s_Fields.TryGetValue(key, out f))
            {
                for (Type c = t; c != null && f == null; c = c.BaseType)
                    f = c.GetField(name, k_Inst | BindingFlags.DeclaredOnly);
                s_Fields[key] = f;
            }
            return f != null ? f.GetValue(target) : null;
        }

        static bool? FieldBool(object target, string name)
        {
            object v = Field(target, name);
            return v is bool ? (bool?)(bool)v : null;
        }

        /// <summary>The current game state as a JSON object (never throws).</summary>
        public static string Capture()
        {
            var w = new Json();
            w.Begin();
            Section(w, "scene", () => w.Str(SceneManager.GetActiveScene().name));
            Section(w, "menu", () => WriteMenu(w));
            Section(w, "display", () => WriteDisplay(w));
            Section(w, "frontend", () => WriteFrontend(w));
            Section(w, "logo", () => w.Bool(IWYKLogo.Enabled));
            Section(w, "pause", () => WritePause(w));
            Section(w, "labels", () => WriteLabels(w));
            w.End();
            return w.ToString();
        }

        // Writes one member; a failure inside it becomes {"error":".."} without losing the rest.
        static void Section(Json w, string name, Action body)
        {
            w.Key(name);
            int mark = w.Length;
            try
            {
                body();
            }
            catch (Exception e)
            {
                w.Truncate(mark);
                w.Begin();
                w.Key("error").Str(e.GetType().Name + ": " + e.Message);
                w.End();
            }
        }

        static void WriteMenu(Json w)
        {
            w.Begin();
            w.Key("active").Bool(Menu.MenuActive);
            w.Key("transition").Bool(Menu.MenuInTransition);
            w.Key("ignoreInput").Str(Menu.IgnoreInput.ToString());
            w.Key("loading").Bool(Menu.m_Loading);
            w.Key("page");
            MenuPage page = Menu.CurrentMenuPage;
            if (page == null)
            {
                w.Null();
            }
            else
            {
                w.Begin();
                w.Key("name").Str(page.MenuPageName);
                w.Key("object").Str(page.gameObject.name);
                w.Key("class").Str(page.GetType().Name);
                w.Key("type").Str(page.m_MenuPageType.ToString());
                w.Key("index").Int(page.MenuPagePosition);
                w.Key("count").Int(page.m_MenuObjects != null ? page.m_MenuObjects.Count : 0);
                MenuObject item = page.CurrentMenuObject;
                if (item == null && page.m_MenuObjects != null && page.MenuPagePosition >= 0
                    && page.MenuPagePosition < page.m_MenuObjects.Count)
                    item = page.m_MenuObjects[page.MenuPagePosition];
                w.Key("item").Str(item != null ? item.MenuObjectName : null);
                w.Key("itemObject").Str(item != null ? item.gameObject.name : null);
                w.Key("itemClass").Str(item != null ? item.GetType().Name : null);
                w.End();
            }
            w.End();
        }

        static void WriteDisplay(Json w)
        {
            MenuDisplay d = MenuDisplay.s;
            if (d == null)
            {
                w.Null();
                return;
            }
            w.Begin();
            var page = Field(d, "m_CurrentMenuPage") as MenuPage;
            var button = Field(d, "m_CurrentButton") as MenuObject;
            w.Key("page").Str(page != null ? page.gameObject.name : null);
            w.Key("button").Str(button != null ? button.MenuObjectName : null);
            UILabel title = d.m_MenuTitle;
            w.Key("title").Str(title != null ? Clean(title.text) : null);
            w.Key("titleShown").Bool(title != null && title.enabled && title.gameObject.activeInHierarchy);
            w.Key("panel").Bool(d.m_SlidePanel != null && d.m_SlidePanel.activeInHierarchy);
            w.Key("arrowScrolling").Bool(FieldBool(d, "m_ArrowScrolling"));
            w.Key("buttonPressed").Bool(FieldBool(d, "m_MenuButtonPressed"));
            w.End();
        }

        static void WriteFrontend(Json w)
        {
            w.Begin();
            w.Key("in").Bool(FrontEndFlow.s_InFrontend);
            w.Key("starting").Bool(FrontEndFlow.IsStarting);
            FrontEndFlow flow = UnityEngine.Object.FindAnyObjectByType<FrontEndFlow>();
            object pos = Field(flow, "m_HomeCameraPosition");
            object tr = Field(flow, "m_HomeTransition");
            w.Key("home").Str(pos != null ? pos.ToString() : null);
            w.Key("transition").Str(tr != null ? tr.ToString() : null);
            w.End();
        }

        static void WritePause(Json w)
        {
            PauseGame p = PauseGame.s;
            if (p == null)
            {
                w.Null();
                return;
            }
            w.Begin();
            w.Key("canPause").Bool(p.CanPause);
            w.Key("paused").Bool(p.Paused);
            w.End();
        }

        struct Shown
        {
            public float x, y;
            public string text;
        }

        // Active, visible labels whose centre is inside the viewport of the camera drawing them,
        // ordered top to bottom, then left to right.
        static void WriteLabels(Json w)
        {
            var shown = new List<Shown>();
            var seen = new HashSet<UILabel>();
            var cams = new Dictionary<int, Camera>();
            foreach (UIPanel panel in UIPanel.list)
            {
                if (panel == null || !panel.isActiveAndEnabled || panel.widgets == null) continue;
                foreach (UIWidget wd in panel.widgets)
                {
                    var label = wd as UILabel;
                    if (label == null || !seen.Add(label) || !label.enabled || !label.isVisible) continue;
                    string text = Clean(label.text);
                    if (text.Length == 0) continue;
                    int layer = label.gameObject.layer;
                    Camera cam;
                    if (!cams.TryGetValue(layer, out cam))
                        cams[layer] = cam = NGUITools.FindCameraForLayer(layer);
                    Vector3 vp = new Vector3(0.5f, 0.5f, 1f);
                    if (cam != null)
                    {
                        if (!cam.isActiveAndEnabled) continue;
                        Vector3[] c = label.worldCorners;
                        vp = cam.WorldToViewportPoint((c[0] + c[2]) * 0.5f);
                        if (vp.z < 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                    }
                    shown.Add(new Shown { x = vp.x, y = vp.y, text = text });
                }
            }
            shown.Sort((a, b) =>
            {
                float dy = b.y - a.y;
                if (Mathf.Abs(dy) > 0.01f) return dy > 0 ? 1 : -1;
                return a.x.CompareTo(b.x);
            });
            w.BeginArray();
            for (int i = 0; i < shown.Count && i < MaxLabels; i++)
                w.Str(shown[i].text);
            if (shown.Count > MaxLabels)
                w.Str("... +" + (shown.Count - MaxLabels).ToString(CultureInfo.InvariantCulture) + " more");
            w.EndArray();
        }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s_Markup.Replace(s, "");
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space) sb.Append(' ');
                space = false;
                sb.Append(ch);
            }
            string r = sb.ToString();
            return r.Length > MaxText ? r.Substring(0, MaxText) + "..." : r;
        }

        /// <summary>One-line digest of a probe for player.log.</summary>
        public static string Summary()
        {
            try
            {
                var sb = new StringBuilder();
                MenuPage page = Menu.CurrentMenuPage;
                sb.Append("menu=").Append(Menu.MenuActive ? "on" : "off");
                if (page != null)
                {
                    MenuObject item = page.CurrentMenuObject;
                    sb.Append(" page=").Append(page.gameObject.name).Append('(').Append(page.m_MenuPageType).Append(')');
                    sb.Append(" sel=").Append(page.MenuPagePosition.ToString(CultureInfo.InvariantCulture));
                    if (item != null) sb.Append(":'").Append(Clean(item.MenuObjectName)).Append('\'');
                }
                sb.Append(" ignore=").Append(Menu.IgnoreInput);
                sb.Append(" frontend=").Append(FrontEndFlow.s_InFrontend ? 1 : 0);
                sb.Append(" logo=").Append(IWYKLogo.Enabled ? 1 : 0);
                if (PauseGame.s != null) sb.Append(" canPause=").Append(PauseGame.s.CanPause ? 1 : 0);
                return sb.ToString();
            }
            catch (Exception e)
            {
                return "probe failed: " + e.GetType().Name;
            }
        }

        // Minimal JSON writer: tracks commas, escapes strings.
        sealed class Json
        {
            readonly StringBuilder m_Sb = new StringBuilder(512);
            bool m_NeedComma;

            public int Length { get { return m_Sb.Length; } }

            public void Truncate(int length)
            {
                m_Sb.Length = length;
                m_NeedComma = false;
            }

            void Sep()
            {
                if (m_NeedComma) m_Sb.Append(',');
                m_NeedComma = true;
            }

            public void Begin() { Sep(); m_Sb.Append('{'); m_NeedComma = false; }
            public void End() { m_Sb.Append('}'); m_NeedComma = true; }
            public void BeginArray() { Sep(); m_Sb.Append('['); m_NeedComma = false; }
            public void EndArray() { m_Sb.Append(']'); m_NeedComma = true; }

            public Json Key(string k)
            {
                Sep();
                Quote(k);
                m_Sb.Append(':');
                m_NeedComma = false;
                return this;
            }

            public void Null() { Sep(); m_Sb.Append("null"); }
            public void Bool(bool b) { Sep(); m_Sb.Append(b ? "true" : "false"); }
            public void Bool(bool? b) { if (b.HasValue) Bool(b.Value); else Null(); }
            public void Int(int i) { Sep(); m_Sb.Append(i.ToString(CultureInfo.InvariantCulture)); }

            public void Str(string s)
            {
                if (s == null) { Null(); return; }
                Sep();
                Quote(s);
            }

            void Quote(string s)
            {
                m_Sb.Append('"');
                PortObserver.Escape(m_Sb, s);
                m_Sb.Append('"');
            }

            public override string ToString() { return m_Sb.ToString(); }
        }
    }
}
