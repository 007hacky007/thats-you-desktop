// Join QR code on the TV: the PS4 game told players to open the PlayLink app; the port shows a
// QR code that opens the phone page with this game's address. Shown while the front end
// (FrontEndFlow.s_InFrontend) is active, and anywhere while toggled with J (for re-joining mid-game).

using UnityEngine;

namespace PortNet
{
    public sealed class PortJoinOverlay : MonoBehaviour
    {
        public static bool ForceShow;
        static PortJoinOverlay s_instance;

        bool m_frontEnd;
        GUIStyle m_text, m_small;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Ensure()
        {
            if ((object)s_instance != null || Application.isBatchMode)
                return;
            var go = new GameObject("PortJoinOverlay") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PortJoinOverlay>();
            Debug.Log("[PortNet] join overlay ready");
        }

        void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
            Debug.Log("[PortNet] join overlay destroyed");
        }

        // Automated playthroughs: -portJoinUrlFile <path> receives the join URL once it exists.
        static string s_joinUrlFile = ArgValue("-portJoinUrlFile");
        static string s_written;

        static string ArgValue(string name)
        {
            string[] a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        void Update()
        {
            if (s_joinUrlFile != null)
            {
                string url = PortNetJoin.JoinUrl;
                if (url != null && url != s_written)
                {
                    try
                    {
                        System.IO.File.WriteAllText(s_joinUrlFile + ".tmp", url);
                        System.IO.File.Copy(s_joinUrlFile + ".tmp", s_joinUrlFile, true);
                        s_written = url;
                    }
                    catch (System.Exception e) { Debug.LogWarning("[PortNet] join url file: " + e.Message); }
                }
            }
            if (Input.GetKeyDown(KeyCode.J))
                ForceShow = !ForceShow;
            bool fe = FrontEndFlow.s_InFrontend;
            if (fe != m_frontEnd)
                Debug.Log("[PortNet] join QR " + (fe ? "shown" : "hidden") + " (front end)");
            m_frontEnd = fe;
        }

        void OnGUI()
        {
            if (!(ForceShow || m_frontEnd))
                return;
            if (m_text == null)
            {
                m_text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
                m_text.normal.textColor = Color.black;
                m_small = new GUIStyle(m_text) { fontStyle = FontStyle.Normal };
            }
            float h = Screen.height;
            m_text.fontSize = Mathf.RoundToInt(h * 0.022f);
            m_small.fontSize = Mathf.RoundToInt(h * 0.016f);

            // top right: the game's button bar runs along the bottom edge
            float qr = h * 0.2f, pad = h * 0.012f;
            float w = qr + 2 * pad, ph = qr + 2 * pad + h * 0.09f;
            var box = new Rect(Screen.width - w - h * 0.03f, h * 0.03f, w, ph);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.94f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;

            Texture2D tex = PortNetJoin.QrTexture(4);
            var qrRect = new Rect(box.x + pad, box.y + pad, qr, qr);
            if (tex != null)
            {
                GUI.DrawTexture(qrRect, tex, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(box.x, qrRect.yMax + h * 0.004f, w, h * 0.036f), "Scan to join", m_text);
                int n = PortNetJoin.SessionCount;
                GUI.Label(new Rect(box.x, qrRect.yMax + h * 0.04f, w, h * 0.045f),
                    (n == 1 ? "1 phone connected" : n + " phones connected") + "\nJ shows / hides", m_small);
            }
            else
            {
                string why = PortNetJoin.Problem ?? "starting network...";
                GUI.Label(new Rect(box.x + pad, box.y + pad, qr, ph - 2 * pad),
                    "Phones cannot join yet:\n" + why + "\n\nSet phone_page_url in iwyk-net.json next to the game.", m_small);
            }
        }
    }
}
