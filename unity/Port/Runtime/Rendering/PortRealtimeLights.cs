// Restores realtime direct lighting for lights that Unity 6 treats as baked.
//
// The scenes ship Unity 5.3 LightingData assets that Unity 6 rejects (version 3, below the
// supported minimum), so no lightmaps are loaded. The lights still come up with a baking output
// of {lightmapBakeType = Baked, isBaked = true}, and Unity skips the direct contribution of any
// light whose baking output says it is baked, expecting the lightmap to supply it. With no
// lightmap the result is that the lights contribute nothing at all in any render path.
//
// While no lightmaps are loaded, this component rewrites the baking output of every such light
// to a plain realtime one (not baked, Realtime bake type, no occlusion mask channel), which is
// what the scenes serialise for most of their lights anyway. Mixed lights lose their baked part
// together with the lightmaps, so they are made fully realtime too.
//
// Runs after every scene load and on a periodic rescan, so lights instantiated or enabled later
// are caught as well. Each light is fixed and counted once. Logs one line per scene with the
// prefix "[PortLights]". Opt out with the command-line flag -portNoLightFix.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PortRendering
{
    public sealed class PortRealtimeLights : MonoBehaviour
    {
        const string k_OptOut = "-portNoLightFix";
        const float k_RescanInterval = 1f;

        static PortRealtimeLights s_Instance;

        // One line describing the last scan that changed something ("off" when opted out).
        public static string LastStatus { get; private set; }
        public static bool Active { get { return s_Instance != null; } }
        // Lights whose baking output was rewritten since start.
        public static int TotalFixed { get; private set; }

        // lights already rewritten (UnityEngine.Object hashes by its instance identity)
        readonly HashSet<Light> m_Fixed = new HashSet<Light>();
        float m_NextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (s_Instance != null) return;
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, k_OptOut, StringComparison.OrdinalIgnoreCase))
                {
                    LastStatus = "off (" + k_OptOut + ")";
                    Debug.Log("[PortLights] realtime light fix disabled by " + k_OptOut);
                    return;
                }
            }
            var go = new GameObject("PortRealtimeLights");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<PortRealtimeLights>();
            LastStatus = "waiting for a scene";
            SceneManager.sceneLoaded += s_Instance.OnSceneLoaded;
            Debug.Log("[PortLights] realtime light fix active (opt out with " + k_OptOut + ")");
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (s_Instance == this) s_Instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_Fixed.RemoveWhere(l => l == null);   // drop lights destroyed with old scenes
            Scan(true, scene);
            m_NextScan = Time.unscaledTime + k_RescanInterval;
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextScan) return;
            m_NextScan = Time.unscaledTime + k_RescanInterval;
            Scan(false, default(Scene));
        }

        static bool NeedsFix(Light light)
        {
            LightBakingOutput o = light.bakingOutput;
            return o.isBaked || o.lightmapBakeType != LightmapBakeType.Realtime;
        }

        // loadedScene is valid only for a scan triggered by a scene load: that scene always gets
        // a log line (also when nothing needed fixing); a periodic scan logs only scenes in which
        // it fixed something.
        void Scan(bool onLoad, Scene loadedScene)
        {
            Light[] lights;
            try
            {
                lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PortLights] light scan failed: " + e.Message);
                return;
            }

            bool haveLightmaps = LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length > 0;

            // per scene name: [fixed now, total lights, already fixed earlier]
            var perScene = new Dictionary<string, int[]>();
            var order = new List<string>();
            if (onLoad && loadedScene.IsValid())
            {
                perScene[loadedScene.name] = new int[3];
                order.Add(loadedScene.name);
            }

            foreach (Light light in lights)
            {
                if (light == null) continue;
                string sceneName = light.gameObject.scene.IsValid() ? light.gameObject.scene.name : "(none)";
                int[] c;
                if (!perScene.TryGetValue(sceneName, out c))
                {
                    c = new int[3];
                    perScene[sceneName] = c;
                    order.Add(sceneName);
                }
                c[1]++;

                if (m_Fixed.Contains(light))
                {
                    // rewritten earlier; re-apply if something set it back, without counting again
                    if (!haveLightmaps && NeedsFix(light)) Apply(light);
                    c[2]++;
                    continue;
                }
                if (haveLightmaps || !NeedsFix(light)) continue;

                try
                {
                    Apply(light);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PortLights] could not set baking output of '" + light.name + "': " + e.Message);
                    continue;
                }
                m_Fixed.Add(light);
                c[0]++;
                TotalFixed++;
            }

            foreach (string name in order)
            {
                int[] c = perScene[name];
                bool isLoaded = onLoad && loadedScene.IsValid() && name == loadedScene.name;
                if (c[0] == 0 && !isLoaded) continue;
                string line = "[PortLights] " + name + ": fixed " + c[0] + " of " + c[1]
                              + " lights (was Baked/isBaked)";
                if (c[2] > 0) line += ", " + c[2] + " fixed earlier";
                if (!onLoad) line += " [rescan]";
                if (haveLightmaps && isLoaded) line += "; lightmaps present (" + LightmapSettings.lightmaps.Length + "), left as is";
                Debug.Log(line);
                LastStatus = line.Substring("[PortLights] ".Length) + "; total " + TotalFixed;
            }
        }

        static void Apply(Light light)
        {
            light.bakingOutput = new LightBakingOutput
            {
                isBaked = false,
                lightmapBakeType = LightmapBakeType.Realtime,
                mixedLightingMode = MixedLightingMode.IndirectOnly,
                occlusionMaskChannel = -1,
                probeOcclusionLightIndex = -1,
            };
        }
    }
}
