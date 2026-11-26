// Restores the environment lighting the scenes lose in Unity 6.
//
// Every scene of the game ships a Unity 5.3 LightingData asset that Unity 6 rejects
// ("LightingDataAsset object at version 3, below the supported minimum"). That asset held the
// baked default reflection cubemap (the skybox reflection used when the reflection source is
// Skybox) and, for skybox ambient, the ambient SH probe. Without it smooth surfaces reflect
// black and skybox-lit scenes lose their ambient term.
//
// After every scene load (for the active scene) this component:
//   1. Reflection: when RenderSettings.defaultReflectionMode is Skybox, a skybox material is set
//      and ReflectionProbe.defaultTexture is missing or tiny, renders the skybox into a
//      mip-mapped HDR cube render texture with a temporary camera (cullingMask 0, clear to
//      skybox), copies it into a Cubemap and installs that as the custom reflection
//      (defaultReflectionMode = Custom). The mip chain is box filtered, an approximation of the
//      convolved chain Unity bakes.
//   2. Ambient: first calls DynamicGI.UpdateEnvironment() and waits a frame, so Unity's own
//      environment update cannot later overwrite what is set here. Then builds
//      RenderSettings.ambientProbe from the environment settings: Flat from the ambient colour,
//      Trilight from the sky / equator / ground gradient, Skybox from the face averages of the
//      rendered skybox (L0, L1 and the axis-aligned L2 terms). Colours are converted to linear
//      in linear colour space and scaled by ambientIntensity (the Unity 5 lighting window applied
//      the intensity to every ambient source). Flat and Trilight are always rebuilt; Skybox is
//      rebuilt unless DynamicGI.UpdateEnvironment() itself produced a new valid probe; any mode
//      is rebuilt when the current probe is all zero or has NaN / infinite coefficients.
//   3. Light probes: every LightProbes asset of the game holds all-zero SH; Unity 5.3 filled them
//      at runtime through realtime GI, which Unity 6 cannot do from this data. Probe-lit
//      renderers then get no ambient at all, because the ambient probe is ignored once light
//      probes exist. When the loaded probe set is entirely zero (or still holds a fill done
//      here earlier), every probe is set to the final RenderSettings.ambientProbe. Sets with any
//      other data are left alone. Re-checked after every scene load (additive loads merge probe
//      sets) and whenever Unity reports the probes changed.
//   4. Reflection probes: Baked (or Custom) probes whose texture is missing are disabled, so the
//      renderers inside them fall back to the scene's default reflection instead of black.
// Captures are cached per skybox material for the session (scenes share skyboxes, and an
// additive scene that becomes active again keeps a valid reflection texture).
// About 1 s after the load it checks once that the probe and reflection it set are still in
// place and logs if something replaced them.
//
// Opt out with the command-line flag -portNoEnvFix (for A/B runs); -portNoProbeFill skips only
// steps 3 and 4. Every decision is logged with
// the prefix "[PortEnv]"; LastStatus holds a one-line summary for the render probe.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PortRendering
{
    public sealed class PortEnvironmentLighting : MonoBehaviour
    {
        const string k_OptOut = "-portNoEnvFix";
        const string k_NoProbeFill = "-portNoProbeFill";
        const float k_RecheckDelay = 1f;
        const float k_ZeroEps = 1e-6f;

        static bool s_NoProbeFill;
        static string s_StatusBase;

        static PortEnvironmentLighting s_Instance;

        // One line describing what was done for the last processed scene ("off" when opted out).
        public static string LastStatus { get; private set; }
        public static bool Active { get { return s_Instance != null; } }
        // Number of light probes last set to the ambient (0 when kept or none), and reflection
        // probes disabled for a missing texture since start.
        public static int LightProbesFilled { get; private set; }
        public static int ReflectionProbesDisabled { get; private set; }

        // One capture per skybox material, kept for the session: scenes share skyboxes, and an
        // additive scene that becomes active again must still find its reflection texture.
        sealed class Capture
        {
            public Texture reflection;    // Cubemap, or the cube render texture without CopyTexture
            public Color[] faces;         // +X, -X, +Y, -Y, +Z, -Z averages, null if unreadable
            public int size;
        }

        readonly Dictionary<Material, Capture> m_Captures = new Dictionary<Material, Capture>();
        readonly HashSet<Texture> m_Ours = new HashSet<Texture>();
        Scene m_LastScene;
        int m_LastFrame = -1;
        int m_Serial;
        bool m_Processing;            // a Process coroutine is between its start and its probe fill
        bool m_ProbesPending;         // re-check the light / reflection probes in the next Update
        bool m_HaveFill;
        SphericalHarmonicsL2 m_FillValue;
        string m_LastProbeLog;

        // calibrated SH basis (9 coefficients each, colour channel independent)
        static float[] s_Const;       // evaluates to 1
        static float[][] s_Lin;       // evaluates to n.x, n.y, n.z
        static float[][] s_Abs;       // evaluates to the L0+L2 projection of |n.x|, |n.y|, |n.z|

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (s_Instance != null) return;
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, k_NoProbeFill, StringComparison.OrdinalIgnoreCase))
                    s_NoProbeFill = true;
            }
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, k_OptOut, StringComparison.OrdinalIgnoreCase))
                {
                    LastStatus = "off (" + k_OptOut + ")";
                    Debug.Log("[PortEnv] environment lighting fix disabled by " + k_OptOut);
                    return;
                }
            }
            var go = new GameObject("PortEnvironmentLighting");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<PortEnvironmentLighting>();
            LastStatus = "waiting for a scene";
            SceneManager.sceneLoaded += s_Instance.OnSceneLoaded;
            SceneManager.activeSceneChanged += s_Instance.OnActiveSceneChanged;
            if (!s_NoProbeFill)
            {
                LightProbes.lightProbesUpdated += s_Instance.OnLightProbesChanged;
                LightProbes.tetrahedralizationCompleted += s_Instance.OnLightProbesChanged;
            }
            Debug.Log("[PortEnv] environment lighting fix active (opt out with " + k_OptOut + ")"
                      + (s_NoProbeFill ? "; light / reflection probe fill disabled by " + k_NoProbeFill
                                       : "; probe fill opt out with " + k_NoProbeFill));
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            LightProbes.lightProbesUpdated -= OnLightProbesChanged;
            LightProbes.tetrahedralizationCompleted -= OnLightProbesChanged;
            if (s_Instance == this) s_Instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // RenderSettings belong to the active scene; an additive load that does not become
            // active does not change them.
            if (mode == LoadSceneMode.Single || scene == SceneManager.GetActiveScene())
                Schedule(SceneManager.GetActiveScene());
            // any load, additive ones included, can bring new (merged) light probes and
            // reflection probes; checked one frame later
            m_ProbesPending = true;
        }

        void OnLightProbesChanged()
        {
            // may fire in the middle of a load or merge; handled in the next Update
            m_ProbesPending = true;
        }

        void Update()
        {
            // while a Process is running it fills the probes itself once the ambient is final
            if (!m_ProbesPending || m_Processing || s_NoProbeFill) return;
            m_ProbesPending = false;
            try
            {
                FixProbes(SceneManager.GetActiveScene().name);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortEnv] probe fill failed: " + e);
            }
        }

        void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (to.IsValid() && to.isLoaded) Schedule(to);
        }

        void Schedule(Scene scene)
        {
            if (scene == m_LastScene && Time.frameCount == m_LastFrame) return;
            m_LastScene = scene;
            m_LastFrame = Time.frameCount;
            m_Processing = true;
            StartCoroutine(Process(scene.name, ++m_Serial));
        }

        IEnumerator Process(string sceneName, int serial)
        {
            // one frame later: the scene's objects have run Awake/Start and may have changed the
            // settings; this runs in Update, before the frame renders
            yield return null;
            if (serial != m_Serial) yield break;
            SphericalHarmonicsL2 unityBefore = RenderSettings.ambientProbe;
            DynamicGI.UpdateEnvironment();
            yield return null;
            if (serial != m_Serial) yield break;
            // a valid probe that the environment update changed means Unity computed it live
            bool unityUpdated = !ProbeInvalid(RenderSettings.ambientProbe) && !SameProbe(unityBefore, RenderSettings.ambientProbe);
            SphericalHarmonicsL2 setProbe;
            bool probeSet;
            Texture setReflection;
            try
            {
                Apply(sceneName, unityUpdated, out probeSet, out setProbe, out setReflection);
            }
            catch (Exception e)
            {
                LastStatus = "error " + e.GetType().Name + " in " + sceneName;
                s_StatusBase = LastStatus;
                Debug.LogWarning("[PortEnv] " + sceneName + ": failed: " + e);
                m_Processing = false;
                yield break;
            }
            s_StatusBase = LastStatus;

            // the ambient probe is final now: fill zero light probes with it
            m_Processing = false;
            m_ProbesPending = false;
            if (!s_NoProbeFill)
            {
                try
                {
                    FixProbes(sceneName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PortEnv] " + sceneName + ": probe fill failed: " + e);
                }
            }
            RefreshStatus();

            yield return new WaitForSecondsRealtime(k_RecheckDelay);
            if (serial != m_Serial) yield break;
            // a late merge or reload of the probe data shows up here
            if (!s_NoProbeFill) m_ProbesPending = true;
            if (probeSet && !SameProbe(RenderSettings.ambientProbe, setProbe))
                Debug.LogWarning("[PortEnv] " + sceneName + ": ambient probe was replaced after it was set: now "
                                 + ProbeString(RenderSettings.ambientProbe));
            if (setReflection != null && (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom
                                          || RenderSettings.customReflectionTexture != setReflection))
                Debug.LogWarning("[PortEnv] " + sceneName + ": custom reflection was replaced after it was set: mode="
                                 + RenderSettings.defaultReflectionMode + " texture=" + TexString(RenderSettings.customReflectionTexture));
        }

        void Apply(string sceneName, bool unityUpdated, out bool probeSet, out SphericalHarmonicsL2 setProbe, out Texture setReflection)
        {
            probeSet = false;
            setProbe = new SphericalHarmonicsL2();
            setReflection = null;
            var log = new StringBuilder(256);
            log.Append("[PortEnv] ").Append(sceneName).Append(':');

            AmbientMode ambientMode = RenderSettings.ambientMode;
            Material sky = RenderSettings.skybox;
            Texture def = ReflectionProbe.defaultTexture;
            bool defValid = def != null && def.width >= 16 && !m_Ours.Contains(def);
            bool wantReflection = RenderSettings.defaultReflectionMode == DefaultReflectionMode.Skybox && sky != null && !defValid;
            bool wantSkyAmbient = ambientMode == AmbientMode.Skybox && sky != null && !unityUpdated;
            SphericalHarmonicsL2 before = RenderSettings.ambientProbe;
            bool probeBad = ProbeInvalid(before);

            log.Append(" ambientMode=").Append(ambientMode)
               .Append(" reflectionMode=").Append(RenderSettings.defaultReflectionMode)
               .Append(" skybox=").Append(sky != null ? sky.name : "null")
               .Append(" defaultTexture=").Append(TexString(def))
               .Append(" probeBefore=").Append(probeBad ? "invalid" : "ok")
               .Append(unityUpdated ? " (updated by DynamicGI)" : "");

            // ---- skybox capture (reflection and / or skybox ambient) ----
            Color[] faces = null;
            if (wantReflection || wantSkyAmbient)
            {
                int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(16, RenderSettings.defaultReflectionResolution)), 16, 1024);
                Capture cap = GetCapture(sky, size, log);
                if (cap != null)
                {
                    faces = cap.faces;
                    if (wantReflection)
                    {
                        if (cap.reflection != null)
                        {
                            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                            RenderSettings.customReflectionTexture = cap.reflection;
                            setReflection = cap.reflection;
                            log.Append(" reflection=custom ").Append(TexString(cap.reflection));
                        }
                        else
                        {
                            log.Append(" reflection=failed");
                        }
                    }
                }
            }
            else if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Skybox)
            {
                log.Append(defValid ? " reflection=kept" : " reflection=none (no skybox)");
            }

            // ---- ambient probe ----
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            float intensity = RenderSettings.ambientIntensity;
            bool haveProbe = false;
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            string source = null;
            switch (ambientMode)
            {
                case AmbientMode.Flat:
                    probe = FlatProbe(ToWorking(RenderSettings.ambientSkyColor, linear) * intensity);
                    haveProbe = true;
                    source = "flat";
                    break;
                case AmbientMode.Trilight:
                    probe = TrilightProbe(ToWorking(RenderSettings.ambientSkyColor, linear) * intensity,
                                          ToWorking(RenderSettings.ambientEquatorColor, linear) * intensity,
                                          ToWorking(RenderSettings.ambientGroundColor, linear) * intensity);
                    haveProbe = true;
                    source = "trilight";
                    break;
                case AmbientMode.Skybox:
                    if (unityUpdated)
                        source = "kept (live skybox ambient from DynamicGI.UpdateEnvironment)";
                    else if (faces != null)
                    {
                        var scaled = new Color[6];
                        for (int i = 0; i < 6; i++) scaled[i] = faces[i] * intensity;
                        probe = SkyboxProbe(scaled);
                        haveProbe = true;
                        source = "skybox";
                    }
                    break;
            }
            if (!haveProbe && probeBad)
            {
                // custom mode or a failed skybox render with a broken probe: fall back to the
                // ambient colour so the scene is not left without ambient light
                probe = FlatProbe(ToWorking(RenderSettings.ambientSkyColor, linear) * intensity);
                haveProbe = true;
                source = "fallback flat";
            }
            if (haveProbe && !ProbeInvalid(probe))
            {
                RenderSettings.ambientProbe = probe;
                probeSet = true;
                setProbe = probe;
                log.Append(" ambient=").Append(source).Append(" before=").Append(ProbeString(before))
                   .Append(" after=").Append(ProbeString(probe));
                if (!SameProbe(RenderSettings.ambientProbe, probe))
                    log.Append(" (readback differs: ").Append(ProbeString(RenderSettings.ambientProbe)).Append(')');
            }
            else
            {
                log.Append(" ambient=").Append(source ?? "kept").Append(' ').Append(ProbeString(before));
            }

            LastStatus = string.Format(CultureInfo.InvariantCulture, "{0}: reflection={1} ambient={2}", sceneName,
                                       setReflection != null ? "custom " + TexString(setReflection) : (defValid ? "default" : "none"),
                                       probeSet ? source : "kept");
            Debug.Log(log.ToString());
        }

        // ---- light and reflection probes ----

        void FixProbes(string sceneName)
        {
            FillLightProbes(sceneName);
            DisableEmptyReflectionProbes(sceneName);
            RefreshStatus();
        }

        void FillLightProbes(string sceneName)
        {
            LightProbes lp = LightmapSettings.lightProbes;
            SphericalHarmonicsL2[] baked = lp != null ? lp.bakedProbes : null;
            if (baked == null || baked.Length == 0)
            {
                LightProbesFilled = 0;
                ProbeLog(sceneName, "lightProbes none");
                return;
            }

            // every probe must be zero or hold the fill done here, otherwise it is real data
            bool allFillable = true, allOurs = m_HaveFill;
            for (int p = 0; p < baked.Length && allFillable; p++)
            {
                bool zero = SumAbs(baked[p]) < k_ZeroEps;
                bool ours = m_HaveFill && SameProbe(baked[p], m_FillValue);
                if (!ours) allOurs = false;
                if (!zero && !ours) allFillable = false;
            }
            if (!allFillable)
            {
                LightProbesFilled = 0;
                ProbeLog(sceneName, "lightProbes kept (nonzero) count=" + baked.Length);
                return;
            }

            SphericalHarmonicsL2 ambient = RenderSettings.ambientProbe;
            if (ProbeInvalid(ambient))
            {
                LightProbesFilled = 0;
                ProbeLog(sceneName, "lightProbes left zero (ambient probe invalid " + ProbeString(ambient) + ") count=" + baked.Length);
                return;
            }
            if (allOurs && SameProbe(m_FillValue, ambient)) return;   // already filled with this ambient

            var arr = new SphericalHarmonicsL2[baked.Length];
            for (int p = 0; p < arr.Length; p++) arr[p] = ambient;
            lp.bakedProbes = arr;
            m_HaveFill = true;
            m_FillValue = ambient;
            LightProbesFilled = arr.Length;
            m_LastProbeLog = null;
            ProbeLog(sceneName, "lightProbes filled " + arr.Length + " with ambient " + ProbeString(ambient));
        }

        void DisableEmptyReflectionProbes(string sceneName)
        {
            ReflectionProbe[] probes = FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            var names = new StringBuilder();
            foreach (ReflectionProbe rp in probes)
            {
                if (rp == null || !rp.enabled) continue;
                bool empty = (rp.mode == ReflectionProbeMode.Baked && rp.bakedTexture == null)
                             || (rp.mode == ReflectionProbeMode.Custom && rp.customBakedTexture == null);
                if (!empty) continue;
                rp.enabled = false;
                n++;
                if (names.Length < 200) names.Append(names.Length > 0 ? ", " : "").Append(rp.name).Append(" (").Append(rp.mode).Append(')');
            }
            if (n == 0) return;
            ReflectionProbesDisabled += n;
            Debug.Log("[PortEnv] " + sceneName + ": disabled " + n + " reflection probe(s) without a texture, renderers fall back to the default reflection: " + names);
        }

        void ProbeLog(string sceneName, string what)
        {
            string line = "[PortEnv] " + sceneName + ": " + what;
            if (line == m_LastProbeLog) return;   // the probe events can repeat the same result
            m_LastProbeLog = line;
            Debug.Log(line);
        }

        static void RefreshStatus()
        {
            if (s_NoProbeFill)
            {
                if (s_StatusBase != null) LastStatus = s_StatusBase + " lightProbesFilled=off";
                return;
            }
            LastStatus = (s_StatusBase ?? "waiting for a scene") + " lightProbesFilled=" + LightProbesFilled
                         + (ReflectionProbesDisabled > 0 ? " reflectionProbesDisabled=" + ReflectionProbesDisabled : "");
        }

        static float SumAbs(SphericalHarmonicsL2 sh)
        {
            float s = 0f;
            for (int ch = 0; ch < 3; ch++)
                for (int i = 0; i < 9; i++)
                {
                    float v = sh[ch, i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) return float.PositiveInfinity;
                    s += Mathf.Abs(v);
                }
            return s;
        }

        // ---- skybox capture ----

        Capture GetCapture(Material sky, int size, StringBuilder log)
        {
            Capture cap;
            if (m_Captures.TryGetValue(sky, out cap) && cap.size == size
                && (cap.reflection != null || cap.faces != null))
            {
                log.Append(" capture=cached");
                return cap;
            }
            RenderTexture rt = RenderSkybox(size);
            if (rt == null)
            {
                log.Append(" capture=failed");
                return null;
            }
            cap = new Capture { size = size };
            cap.faces = ReadFaceAverages(rt);
            bool keptRT;
            cap.reflection = MakeReflection(rt, size, out keptRT);
            if (!keptRT) ReleaseRT(rt);
            if (cap.reflection != null) m_Ours.Add(cap.reflection);
            m_Captures[sky] = cap;
            log.Append(" capture=new").Append(keptRT ? " (render texture kept)" : "")
               .Append(cap.faces != null ? "" : " (no face averages)");
            return cap;
        }

        static RenderTexture RenderSkybox(int size)
        {
            var desc = new RenderTextureDescriptor(size, size, GraphicsFormat.R16G16B16A16_SFloat, 24);
            desc.dimension = TextureDimension.Cube;
            desc.useMipMap = true;
            desc.autoGenerateMips = true;
            desc.msaaSamples = 1;
            var rt = new RenderTexture(desc);
            rt.name = "PortSkyboxReflectionRT";
            rt.hideFlags = HideFlags.HideAndDontSave;
            rt.filterMode = FilterMode.Trilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            if (!rt.Create())
            {
                Debug.LogWarning("[PortEnv] cannot create the skybox cube render texture");
                DestroyImmediate(rt);
                return null;
            }

            var go = new GameObject("PortSkyboxCapture");
            go.hideFlags = HideFlags.HideAndDontSave;
            bool ok = false;
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = Color.black;
                cam.renderingPath = RenderingPath.Forward;
                cam.allowHDR = true;
                cam.allowMSAA = false;
                cam.useOcclusionCulling = false;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000f;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ok = cam.RenderToCubemap(rt, 63);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortEnv] skybox capture failed: " + e.Message);
            }
            finally
            {
                DestroyImmediate(go);
            }
            if (!ok)
            {
                Debug.LogWarning("[PortEnv] Camera.RenderToCubemap returned false");
                ReleaseRT(rt);
                return null;
            }
            return rt;
        }

        // GPU copy into a Cubemap so it can be used as RenderSettings.customReflectionTexture.
        static Texture MakeReflection(RenderTexture rt, int size, out bool keptRT)
        {
            keptRT = false;
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.RTToTexture) != 0)
            {
                try
                {
                    var cube = new Cubemap(size, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.MipChain);
                    cube.name = "PortSkyboxReflection";
                    cube.hideFlags = HideFlags.HideAndDontSave;
                    cube.filterMode = FilterMode.Trilinear;
                    cube.wrapMode = TextureWrapMode.Clamp;
                    int mips = Mathf.Min(cube.mipmapCount, rt.mipmapCount);
                    if (mips == cube.mipmapCount && mips == rt.mipmapCount)
                    {
                        Graphics.CopyTexture(rt, cube);
                    }
                    else
                    {
                        for (int face = 0; face < 6; face++)
                            for (int mip = 0; mip < mips; mip++)
                                Graphics.CopyTexture(rt, face, mip, cube, face, mip);
                    }
                    return cube;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PortEnv] copy into a Cubemap failed, using the render texture: " + e.Message);
                }
            }
            keptRT = true;
            return rt;
        }

        // Average colour of each face (+X, -X, +Y, -Y, +Z, -Z) from the 1x1 top of the mip chain.
        static Color[] ReadFaceAverages(RenderTexture rt)
        {
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return null;
            int mip = rt.mipmapCount - 1;
            int w = Mathf.Max(1, rt.width >> mip);
            var tmp = new RenderTexture(w, w, 0, GraphicsFormat.R16G16B16A16_SFloat);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            var tex = new Texture2D(w, w, TextureFormat.RGBAFloat, false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            RenderTexture prev = RenderTexture.active;
            var faces = new Color[6];
            try
            {
                tmp.Create();
                for (int face = 0; face < 6; face++)
                {
                    Graphics.CopyTexture(rt, face, mip, tmp, 0, 0);
                    RenderTexture.active = tmp;
                    tex.ReadPixels(new Rect(0, 0, w, w), 0, 0, false);
                    Color sum = Color.clear;
                    Color[] px = tex.GetPixels();
                    foreach (Color c in px) sum += c;
                    faces[face] = sum / px.Length;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortEnv] reading the skybox face averages failed: " + e.Message);
                faces = null;
            }
            finally
            {
                RenderTexture.active = prev;
                ReleaseRT(tmp);
                DestroyImmediate(tex);
            }
            return faces;
        }

        static void ReleaseRT(RenderTexture rt)
        {
            if (rt == null) return;
            if (RenderTexture.active == rt) RenderTexture.active = null;
            rt.Release();
            Destroy(rt);
        }

        // ---- SH construction ----
        //
        // The basis is taken from Unity's own SphericalHarmonicsL2 helpers, so it matches whatever
        // normalisation the built-in shaders expect: AddAmbientLight(white) evaluates to 1 and
        // AddDirectionalLight(d, white, 1) to the L2 projection of max(0, n.d). For an axis a,
        // D(+a) - D(-a) is exactly n.a and D(+a) + D(-a) is the projection of |n.a|,
        // which is 3/16 + 15/16 (n.a)^2, so (n.a)^2 = 16/15 (D(+a) + D(-a)) - 1/5.

        static void Calibrate()
        {
            if (s_Const != null) return;
            var amb = new SphericalHarmonicsL2();
            amb.AddAmbientLight(Color.white);
            s_Const = Coeffs(amb);
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            s_Lin = new float[3][];
            s_Abs = new float[3][];
            for (int a = 0; a < 3; a++)
            {
                var p = new SphericalHarmonicsL2();
                p.AddDirectionalLight(axes[a], Color.white, 1f);
                var m = new SphericalHarmonicsL2();
                m.AddDirectionalLight(-axes[a], Color.white, 1f);
                float[] cp = Coeffs(p), cm = Coeffs(m);
                s_Lin[a] = new float[9];
                s_Abs[a] = new float[9];
                for (int i = 0; i < 9; i++)
                {
                    s_Lin[a][i] = cp[i] - cm[i];
                    s_Abs[a][i] = cp[i] + cm[i];
                }
            }
            // the DC term of |n.a| is exactly 1/2; rescale if Unity's directional and ambient
            // helpers use a different relative normalisation
            float dc = s_Abs[1][0];
            if (Mathf.Abs(dc) > 1e-8f && Mathf.Abs(s_Const[0]) > 1e-8f)
            {
                float scale = 0.5f * s_Const[0] / dc;
                if (Mathf.Abs(scale - 1f) > 1e-3f)
                {
                    Debug.Log("[PortEnv] rescaling the directional SH basis by " + scale.ToString("0.####", CultureInfo.InvariantCulture));
                    for (int a = 0; a < 3; a++)
                        for (int i = 0; i < 9; i++)
                        {
                            s_Lin[a][i] *= scale;
                            s_Abs[a][i] *= scale;
                        }
                }
            }
        }

        static float[] Coeffs(SphericalHarmonicsL2 sh)
        {
            var c = new float[9];
            for (int i = 0; i < 9; i++) c[i] = sh[0, i];
            return c;
        }

        static void AddTerm(ref SphericalHarmonicsL2 sh, float[] basis, Color c)
        {
            for (int i = 0; i < 9; i++)
            {
                sh[0, i] += basis[i] * c.r;
                sh[1, i] += basis[i] * c.g;
                sh[2, i] += basis[i] * c.b;
            }
        }

        static SphericalHarmonicsL2 FlatProbe(Color c)
        {
            Calibrate();
            var sh = new SphericalHarmonicsL2();
            AddTerm(ref sh, s_Const, c);
            return sh;
        }

        // f(n.y) passes through ground at -1, equator at 0 and sky at +1:
        // f = equator + p y + q y^2, p = (sky - ground) / 2, q = (sky + ground) / 2 - equator.
        static SphericalHarmonicsL2 TrilightProbe(Color sky, Color equator, Color ground)
        {
            Calibrate();
            Color p = (sky - ground) * 0.5f;
            Color q = (sky + ground) * 0.5f - equator;
            var sh = new SphericalHarmonicsL2();
            AddTerm(ref sh, s_Const, equator - q * 0.2f);
            AddTerm(ref sh, s_Lin[1], p);
            AddTerm(ref sh, s_Abs[1], q * (16f / 15f));
            return sh;
        }

        // Radiance model fitted to the six face averages: L = a + b.w + sum_k q_k (w_k^2 - 1/3)
        // with sum q = 0. Over a face (uniform texel weights, as the box-filtered mip averages)
        // the mean of w.axis is k1, of (w.axis)^2 is m2 and of another component squared is mo.
        // Then a = mean of the faces, b_k = (F+k - F-k) / (2 k1), q_k = ((F+k + F-k) / 2 - a) / (m2 - mo).
        // Cosine convolution (divided by pi) keeps band 0, scales band 1 by 2/3 and band 2 by 1/4:
        // E/pi = a + 2/3 b.n + 1/4 sum q_k (n_k^2 - 1/3) = a + 2/3 b.n + 4/15 sum q_k |n_k|proj.
        static SphericalHarmonicsL2 SkyboxProbe(Color[] f)
        {
            Calibrate();
            float k1, m2, mo;
            FaceMoments(out k1, out m2, out mo);
            Color a = Color.clear;
            for (int i = 0; i < 6; i++) a += f[i];
            a /= 6f;
            var sh = new SphericalHarmonicsL2();
            AddTerm(ref sh, s_Const, a);
            for (int k = 0; k < 3; k++)
            {
                Color pos = f[2 * k], neg = f[2 * k + 1];
                Color b = (pos - neg) / (2f * k1);
                Color q = ((pos + neg) * 0.5f - a) / (m2 - mo);
                AddTerm(ref sh, s_Lin[k], b * (2f / 3f));
                AddTerm(ref sh, s_Abs[k], q * (4f / 15f));
            }
            return sh;
        }

        static void FaceMoments(out float k1, out float m2, out float mo)
        {
            const int n = 64;
            double s1 = 0, s2 = 0, so = 0;
            for (int y = 0; y < n; y++)
            {
                double v = (y + 0.5) / n * 2 - 1;
                for (int x = 0; x < n; x++)
                {
                    double u = (x + 0.5) / n * 2 - 1;
                    double r2 = 1 + u * u + v * v;
                    s1 += 1 / Math.Sqrt(r2);
                    s2 += 1 / r2;
                    so += u * u / r2;
                }
            }
            double count = n * n;
            k1 = (float)(s1 / count);
            m2 = (float)(s2 / count);
            mo = (float)(so / count);
        }

        static Color ToWorking(Color c, bool linear)
        {
            Color w = linear ? c.linear : c;
            w.a = 1f;
            return w;
        }

        // ---- helpers ----

        public static bool ProbeInvalid(SphericalHarmonicsL2 sh)
        {
            bool allZero = true;
            for (int ch = 0; ch < 3; ch++)
                for (int i = 0; i < 9; i++)
                {
                    float v = sh[ch, i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) return true;
                    if (Mathf.Abs(v) > 1e-6f) allZero = false;
                }
            return allZero;
        }

        static bool SameProbe(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            for (int ch = 0; ch < 3; ch++)
                for (int i = 0; i < 9; i++)
                    if (!(Mathf.Abs(a[ch, i] - b[ch, i]) <= 1e-4f)) return false;
            return true;
        }

        static string ProbeString(SphericalHarmonicsL2 sh)
        {
            // DC colour as the shader evaluates it, which is the easiest number to compare
            Calibrate();
            float k = Mathf.Abs(s_Const[0]) > 1e-8f ? 1f / s_Const[0] : 0f;
            return string.Format(CultureInfo.InvariantCulture, "dc({0:0.####},{1:0.####},{2:0.####})",
                                 sh[0, 0] * k, sh[1, 0] * k, sh[2, 0] * k);
        }

        static string TexString(Texture t)
        {
            if (t == null) return "null";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2} {3}", t.name, t.width, t.height, t.dimension);
        }
    }
}
