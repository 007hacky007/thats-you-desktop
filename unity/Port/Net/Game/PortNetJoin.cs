// Static API for the join screen (contract 4.8). The game UI that shows the QR code is a later
// step; this only exposes the state and builds the texture.

using UnityEngine;

namespace PortNet
{
    public static class PortNetJoin
    {
        static Texture2D s_tex;
        static string s_texUrl;
        static int s_texPpm;

        static NetEngine Engine => PortNetHost.Current;

        public static bool Running => Engine != null && Engine.Running;
        public static string JoinUrl => Engine?.JoinUrl;
        public static string Problem => Engine == null ? "network not started" : Engine.Problem;
        public static bool UsesTunnel => Engine != null && Engine.Config.PageHost == NetConfig.PageHostTunnel;
        public static string Endpoint => Engine?.Endpoint;
        public static int SessionCount => Engine == null ? 0 : Engine.SessionCount;

        // QR code of JoinUrl: dark modules black, a 4-module white quiet zone, point filtered.
        // Cached per URL and size; null when there is no join URL.
        public static Texture2D QrTexture(int pixelsPerModule = 8)
        {
            string url = JoinUrl;
            if (url == null || pixelsPerModule < 1)
                return null;
            if (s_tex != null && s_texUrl == url && s_texPpm == pixelsPerModule)
                return s_tex;
            if (!Engine.TryGetQr(out byte[] modules, out int side))
                return null;
            DropTexture();

            int q = NetLimits.QuietZoneModules;
            int n = (side + 2 * q) * pixelsPerModule;
            var px = new Color32[n * n];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int y = 0; y < n; y++)
            {
                int my = side - 1 - (y / pixelsPerModule - q);  // texture rows go bottom-up
                for (int x = 0; x < n; x++)
                {
                    int mx = x / pixelsPerModule - q;
                    bool dark = mx >= 0 && mx < side && my >= 0 && my < side && modules[my * side + mx] != 0;
                    px[y * n + x] = dark ? black : white;
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "PortNet join QR",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            s_tex = tex;
            s_texUrl = url;
            s_texPpm = pixelsPerModule;
            return tex;
        }

        // Persists the page address to the user config file and recomputes JoinUrl.
        public static void SetPageUrl(string url)
        {
            PortNetHost.SetPageUrl(url);
            DropTexture();
        }

        static void DropTexture()
        {
            if (s_tex != null)
                UnityEngine.Object.Destroy(s_tex);
            s_tex = null;
            s_texUrl = null;
        }
    }
}
