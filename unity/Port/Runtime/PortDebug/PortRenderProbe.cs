// Render diagnostics for automated runs of the player. Inert unless the player is started with
//   -portObserve <dir>        writes "render" events to <dir>/render.jsonl
//   -portNoImageEffects       disables the camera MonoBehaviours that have OnRenderImage, except
//                             the ones the game needs to work (see k_DefaultKeep)
// (either one switches the probe on; the second works without the first too). With
// -portNoImageEffects, two optional lists of type names (Name or FullName, case-insensitive,
// separated by commas) change which behaviours are switched off:
//   -portNoImageEffectsOnly <Type,Type>   disable only these; naming a default-kept type here
//                                         disables it too
//   -portKeepImageEffects <Type,Type>     never disable these (added to the default-kept set)
//
// A "render" event is written 1 s after every scene load (reason "scene <name>", so the scene
// has rendered at least once and Renderer.isVisible means something) and every 10 s
// (reason "timer"):
//   {"t":12.345,"kind":"render","msg":"<digest>","reason":"timer","scene":"Home",
//    "noImageEffects":false,"noImageEffectsOnly":null|[..],"keepImageEffects":[..],
//    "cameras":[{"name","path","depth","clearFlags","cullingMask","layers":[..],
//                "targetTexture":null|"<name> <w>x<h>","rect":[x,y,w,h],"hdr","renderingPath",
//                "depthTextureMode","visibleRenderers",
//                "effects":[{"type","enabled","probeDisabled","probeKept","shaders":[..]}],
//                "shaders":[..]}],
//    "ngui":{"type":"UIDrawCall"|null,"drawCalls":n,"shaders":[{"name","drawCalls","visible"}]},
//    "shaders":[{"name","supported","passCount","renderQueue"}]}
// "effects" lists the enabled (or probe-disabled) MonoBehaviours on the camera that define
// OnRenderImage, with the shaders found in their Shader / Material fields; "probeKept" marks
// the ones -portNoImageEffects left on because of the keep set or the -Only list.
// "keepImageEffects" is the effective keep set (defaults plus -portKeepImageEffects, minus the
// types named in -portNoImageEffectsOnly); both lists are empty / null without -portNoImageEffects. A camera's "shaders"
// are those of the enabled, visible renderers whose layer is in its culling mask (isVisible is
// true when any camera renders the renderer, so this is an upper bound per camera). NGUI draw
// calls are found by the type name UIDrawCall, with no compile dependency on NGUI. "shaders"
// at the top level describes every shader named anywhere in the event.
//
// PortObserver keeps log.jsonl open with its own writer and has no public append hook, so the
// probe writes its own file next to it. Installed with [RuntimeInitializeOnLoadMethod]; no scene
// or game file is modified, and without the two arguments nothing happens.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PortDebug
{
    [DefaultExecutionOrder(32000)]
    public sealed class PortRenderProbe : MonoBehaviour
    {
        const float k_Interval = 10f;
        const float k_SceneDelay = 1f;
        const BindingFlags k_Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static PortRenderProbe s_Instance;
        static readonly Dictionary<Type, bool> s_HasOnRenderImage = new Dictionary<Type, bool>();
        static readonly Dictionary<Type, FieldInfo[]> s_ShaderFields = new Dictionary<Type, FieldInfo[]>();
        static readonly Type[] k_OnRenderImageArgs = { typeof(RenderTexture), typeof(RenderTexture) };
        static Type s_DrawCallType;
        static bool s_DrawCallLookedUp;

        // OnRenderImage behaviours that are part of how the game works rather than post effects,
        // matched by type name so there is no compile-time reference to game code:
        //   MenuRenderer             snapshots the menu for page transitions; the menu waits for it
        //   ScreenCopier             copies the frame into a texture the game shows later
        //   TransitionBorderOverlay  draws the scenario transition borders, passes through otherwise
        //   ExcludedEffectGrab/Merge put the objects excluded from the effect chain back on top
        static readonly string[] k_DefaultKeep =
        {
            "MenuRenderer", "ScreenCopier", "TransitionBorderOverlay", "ExcludedEffectGrab", "ExcludedEffectMerge",
        };

        const int k_Disable = 0, k_KeepListed = 1, k_KeepNotOnly = 2;

        string m_Path;
        bool m_NoEffects;
        // null: every OnRenderImage behaviour not in m_Keep; otherwise only the ones named here
        List<string> m_Only;
        readonly List<string> m_Keep = new List<string>();
        readonly Dictionary<Type, int> m_Decision = new Dictionary<Type, int>();
        float m_Next;
        string m_LastDigest;
        readonly List<KeyValuePair<float, string>> m_Pending = new List<KeyValuePair<float, string>>();
        // behaviours this probe disabled
        readonly HashSet<Behaviour> m_Disabled = new HashSet<Behaviour>();
        // behaviours this probe left on although -portNoImageEffects is set (logged once)
        readonly HashSet<Behaviour> m_Kept = new HashSet<Behaviour>();
        readonly List<MonoBehaviour> m_Scratch = new List<MonoBehaviour>();

        public static bool ImageEffectsDisabled
        {
            get { return s_Instance != null && s_Instance.m_NoEffects; }
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        static List<string> TypeList(string value)
        {
            var list = new List<string>();
            if (value == null) return list;
            foreach (string part in value.Split(',', ';'))
            {
                string n = part.Trim();
                if (n.Length > 0 && !Contains(list, n)) list.Add(n);
            }
            return list;
        }

        static bool Contains(List<string> names, string name)
        {
            foreach (string n in names)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static bool Matches(List<string> names, Type t)
        {
            return Contains(names, t.Name) || (t.FullName != null && Contains(names, t.FullName));
        }

        static bool Has(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (s_Instance != null)
                return;
            string[] args = Environment.GetCommandLineArgs();
            string dir = Arg(args, "-portObserve");
            bool noEffects = Has(args, "-portNoImageEffects");
            if (string.IsNullOrEmpty(dir) && !noEffects)
                return;
            var go = new GameObject("PortRenderProbe");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<PortRenderProbe>();
            string only = Arg(args, "-portNoImageEffectsOnly");
            string keep = Arg(args, "-portKeepImageEffects");
            if (!noEffects && (only != null || keep != null))
                Debug.LogWarning("[PortObserve] -portNoImageEffectsOnly / -portKeepImageEffects ignored without -portNoImageEffects");
            s_Instance.Init(string.IsNullOrEmpty(dir) ? null : Path.GetFullPath(dir), noEffects,
                            only != null ? TypeList(only) : null, TypeList(keep));
        }

        void Init(string dir, bool noEffects, List<string> only, List<string> keep)
        {
            m_NoEffects = noEffects;
            if (noEffects)
            {
                m_Only = only;
                foreach (string n in k_DefaultKeep)
                    if (m_Only == null || !Contains(m_Only, n))
                        m_Keep.Add(n);
                foreach (string n in keep)
                    if (!Contains(m_Keep, n))
                        m_Keep.Add(n);
            }
            if (dir != null)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    m_Path = Path.Combine(dir, "render.jsonl");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PortObserve] render probe cannot use " + dir + ": " + e.Message);
                }
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            m_Next = Time.realtimeSinceStartup + k_Interval;
            Debug.Log("[PortObserve] render probe started: file=" + (m_Path ?? "-") + " noImageEffects=" + (noEffects ? "on" : "off")
                      + (noEffects ? " only=" + (m_Only == null ? "*" : string.Join(",", m_Only.ToArray()))
                                     + " keep=" + string.Join(",", m_Keep.ToArray()) : ""));
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (s_Instance == this) s_Instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (m_NoEffects)
                DisableEffects(Resources.FindObjectsOfTypeAll<Camera>());
            if (m_Path != null)
                m_Pending.Add(new KeyValuePair<float, string>(Time.realtimeSinceStartup + k_SceneDelay, "scene " + scene.name));
        }

        void Update()
        {
            if (m_Path == null) return;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                if (now < m_Pending[i].Key) continue;
                string reason = m_Pending[i].Value;
                m_Pending.RemoveAt(i--);
                Write(reason);
            }
            if (now >= m_Next)
            {
                m_Next = now + k_Interval;
                Write("timer");
            }
        }

        // Every frame, so effects the game adds or re-enables are switched off before they render.
        void LateUpdate()
        {
            if (m_NoEffects)
                DisableEffects(Camera.allCameras);
        }

        void DisableEffects(Camera[] cameras)
        {
            foreach (Camera cam in cameras)
            {
                if (cam == null || !cam.gameObject.scene.IsValid()) continue;
                cam.GetComponents(m_Scratch);
                foreach (MonoBehaviour mb in m_Scratch)
                {
                    if (mb == null || !mb.enabled || !HasOnRenderImage(mb.GetType())) continue;
                    int decision = Decide(mb.GetType());
                    if (decision != k_Disable)
                    {
                        if (m_Kept.Add(mb))
                            Debug.Log("[PortObserve] -portNoImageEffects: kept " + mb.GetType().FullName + " on camera " + cam.name
                                      + (decision == k_KeepListed ? " (keep set)" : " (not in -portNoImageEffectsOnly)"));
                        continue;
                    }
                    mb.enabled = false;
                    if (m_Disabled.Add(mb))
                    {
                        Debug.Log("[PortObserve] -portNoImageEffects: disabled " + mb.GetType().FullName + " on camera " + cam.name);
                    }
                }
            }
        }

        int Decide(Type t)
        {
            int d;
            if (m_Decision.TryGetValue(t, out d)) return d;
            if (Matches(m_Keep, t)) d = k_KeepListed;
            else if (m_Only != null && !Matches(m_Only, t)) d = k_KeepNotOnly;
            else d = k_Disable;
            m_Decision[t] = d;
            return d;
        }

        static bool HasOnRenderImage(Type t)
        {
            bool has;
            if (s_HasOnRenderImage.TryGetValue(t, out has)) return has;
            for (Type c = t; c != null && c != typeof(MonoBehaviour) && !has; c = c.BaseType)
                has = c.GetMethod("OnRenderImage", k_Inst | BindingFlags.DeclaredOnly, null, k_OnRenderImageArgs, null) != null;
            s_HasOnRenderImage[t] = has;
            return has;
        }

        static FieldInfo[] ShaderFields(Type t)
        {
            FieldInfo[] found;
            if (s_ShaderFields.TryGetValue(t, out found)) return found;
            var list = new List<FieldInfo>();
            for (Type c = t; c != null && c != typeof(MonoBehaviour); c = c.BaseType)
                foreach (FieldInfo f in c.GetFields(k_Inst | BindingFlags.DeclaredOnly))
                    if (typeof(Shader).IsAssignableFrom(f.FieldType) || typeof(Material).IsAssignableFrom(f.FieldType))
                        list.Add(f);
            found = list.ToArray();
            s_ShaderFields[t] = found;
            return found;
        }

        static Type DrawCallType()
        {
            if (s_DrawCallLookedUp) return s_DrawCallType;
            s_DrawCallLookedUp = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType("UIDrawCall", false); }
                catch (Exception) { continue; }
                if (t != null && typeof(Component).IsAssignableFrom(t))
                {
                    s_DrawCallType = t;
                    break;
                }
            }
            return s_DrawCallType;
        }

        // ---- capture ----

        sealed class ShaderSet
        {
            public readonly HashSet<Shader> all = new HashSet<Shader>();

            public void Add(Shader s)
            {
                if (s != null) all.Add(s);
            }
        }

        static void AddMaterials(Renderer r, ShaderSet shaders, SortedSet<string> names)
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                shaders.Add(m.shader);
                names.Add(m.shader.name);
            }
        }

        void Write(string reason)
        {
            string line, digest;
            try
            {
                line = Capture(reason, out digest);
            }
            catch (Exception e)
            {
                digest = "error " + e.GetType().Name;
                var err = new StringBuilder(128);
                Head(err, reason, digest);
                err.Append(",\"error\":");
                Str(err, e.ToString());
                err.Append('}');
                line = err.ToString();
            }
            try
            {
                File.AppendAllText(m_Path, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortObserve] render probe write failed: " + e.Message);
            }
            if (digest != m_LastDigest)
            {
                m_LastDigest = digest;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PortObserve] render ({0}): {1} t={2:F2}",
                                        reason, digest, Time.realtimeSinceStartup));
            }
        }

        void Head(StringBuilder sb, string reason, string digest)
        {
            sb.Append("{\"t\":").Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(",\"kind\":\"render\",\"msg\":");
            Str(sb, digest);
            sb.Append(",\"reason\":");
            Str(sb, reason);
            sb.Append(",\"scene\":");
            Str(sb, SceneManager.GetActiveScene().name);
            sb.Append(",\"noImageEffects\":").Append(m_NoEffects ? "true" : "false");
            sb.Append(",\"noImageEffectsOnly\":");
            if (m_Only != null) StrList(sb, m_Only);
            else sb.Append("null");
            sb.Append(",\"keepImageEffects\":");
            StrList(sb, m_Keep);
        }

        string Capture(string reason, out string digest)
        {
            var shaders = new ShaderSet();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var visible = new List<Renderer>(renderers.Length);
            foreach (Renderer r in renderers)
                if (r != null && r.enabled && r.isVisible)
                    visible.Add(r);

            Camera[] cams = Camera.allCameras;
            Array.Sort(cams, (a, b) => a.depth.CompareTo(b.depth));
            var body = new StringBuilder(4096);
            int effectCount = 0;
            body.Append(",\"cameras\":[");
            for (int ci = 0; ci < cams.Length; ci++)
            {
                Camera cam = cams[ci];
                if (ci > 0) body.Append(',');
                effectCount += WriteCamera(body, cam, visible, shaders);
            }
            body.Append(']');

            WriteNgui(body, shaders);

            int unsupported = 0;
            var list = new List<Shader>(shaders.all);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            body.Append(",\"shaders\":[");
            for (int i = 0; i < list.Count; i++)
            {
                Shader s = list[i];
                if (i > 0) body.Append(',');
                bool supported = s.isSupported;
                if (!supported) unsupported++;
                body.Append("{\"name\":");
                Str(body, s.name);
                body.Append(",\"supported\":").Append(supported ? "true" : "false");
                body.Append(",\"passCount\":").Append(s.passCount.ToString(CultureInfo.InvariantCulture));
                body.Append(",\"renderQueue\":").Append(s.renderQueue.ToString(CultureInfo.InvariantCulture));
                body.Append('}');
            }
            body.Append(']');

            digest = string.Format(CultureInfo.InvariantCulture, "cams={0} effects={1} visible={2} shaders={3} unsupported={4}",
                                   cams.Length, effectCount, visible.Count, list.Count, unsupported);
            var sb = new StringBuilder(body.Length + 256);
            Head(sb, reason, digest);
            sb.Append(body).Append('}');
            return sb.ToString();
        }

        int WriteCamera(StringBuilder sb, Camera cam, List<Renderer> visible, ShaderSet shaders)
        {
            sb.Append("{\"name\":");
            Str(sb, cam.name);
            sb.Append(",\"path\":");
            Str(sb, PathOf(cam.transform));
            sb.Append(",\"depth\":").Append(Num(cam.depth));
            sb.Append(",\"clearFlags\":");
            Str(sb, cam.clearFlags.ToString());
            int mask = cam.cullingMask;
            sb.Append(",\"cullingMask\":").Append(mask.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"layers\":[");
            bool first = true;
            for (int l = 0; l < 32; l++)
            {
                if ((mask & (1 << l)) == 0) continue;
                if (!first) sb.Append(',');
                first = false;
                string n = LayerMask.LayerToName(l);
                Str(sb, string.IsNullOrEmpty(n) ? l.ToString(CultureInfo.InvariantCulture) : n);
            }
            sb.Append(']');
            sb.Append(",\"targetTexture\":");
            RenderTexture rt = cam.targetTexture;
            if (rt != null) Str(sb, string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2}", rt.name, rt.width, rt.height));
            else sb.Append("null");
            Rect r = cam.rect;
            sb.Append(",\"rect\":[").Append(Num(r.x)).Append(',').Append(Num(r.y)).Append(',')
              .Append(Num(r.width)).Append(',').Append(Num(r.height)).Append(']');
            sb.Append(",\"hdr\":").Append(cam.allowHDR ? "true" : "false");
            sb.Append(",\"renderingPath\":");
            Str(sb, cam.actualRenderingPath.ToString());
            sb.Append(",\"depthTextureMode\":");
            Str(sb, cam.depthTextureMode.ToString());

            // image effects
            int effects = 0;
            sb.Append(",\"effects\":[");
            cam.GetComponents(m_Scratch);
            foreach (MonoBehaviour mb in m_Scratch)
            {
                if (mb == null || !HasOnRenderImage(mb.GetType())) continue;
                bool probeDisabled = m_Disabled.Contains(mb);
                if (!mb.enabled && !probeDisabled) continue;
                if (effects++ > 0) sb.Append(',');
                sb.Append("{\"type\":");
                Str(sb, mb.GetType().FullName);
                sb.Append(",\"enabled\":").Append(mb.enabled ? "true" : "false");
                sb.Append(",\"probeDisabled\":").Append(probeDisabled ? "true" : "false");
                sb.Append(",\"probeKept\":").Append(m_Kept.Contains(mb) ? "true" : "false");
                var names = new SortedSet<string>(StringComparer.Ordinal);
                foreach (FieldInfo f in ShaderFields(mb.GetType()))
                {
                    UnityEngine.Object o;
                    try { o = f.GetValue(mb) as UnityEngine.Object; }
                    catch (Exception) { continue; }
                    if (o == null) continue;
                    Shader s = o as Shader;
                    if (s == null)
                    {
                        Material m = o as Material;
                        if (m != null) s = m.shader;
                    }
                    if (s == null) continue;
                    shaders.Add(s);
                    names.Add(s.name);
                }
                sb.Append(",\"shaders\":");
                StrList(sb, names);
                sb.Append('}');
            }
            sb.Append(']');

            // visible renderers in the culling mask
            var camShaders = new SortedSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (Renderer rd in visible)
            {
                if ((mask & (1 << rd.gameObject.layer)) == 0) continue;
                count++;
                AddMaterials(rd, shaders, camShaders);
            }
            sb.Append(",\"visibleRenderers\":").Append(count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"shaders\":");
            StrList(sb, camShaders);
            sb.Append('}');
            return effects;
        }

        static void WriteNgui(StringBuilder sb, ShaderSet shaders)
        {
            Type t = DrawCallType();
            sb.Append(",\"ngui\":{\"type\":");
            if (t == null)
            {
                sb.Append("null,\"drawCalls\":0,\"shaders\":[]}");
                return;
            }
            Str(sb, t.FullName);
            // name -> [draw calls, visible]
            var perShader = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            int drawCalls = 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
            {
                Component c = o as Component;
                if (c == null || !c.gameObject.scene.IsValid() || !c.gameObject.activeInHierarchy) continue;
                Renderer r = c.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                drawCalls++;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    shaders.Add(m.shader);
                    int[] n;
                    if (!perShader.TryGetValue(m.shader.name, out n)) perShader[m.shader.name] = n = new int[2];
                    n[0]++;
                    if (r.isVisible) n[1]++;
                }
            }
            sb.Append(",\"drawCalls\":").Append(drawCalls.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"shaders\":[");
            bool first = true;
            foreach (KeyValuePair<string, int[]> kv in perShader)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":");
                Str(sb, kv.Key);
                sb.Append(",\"drawCalls\":").Append(kv.Value[0].ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"visible\":").Append(kv.Value[1].ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append("]}");
        }

        // ---- JSON helpers ----

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static string Num(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "null";
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        static void Str(StringBuilder sb, string s)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('"');
            PortObserver.Escape(sb, s);
            sb.Append('"');
        }

        static void StrList(StringBuilder sb, IEnumerable<string> items)
        {
            sb.Append('[');
            bool first = true;
            foreach (string s in items)
            {
                if (!first) sb.Append(',');
                first = false;
                Str(sb, s);
            }
            sb.Append(']');
        }
    }
}
