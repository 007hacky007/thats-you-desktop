// Window behaviour of the desktop port.
// - The game was made for a 16:9 TV: when the player window is resized, it snaps back to 16:9
//   (keeping the edge the user dragged), once the size has been stable for a moment.
// - -portMute silences all output (automated test runs).

using System;
using UnityEngine;

namespace PortRuntime
{
    public sealed class PortWindow : MonoBehaviour
    {
        public const float Aspect = 16f / 9f;
        static PortWindow s_instance;

        int m_w, m_h;          // last size we saw
        int m_setW, m_setH;    // last size we applied (or accepted)
        float m_stableSince;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-portMute") >= 0)
            {
                AudioListener.volume = 0f;
                Debug.Log("[PortWindow] muted (-portMute)");
            }
            Ensure();
        }

        public static void Ensure()
        {
            if ((object)s_instance != null || Application.isBatchMode || Application.isEditor)
                return;
            var go = new GameObject("PortWindow") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PortWindow>();
        }

        void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }

        // Size that restores 16:9: keep the dimension that changed most since the last applied size.
        public static void Target(int w, int h, int lastW, int lastH, int maxW, int maxH, out int tw, out int th)
        {
            bool keepWidth = Math.Abs(w - lastW) * 9 >= Math.Abs(h - lastH) * 16;
            if (keepWidth) { tw = w; th = (int)Math.Round(w / Aspect); }
            else { th = h; tw = (int)Math.Round(h * Aspect); }
            if (maxW > 0 && maxH > 0 && (tw > maxW || th > maxH))
            {
                float s = Math.Min((float)maxW / tw, (float)maxH / th);
                tw = (int)(tw * s);
                th = (int)Math.Round(tw / Aspect);
            }
        }

        void Update()
        {
            PortMuteGuard();
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
                return;
            int w = Screen.width, h = Screen.height;
            if (m_setW == 0) { m_setW = w; m_setH = h; }
            if (w != m_w || h != m_h)
            {
                m_w = w; m_h = h;
                m_stableSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - m_stableSince < 0.3f)
                return;
            if (Math.Abs((float)w / h - Aspect) < 0.01f)
            {
                m_setW = w; m_setH = h;
                return;
            }
            var res = Screen.currentResolution;
            Target(w, h, m_setW, m_setH, res.width, res.height - 60, out int tw, out int th);
            if (tw == w && th == h)
                return;
            m_setW = tw; m_setH = th;
            Screen.SetResolution(tw, th, FullScreenMode.Windowed);
        }

        // Some scenes set AudioListener.volume themselves; keep the mute.
        static void PortMuteGuard()
        {
            if (s_muted && AudioListener.volume != 0f)
                AudioListener.volume = 0f;
        }

        static readonly bool s_muted = Array.IndexOf(Environment.GetCommandLineArgs(), "-portMute") >= 0;
    }
}
