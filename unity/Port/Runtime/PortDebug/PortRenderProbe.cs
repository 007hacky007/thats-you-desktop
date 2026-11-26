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
// An "env" event is written (and logged to player.log as "[PortObserve] env (...)") when a scene
// loads and again 1 s later, so the second one shows what PortEnvironmentLighting changed:
//   {"t","kind":"env","reason":"scene load <name>"|"scene <name> +1s","scene",
//    "colorSpace","lightsUseLinearIntensity","realtimeReflectionProbes","envFix":"<status>",
//    "ambient":{"mode","intensity","light","sky","equator","ground","probeInvalid",
//               "probe":[27 floats, r0..r8,g0..g8,b0..b8]},
//    "reflection":{"mode","resolution","intensity","bounces","custom":<tex>,"default":<tex>},
//    "skybox":null|{"material","shader"},"sun":null|"<name>",
//    "lightmaps":n,"lightProbes":n|null,
//    "reflectionProbes":[{"name","enabled","mode","importance","center","size","texture":<tex>}],
//    "badges":[{"path","active","enabled","visible","layer","lossyScale","position","forward",
//               "lightProbeUsage","reflectionProbeUsage",
//               "materials":[{"name","shader","keywords","textures":{"_MainTex":<tex>,..}}],
//               "cameras":[{"name","path","forwardDot","normalDot","facingCamera","distance"}],
//               "lights":[{"name","type","intensity","color","range","spotAngle","distance",
//                          "inRange","inCone","facing","renderMode","shadows","bounceIntensity"}]}]}
// <tex> is null or {"name","width","height","dimension"}. Badges are renderers (active or not)
// whose GameObject name contains "WishBadge"; their cameras are the enabled cameras whose culling
// mask has the badge layer ("forwardDot" is the badge forward dotted with the direction to the
// camera; "normalDot" uses the world normal of the local +Z face, which a mirroring scale flips,
// and "facingCamera" is normalDot > 0) and their lights the enabled lights whose culling mask
// has the layer ("facing" is the face normal dotted with the direction to the light).
//
// While a scene named "Home" is loaded, a "badge" event is written every 2 s (to render.jsonl,
// so only with -portObserve) for the active renderers named WishBadge* and the GroundPlane
// renderers under a WishLogo object, with what decides their direct and probe lighting:
//   {"t","kind":"badge","msg":"<digest>","scene","experiments":{..},
//    "quality":{"shadows","shadowDistance","shadowProjection","pixelLightCount"},
//    "lightProbes":n|null,"ambientDc":[r,g,b],
//    "cameras":[{"name","path","enabled","depth","cullingMask","renderingPath","actualRenderingPath",
//                "allowHDR","allowMSAA","targetTexture"}],
//    "renderers":[{"path","active","enabled","visible","layer","center","size","shadowCasting",
//                  "receiveShadows","lightProbeUsage","probeAnchor":null|"<path>","probePos",
//                  "shDc":[r,g,b],"cameras":["<name>",..],
//                  "lights":[{"name","type","inMask","intensity","color","range","spotAngle",
//                             "distance","inRange","inCone","facing","renderMode","shadows",
//                             "shadowStrength","shadowBias","shadowNormalBias","shadowNearPlane",
//                             "shadowResolution","bakeType","isBaked"}]}]}
// "shDc" is the DC (L0) term of LightProbes.GetInterpolatedProbe at the anchor (or the bounds
// centre) per channel; "ambientDc" the same term of RenderSettings.ambientProbe. "lights" are all
// enabled lights in active objects, whether their culling mask has the renderer layer or not
// ("inMask"); "facing" is the local +Z face normal dotted with the direction to the light.
//
// Lighting experiments (diagnostic, default off; any one also switches the probe on). Each is
// applied on every scene load and logged once to player.log as "[PortObserve] <flag>: ...";
// the camera ones are re-applied in LateUpdate and the others with every badge tick, so values
// the game sets later are overridden too:
//   -portCameraHdr                    Camera.allowHDR = true on every camera
//   -portForceForward                 Camera.renderingPath = Forward on every camera
//   -portNoShadows                    QualitySettings.shadows = ShadowQuality.Disable
//   -portNoLightShadows <Spot,Point>  Light.shadows = None for lights of the listed LightTypes
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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PortDebug
{
    [DefaultExecutionOrder(32000)]
    public sealed class PortRenderProbe : MonoBehaviour
    {
        const float k_Interval = 10f;
        const float k_SceneDelay = 1f;
        const float k_BadgeInterval = 2f;
        const string k_BadgeScene = "Home";
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
        readonly List<KeyValuePair<float, string>> m_PendingEnv = new List<KeyValuePair<float, string>>();
        // behaviours this probe disabled
        readonly HashSet<Behaviour> m_Disabled = new HashSet<Behaviour>();
        // behaviours this probe left on although -portNoImageEffects is set (logged once)
        readonly HashSet<Behaviour> m_Kept = new HashSet<Behaviour>();
        readonly List<MonoBehaviour> m_Scratch = new List<MonoBehaviour>();

        // lighting experiments
        bool m_CameraHdr, m_ForceForward, m_NoShadows;
        readonly List<LightType> m_NoLightShadows = new List<LightType>();
        readonly HashSet<string> m_ExperimentLogged = new HashSet<string>();
        readonly Dictionary<string, int> m_ExperimentChanges = new Dictionary<string, int>();
        Camera[] m_CamBuffer = new Camera[16];
        float m_NextBadge;
        string m_LastBadgeDigest;

        bool AnyExperiment
        {
            get { return m_CameraHdr || m_ForceForward || m_NoShadows || m_NoLightShadows.Count > 0; }
        }

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
            bool cameraHdr = Has(args, "-portCameraHdr");
            bool forceForward = Has(args, "-portForceForward");
            bool noShadows = Has(args, "-portNoShadows");
            bool hasNoLightShadows = Has(args, "-portNoLightShadows");
            if (string.IsNullOrEmpty(dir) && !noEffects && !cameraHdr && !forceForward && !noShadows && !hasNoLightShadows)
                return;
            var go = new GameObject("PortRenderProbe");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<PortRenderProbe>();
            string only = Arg(args, "-portNoImageEffectsOnly");
            string keep = Arg(args, "-portKeepImageEffects");
            if (!noEffects && (only != null || keep != null))
                Debug.LogWarning("[PortObserve] -portNoImageEffectsOnly / -portKeepImageEffects ignored without -portNoImageEffects");
            s_Instance.m_CameraHdr = cameraHdr;
            s_Instance.m_ForceForward = forceForward;
            s_Instance.m_NoShadows = noShadows;
            if (hasNoLightShadows)
            {
                string types = Arg(args, "-portNoLightShadows");
                if (types == null || types.StartsWith("-", StringComparison.Ordinal))
                    Debug.LogWarning("[PortObserve] -portNoLightShadows needs a list of light types (Spot,Point,Directional)");
                else
                    foreach (string n in TypeList(types))
                    {
                        LightType lt;
                        if (Enum.TryParse(n, true, out lt) && Enum.IsDefined(typeof(LightType), lt))
                        {
                            if (!s_Instance.m_NoLightShadows.Contains(lt)) s_Instance.m_NoLightShadows.Add(lt);
                        }
                        else
                            Debug.LogWarning("[PortObserve] -portNoLightShadows: unknown light type " + n);
                    }
            }
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
            m_NextBadge = Time.realtimeSinceStartup + k_BadgeInterval;
            Debug.Log("[PortObserve] render probe started: file=" + (m_Path ?? "-") + " noImageEffects=" + (noEffects ? "on" : "off")
                      + (noEffects ? " only=" + (m_Only == null ? "*" : string.Join(",", m_Only.ToArray()))
                                     + " keep=" + string.Join(",", m_Keep.ToArray()) : "")
                      + " experiments=" + ExperimentList());
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
            if (AnyExperiment)
                ApplyExperiments(Resources.FindObjectsOfTypeAll<Camera>(), true, "scene " + scene.name);
            if (m_Path != null)
                m_Pending.Add(new KeyValuePair<float, string>(Time.realtimeSinceStartup + k_SceneDelay, "scene " + scene.name));
            WriteEnv("scene load " + scene.name);
            m_PendingEnv.Add(new KeyValuePair<float, string>(Time.realtimeSinceStartup + k_SceneDelay, "scene " + scene.name + " +1s"));
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < m_PendingEnv.Count; i++)
            {
                if (now < m_PendingEnv[i].Key) continue;
                string reason = m_PendingEnv[i].Value;
                m_PendingEnv.RemoveAt(i--);
                WriteEnv(reason);
            }
            if (now >= m_NextBadge)
            {
                m_NextBadge = now + k_BadgeInterval;
                if (AnyExperiment)
                    ApplyExperiments(null, true, "timer");
                if (m_Path != null && BadgeSceneLoaded())
                    WriteBadges();
            }
            if (m_Path == null) return;
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
            if (m_CameraHdr || m_ForceForward)
                ApplyExperiments(null, false, "frame");
        }

        // ---- lighting experiments ----

        string ExperimentList()
        {
            var parts = new List<string>();
            if (m_CameraHdr) parts.Add("-portCameraHdr");
            if (m_ForceForward) parts.Add("-portForceForward");
            if (m_NoShadows) parts.Add("-portNoShadows");
            if (m_NoLightShadows.Count > 0)
            {
                var types = new List<string>();
                foreach (LightType lt in m_NoLightShadows) types.Add(lt.ToString());
                parts.Add("-portNoLightShadows " + string.Join(",", types.ToArray()));
            }
            return parts.Count == 0 ? "none" : string.Join(" ", parts.ToArray());
        }

        void Count(string flag, int changed, string what, string reason)
        {
            int total;
            m_ExperimentChanges.TryGetValue(flag, out total);
            m_ExperimentChanges[flag] = total + changed;
            if (m_ExperimentLogged.Add(flag))
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PortObserve] {0}: {1} ({2}, first applied at {3}) t={4:F2}",
                                        flag, what, changed, reason, Time.realtimeSinceStartup));
        }

        // cameras: null means the enabled ones (Camera.GetAllCameras); withGlobal also does the
        // quality setting and the lights, which need a scene search.
        void ApplyExperiments(Camera[] cameras, bool withGlobal, string reason)
        {
            if (m_CameraHdr || m_ForceForward)
            {
                int count;
                if (cameras == null)
                {
                    if (m_CamBuffer.Length < Camera.allCamerasCount)
                        m_CamBuffer = new Camera[Camera.allCamerasCount + 8];
                    count = Camera.GetAllCameras(m_CamBuffer);
                    cameras = m_CamBuffer;
                }
                else
                    count = cameras.Length;
                int hdr = 0, fwd = 0;
                for (int i = 0; i < count; i++)
                {
                    Camera cam = cameras[i];
                    if (cam == null || !cam.gameObject.scene.IsValid()) continue;
                    if (m_CameraHdr && !cam.allowHDR)
                    {
                        cam.allowHDR = true;
                        hdr++;
                    }
                    if (m_ForceForward && cam.renderingPath != RenderingPath.Forward)
                    {
                        cam.renderingPath = RenderingPath.Forward;
                        fwd++;
                    }
                }
                if (cameras == m_CamBuffer)
                    Array.Clear(m_CamBuffer, 0, count);
                if (m_CameraHdr && (hdr > 0 || !m_ExperimentLogged.Contains("-portCameraHdr")))
                    Count("-portCameraHdr", hdr, "set allowHDR = true on cameras", reason);
                if (m_ForceForward && (fwd > 0 || !m_ExperimentLogged.Contains("-portForceForward")))
                    Count("-portForceForward", fwd, "set renderingPath = Forward on cameras", reason);
            }
            if (!withGlobal) return;
            if (m_NoShadows)
            {
                int n = 0;
                if (QualitySettings.shadows != ShadowQuality.Disable)
                {
                    QualitySettings.shadows = ShadowQuality.Disable;
                    n = 1;
                }
                if (n > 0 || !m_ExperimentLogged.Contains("-portNoShadows"))
                    Count("-portNoShadows", n, "set QualitySettings.shadows = Disable", reason);
            }
            if (m_NoLightShadows.Count > 0)
            {
                int n = 0;
                foreach (Light l in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (l == null || !l.gameObject.scene.IsValid() || !m_NoLightShadows.Contains(l.type)) continue;
                    if (l.shadows == LightShadows.None) continue;
                    l.shadows = LightShadows.None;
                    n++;
                }
                if (n > 0 || !m_ExperimentLogged.Contains("-portNoLightShadows"))
                    Count("-portNoLightShadows", n, "set shadows = None on lights", reason);
            }
        }

        void WriteExperiments(StringBuilder sb)
        {
            sb.Append("{\"cameraHdr\":").Append(m_CameraHdr ? "true" : "false");
            sb.Append(",\"forceForward\":").Append(m_ForceForward ? "true" : "false");
            sb.Append(",\"noShadows\":").Append(m_NoShadows ? "true" : "false");
            sb.Append(",\"noLightShadows\":[");
            for (int i = 0; i < m_NoLightShadows.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Str(sb, m_NoLightShadows[i].ToString());
            }
            sb.Append("],\"changes\":{");
            bool first = true;
            foreach (KeyValuePair<string, int> kv in m_ExperimentChanges)
            {
                if (!first) sb.Append(',');
                first = false;
                Str(sb, kv.Key);
                sb.Append(':').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append("}}");
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

        // ---- environment lighting ----

        static readonly string[] k_BadgeTextures = { "_MainTex", "_MetallicGlossMap", "_BumpMap" };

        void WriteEnv(string reason)
        {
            string line;
            try
            {
                line = CaptureEnv(reason);
            }
            catch (Exception e)
            {
                var err = new StringBuilder(128);
                err.Append("{\"t\":").Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture));
                err.Append(",\"kind\":\"env\",\"reason\":");
                Str(err, reason);
                err.Append(",\"error\":");
                Str(err, e.ToString());
                err.Append('}');
                line = err.ToString();
            }
            Debug.Log("[PortObserve] env (" + reason + "): " + line);
            if (m_Path == null) return;
            try
            {
                File.AppendAllText(m_Path, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortObserve] render probe write failed: " + e.Message);
            }
        }

        static string CaptureEnv(string reason)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"t\":").Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(",\"kind\":\"env\",\"reason\":");
            Str(sb, reason);
            sb.Append(",\"scene\":");
            Str(sb, SceneManager.GetActiveScene().name);
            sb.Append(",\"colorSpace\":");
            Str(sb, QualitySettings.activeColorSpace.ToString());
            sb.Append(",\"lightsUseLinearIntensity\":").Append(GraphicsSettings.lightsUseLinearIntensity ? "true" : "false");
            sb.Append(",\"realtimeReflectionProbes\":").Append(QualitySettings.realtimeReflectionProbes ? "true" : "false");
            sb.Append(",\"envFix\":");
            Str(sb, PortRendering.PortEnvironmentLighting.LastStatus);

            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            sb.Append(",\"ambient\":{\"mode\":");
            Str(sb, RenderSettings.ambientMode.ToString());
            sb.Append(",\"intensity\":").Append(Num(RenderSettings.ambientIntensity));
            sb.Append(",\"light\":");
            Col(sb, RenderSettings.ambientLight);
            sb.Append(",\"sky\":");
            Col(sb, RenderSettings.ambientSkyColor);
            sb.Append(",\"equator\":");
            Col(sb, RenderSettings.ambientEquatorColor);
            sb.Append(",\"ground\":");
            Col(sb, RenderSettings.ambientGroundColor);
            sb.Append(",\"probeInvalid\":").Append(PortRendering.PortEnvironmentLighting.ProbeInvalid(probe) ? "true" : "false");
            sb.Append(",\"probe\":[");
            for (int ch = 0; ch < 3; ch++)
                for (int i = 0; i < 9; i++)
                {
                    if (ch + i > 0) sb.Append(',');
                    sb.Append(Num(probe[ch, i]));
                }
            sb.Append("]}");

            sb.Append(",\"reflection\":{\"mode\":");
            Str(sb, RenderSettings.defaultReflectionMode.ToString());
            sb.Append(",\"resolution\":").Append(RenderSettings.defaultReflectionResolution.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"intensity\":").Append(Num(RenderSettings.reflectionIntensity));
            sb.Append(",\"bounces\":").Append(RenderSettings.reflectionBounces.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"custom\":");
            Tex(sb, RenderSettings.customReflectionTexture);
            sb.Append(",\"default\":");
            Tex(sb, ReflectionProbe.defaultTexture);
            sb.Append('}');

            Material sky = RenderSettings.skybox;
            sb.Append(",\"skybox\":");
            if (sky == null) sb.Append("null");
            else
            {
                sb.Append("{\"material\":");
                Str(sb, sky.name);
                sb.Append(",\"shader\":");
                Str(sb, sky.shader != null ? sky.shader.name : null);
                sb.Append('}');
            }
            sb.Append(",\"sun\":");
            Str(sb, RenderSettings.sun != null ? RenderSettings.sun.name : null);
            LightmapData[] maps = LightmapSettings.lightmaps;
            sb.Append(",\"lightmaps\":").Append((maps != null ? maps.Length : 0).ToString(CultureInfo.InvariantCulture));
            LightProbes lp = LightmapSettings.lightProbes;
            sb.Append(",\"lightProbes\":").Append(lp != null ? lp.count.ToString(CultureInfo.InvariantCulture) : "null");

            sb.Append(",\"reflectionProbes\":[");
            bool firstProbe = true;
            foreach (ReflectionProbe rp in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                if (rp == null) continue;
                if (!firstProbe) sb.Append(',');
                firstProbe = false;
                sb.Append("{\"name\":");
                Str(sb, PathOf(rp.transform));
                sb.Append(",\"enabled\":").Append(rp.enabled ? "true" : "false");
                sb.Append(",\"mode\":");
                Str(sb, rp.mode.ToString());
                sb.Append(",\"importance\":").Append(rp.importance.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"center\":");
                Vec(sb, rp.bounds.center);
                sb.Append(",\"size\":");
                Vec(sb, rp.bounds.size);
                sb.Append(",\"texture\":");
                Tex(sb, rp.texture);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"badges\":[");
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            Camera[] cams = Camera.allCameras;
            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (r == null || r.gameObject.name.IndexOf("WishBadge", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!first) sb.Append(',');
                first = false;
                WriteBadge(sb, r, cams, lights);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static void WriteBadge(StringBuilder sb, Renderer r, Camera[] cams, Light[] lights)
        {
            Transform t = r.transform;
            int layer = r.gameObject.layer;
            Vector3 pos = r.bounds.center;
            sb.Append("{\"path\":");
            Str(sb, PathOf(t));
            sb.Append(",\"active\":").Append(r.gameObject.activeInHierarchy ? "true" : "false");
            sb.Append(",\"enabled\":").Append(r.enabled ? "true" : "false");
            sb.Append(",\"visible\":").Append(r.isVisible ? "true" : "false");
            sb.Append(",\"layer\":").Append(layer.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"lossyScale\":");
            Vec(sb, t.lossyScale);
            sb.Append(",\"position\":");
            Vec(sb, pos);
            sb.Append(",\"forward\":");
            Vec(sb, t.forward);
            sb.Append(",\"lightProbeUsage\":");
            Str(sb, r.lightProbeUsage.ToString());
            sb.Append(",\"reflectionProbeUsage\":");
            Str(sb, r.reflectionProbeUsage.ToString());

            sb.Append(",\"materials\":[");
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (i > 0) sb.Append(',');
                Material m = mats[i];
                if (m == null)
                {
                    sb.Append("null");
                    continue;
                }
                sb.Append("{\"name\":");
                Str(sb, m.name);
                sb.Append(",\"shader\":");
                Str(sb, m.shader != null ? m.shader.name : null);
                sb.Append(",\"keywords\":");
                StrList(sb, m.shaderKeywords);
                sb.Append(",\"textures\":{");
                bool firstTex = true;
                foreach (string prop in k_BadgeTextures)
                {
                    if (!m.HasProperty(prop)) continue;
                    if (!firstTex) sb.Append(',');
                    firstTex = false;
                    Str(sb, prop);
                    sb.Append(':');
                    Tex(sb, m.GetTexture(prop));
                }
                sb.Append("}}");
            }
            sb.Append(']');

            // world normal of the local +Z face (inverse transpose, so a mirroring scale flips it)
            Vector3 normal = t.worldToLocalMatrix.transpose.MultiplyVector(Vector3.forward).normalized;
            sb.Append(",\"cameras\":[");
            bool first = true;
            foreach (Camera cam in cams)
            {
                if (cam == null || !cam.enabled || (cam.cullingMask & (1 << layer)) == 0) continue;
                if (!first) sb.Append(',');
                first = false;
                Vector3 toCam = cam.transform.position - pos;
                float dist = toCam.magnitude;
                Vector3 dir = dist > 1e-6f ? toCam / dist : -cam.transform.forward;
                if (cam.orthographic) dir = -cam.transform.forward;
                sb.Append("{\"name\":");
                Str(sb, cam.name);
                sb.Append(",\"path\":");
                Str(sb, PathOf(cam.transform));
                sb.Append(",\"forwardDot\":").Append(Num(Vector3.Dot(t.forward, dir)));
                sb.Append(",\"normalDot\":").Append(Num(Vector3.Dot(normal, dir)));
                sb.Append(",\"facingCamera\":").Append(Vector3.Dot(normal, dir) > 0f ? "true" : "false");
                sb.Append(",\"distance\":").Append(Num(dist));
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"lights\":[");
            first = true;
            foreach (Light l in lights)
            {
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || (l.cullingMask & (1 << layer)) == 0) continue;
                if (!first) sb.Append(',');
                first = false;
                Vector3 toBadge = pos - l.transform.position;
                float dist = toBadge.magnitude;
                bool inRange, inCone;
                switch (l.type)
                {
                    case LightType.Directional:
                        inRange = true;
                        inCone = true;
                        break;
                    case LightType.Spot:
                        inRange = dist <= l.range;
                        inCone = dist < 1e-6f || Vector3.Angle(l.transform.forward, toBadge) <= l.spotAngle * 0.5f;
                        break;
                    default:
                        inRange = dist <= l.range;
                        inCone = true;
                        break;
                }
                sb.Append("{\"name\":");
                Str(sb, PathOf(l.transform));
                sb.Append(",\"type\":");
                Str(sb, l.type.ToString());
                sb.Append(",\"intensity\":").Append(Num(l.intensity));
                sb.Append(",\"color\":");
                Col(sb, l.color);
                sb.Append(",\"range\":").Append(Num(l.range));
                sb.Append(",\"spotAngle\":").Append(Num(l.spotAngle));
                sb.Append(",\"distance\":").Append(Num(dist));
                sb.Append(",\"inRange\":").Append(inRange ? "true" : "false");
                sb.Append(",\"inCone\":").Append(inCone ? "true" : "false");
                sb.Append(",\"facing\":").Append(l.type == LightType.Directional
                    ? Num(Vector3.Dot(normal, -l.transform.forward))
                    : Num(dist > 1e-6f ? Vector3.Dot(normal, -toBadge / dist) : 0f));
                sb.Append(",\"renderMode\":");
                Str(sb, l.renderMode.ToString());
                sb.Append(",\"shadows\":");
                Str(sb, l.shadows.ToString());
                sb.Append(",\"bounceIntensity\":").Append(Num(l.bounceIntensity));
                sb.Append('}');
            }
            sb.Append("]}");
        }

        // ---- badge lighting (Wish intro) ----

        static bool BadgeSceneLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name == k_BadgeScene) return true;
            }
            return false;
        }

        static bool IsBadgeTarget(Renderer r)
        {
            string n = r.gameObject.name;
            if (n.StartsWith("WishBadge", StringComparison.Ordinal)) return true;
            if (n != "GroundPlane") return false;
            for (Transform p = r.transform.parent; p != null; p = p.parent)
                if (p.name == "WishLogo") return true;
            return false;
        }

        static void Dc(StringBuilder sb, SphericalHarmonicsL2 sh)
        {
            sb.Append('[').Append(Num(sh[0, 0])).Append(',').Append(Num(sh[1, 0])).Append(',').Append(Num(sh[2, 0])).Append(']');
        }

        void WriteBadges()
        {
            if (m_Path == null) return;
            string line, digest;
            try
            {
                line = CaptureBadges(out digest);
            }
            catch (Exception e)
            {
                digest = "error " + e.GetType().Name;
                var err = new StringBuilder(128);
                err.Append("{\"t\":").Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture));
                err.Append(",\"kind\":\"badge\",\"msg\":");
                Str(err, digest);
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
            if (digest != m_LastBadgeDigest)
            {
                m_LastBadgeDigest = digest;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PortObserve] badge: {0} t={1:F2}", digest, Time.realtimeSinceStartup));
            }
        }

        string CaptureBadges(out string digest)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"t\":").Append(Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(",\"kind\":\"badge\"");
            var body = new StringBuilder(4096);
            body.Append(",\"scene\":");
            Str(body, SceneManager.GetActiveScene().name);
            body.Append(",\"experiments\":");
            WriteExperiments(body);
            body.Append(",\"quality\":{\"shadows\":");
            Str(body, QualitySettings.shadows.ToString());
            body.Append(",\"shadowDistance\":").Append(Num(QualitySettings.shadowDistance));
            body.Append(",\"shadowProjection\":");
            Str(body, QualitySettings.shadowProjection.ToString());
            body.Append(",\"pixelLightCount\":").Append(QualitySettings.pixelLightCount.ToString(CultureInfo.InvariantCulture));
            body.Append('}');
            LightProbes lp = LightmapSettings.lightProbes;
            body.Append(",\"lightProbes\":").Append(lp != null ? lp.count.ToString(CultureInfo.InvariantCulture) : "null");
            body.Append(",\"ambientDc\":");
            Dc(body, RenderSettings.ambientProbe);

            Camera[] cams = Camera.allCameras;
            Array.Sort(cams, (a, b) => a.depth.CompareTo(b.depth));
            body.Append(",\"cameras\":[");
            for (int i = 0; i < cams.Length; i++)
            {
                Camera cam = cams[i];
                if (i > 0) body.Append(',');
                body.Append("{\"name\":");
                Str(body, cam.name);
                body.Append(",\"path\":");
                Str(body, PathOf(cam.transform));
                body.Append(",\"enabled\":").Append(cam.enabled ? "true" : "false");
                body.Append(",\"depth\":").Append(Num(cam.depth));
                body.Append(",\"cullingMask\":").Append(cam.cullingMask.ToString(CultureInfo.InvariantCulture));
                body.Append(",\"renderingPath\":");
                Str(body, cam.renderingPath.ToString());
                body.Append(",\"actualRenderingPath\":");
                Str(body, cam.actualRenderingPath.ToString());
                body.Append(",\"allowHDR\":").Append(cam.allowHDR ? "true" : "false");
                body.Append(",\"allowMSAA\":").Append(cam.allowMSAA ? "true" : "false");
                body.Append(",\"targetTexture\":");
                RenderTexture rt = cam.targetTexture;
                if (rt != null) Str(body, string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2}", rt.name, rt.width, rt.height));
                else body.Append("null");
                body.Append('}');
            }
            body.Append(']');

            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            var names = new List<string>();
            body.Append(",\"renderers\":[");
            bool first = true;
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.gameObject.activeInHierarchy || !IsBadgeTarget(r)) continue;
                if (!first) body.Append(',');
                first = false;
                names.Add(r.gameObject.name + (r.isVisible ? "" : "(hidden)"));
                WriteBadgeLighting(body, r, cams, lights);
            }
            body.Append("]}");

            names.Sort(StringComparer.Ordinal);
            digest = "renderers=" + (names.Count == 0 ? "none" : string.Join(",", names.ToArray()))
                     + " shadows=" + QualitySettings.shadows + " experiments=" + ExperimentList();
            sb.Append(",\"msg\":");
            Str(sb, digest);
            sb.Append(body);
            return sb.ToString();
        }

        static void WriteBadgeLighting(StringBuilder sb, Renderer r, Camera[] cams, Light[] lights)
        {
            Transform t = r.transform;
            int layer = r.gameObject.layer;
            Bounds b = r.bounds;
            Vector3 pos = b.center;
            sb.Append("{\"path\":");
            Str(sb, PathOf(t));
            sb.Append(",\"active\":").Append(r.gameObject.activeInHierarchy ? "true" : "false");
            sb.Append(",\"enabled\":").Append(r.enabled ? "true" : "false");
            sb.Append(",\"visible\":").Append(r.isVisible ? "true" : "false");
            sb.Append(",\"layer\":").Append(layer.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"center\":");
            Vec(sb, pos);
            sb.Append(",\"size\":");
            Vec(sb, b.size);
            sb.Append(",\"shadowCasting\":");
            Str(sb, r.shadowCastingMode.ToString());
            sb.Append(",\"receiveShadows\":").Append(r.receiveShadows ? "true" : "false");
            sb.Append(",\"lightProbeUsage\":");
            Str(sb, r.lightProbeUsage.ToString());
            Transform anchor = r.probeAnchor;
            Vector3 probePos = anchor != null ? anchor.position : pos;
            sb.Append(",\"probeAnchor\":");
            Str(sb, anchor != null ? PathOf(anchor) : null);
            sb.Append(",\"probePos\":");
            Vec(sb, probePos);
            SphericalHarmonicsL2 sh;
            LightProbes.GetInterpolatedProbe(probePos, r, out sh);
            sb.Append(",\"shDc\":");
            Dc(sb, sh);

            sb.Append(",\"cameras\":[");
            bool first = true;
            foreach (Camera cam in cams)
            {
                if (cam == null || !cam.enabled || (cam.cullingMask & (1 << layer)) == 0) continue;
                if (!first) sb.Append(',');
                first = false;
                Str(sb, cam.name);
            }
            sb.Append(']');

            Vector3 normal = t.worldToLocalMatrix.transpose.MultiplyVector(Vector3.forward).normalized;
            sb.Append(",\"lights\":[");
            first = true;
            foreach (Light l in lights)
            {
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy) continue;
                if (!first) sb.Append(',');
                first = false;
                Vector3 toR = pos - l.transform.position;
                float dist = toR.magnitude;
                bool directional = l.type == LightType.Directional;
                bool inRange = directional || dist <= l.range;
                bool inCone = l.type != LightType.Spot || dist < 1e-6f
                              || Vector3.Angle(l.transform.forward, toR) <= l.spotAngle * 0.5f;
                float facing = directional ? Vector3.Dot(normal, -l.transform.forward)
                                           : (dist > 1e-6f ? Vector3.Dot(normal, -toR / dist) : 0f);
                sb.Append("{\"name\":");
                Str(sb, PathOf(l.transform));
                sb.Append(",\"type\":");
                Str(sb, l.type.ToString());
                sb.Append(",\"inMask\":").Append((l.cullingMask & (1 << layer)) != 0 ? "true" : "false");
                sb.Append(",\"intensity\":").Append(Num(l.intensity));
                sb.Append(",\"color\":");
                Col(sb, l.color);
                sb.Append(",\"range\":").Append(Num(l.range));
                sb.Append(",\"spotAngle\":").Append(Num(l.spotAngle));
                sb.Append(",\"distance\":").Append(Num(dist));
                sb.Append(",\"inRange\":").Append(inRange ? "true" : "false");
                sb.Append(",\"inCone\":").Append(inCone ? "true" : "false");
                sb.Append(",\"facing\":").Append(Num(facing));
                sb.Append(",\"renderMode\":");
                Str(sb, l.renderMode.ToString());
                sb.Append(",\"shadows\":");
                Str(sb, l.shadows.ToString());
                sb.Append(",\"shadowStrength\":").Append(Num(l.shadowStrength));
                sb.Append(",\"shadowBias\":").Append(Num(l.shadowBias));
                sb.Append(",\"shadowNormalBias\":").Append(Num(l.shadowNormalBias));
                sb.Append(",\"shadowNearPlane\":").Append(Num(l.shadowNearPlane));
                sb.Append(",\"shadowResolution\":");
                Str(sb, l.shadowResolution.ToString());
                LightBakingOutput bake = l.bakingOutput;
                sb.Append(",\"bakeType\":");
                Str(sb, bake.lightmapBakeType.ToString());
                sb.Append(",\"isBaked\":").Append(bake.isBaked ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("]}");
        }

        static void Tex(StringBuilder sb, Texture t)
        {
            if (t == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append("{\"name\":");
            Str(sb, t.name);
            sb.Append(",\"width\":").Append(t.width.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"height\":").Append(t.height.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dimension\":");
            Str(sb, t.dimension.ToString());
            sb.Append('}');
        }

        static void Col(StringBuilder sb, Color c)
        {
            sb.Append('[').Append(Num(c.r)).Append(',').Append(Num(c.g)).Append(',').Append(Num(c.b)).Append(',').Append(Num(c.a)).Append(']');
        }

        static void Vec(StringBuilder sb, Vector3 v)
        {
            sb.Append('[').Append(Num(v.x)).Append(',').Append(Num(v.y)).Append(',').Append(Num(v.z)).Append(']');
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
