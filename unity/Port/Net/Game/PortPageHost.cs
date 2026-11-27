// Tunnel mode (page_host "tunnel" in iwyk-net.json): the game serves the phone page that sits
// next to it (PhonePage/) and publishes it through the bundled cloudflared as a quick tunnel.
// The address is applied to the engine once it works from outside; it is never saved, because
// every launch gets a new one. Polled from WebRtcDataComms.Update (scene loads destroy objects).

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PortNet
{
    public static class PortPageHost
    {
        public const string PageFolder = "PhonePage";

        static bool s_started;
        static bool s_failedToStart;
        static float s_nextPoll;
        static string s_url;
        static readonly byte[] s_urlBuf = new byte[512];
        static readonly byte[] s_detailBuf = new byte[512];

        public static string CloudflaredPath(string gameDir)
        {
            bool win = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            return Path.Combine(gameDir ?? ".", win ? "cloudflared.exe" : "cloudflared");
        }

        // Starts the page host once when the config asks for a tunnel; never throws.
        public static void Ensure(NetEngine engine, NetConfig config)
        {
            if (s_started || s_failedToStart || engine == null || config == null || config.PageHost != NetConfig.PageHostTunnel)
                return;
            string dir = Path.Combine(PortNetHost.GameDir ?? ".", PageFolder);
            string cf = CloudflaredPath(PortNetHost.GameDir);
            try
            {
                if (!Directory.Exists(dir) || !File.Exists(cf))
                {
                    s_failedToStart = true;
                    engine.PageStatus = "the game files are incomplete (" + PageFolder + " or cloudflared missing)";
                    PortNetHost.Log.Error("tunnel mode: " + dir + " or " + cf + " not found");
                    return;
                }
                int rc = IwykNative.iwyk_pagehost_start(IwykNative.Utf8z(dir), IwykNative.Utf8z(cf));
                if (rc < 0 && rc != IwykNative.IWYK_E_ALREADY_STARTED)
                {
                    s_failedToStart = true;
                    engine.PageStatus = "cannot start (" + rc + ")";
                    PortNetHost.Log.Error("page host start failed: " + rc);
                    return;
                }
                s_started = true;
                Application.quitting += Stop;
                PortNetHost.Log.Info("page host started; waiting for the tunnel");
            }
            catch (Exception e)
            {
                s_failedToStart = true;
                engine.PageStatus = "cannot start";
                PortNetHost.Log.Error("page host start failed: " + e.Message);
            }
        }

        // Once a second: apply a new address, or show why there is none.
        public static void Poll(NetEngine engine)
        {
            if (!s_started || engine == null || Time.realtimeSinceStartup < s_nextPoll)
                return;
            s_nextPoll = Time.realtimeSinceStartup + 1f;
            try
            {
                int st = IwykNative.iwyk_pagehost_state(s_urlBuf, s_urlBuf.Length, s_detailBuf, s_detailBuf.Length);
                if (st == 1)
                {
                    string url = Str(s_urlBuf);
                    if (url != s_url)
                    {
                        s_url = url;
                        engine.PageStatus = null;
                        engine.SetPageUrl(url);
                        PortNetHost.Log.Info("phone page is online through the tunnel");
                    }
                }
                else if (st == 0 || st == 2)
                {
                    if (s_url != null)
                    {
                        s_url = null;
                        engine.SetPageUrl(null);
                    }
                    string detail = Str(s_detailBuf);
                    engine.PageStatus = st == 2 ? "no tunnel yet (" + (string.IsNullOrEmpty(detail) ? "retrying" : detail) + "); is the computer online?"
                                                : (string.IsNullOrEmpty(detail) ? null : detail);
                }
            }
            catch (Exception e)
            {
                s_started = false;
                engine.PageStatus = "page host error";
                PortNetHost.Log.Error("page host poll failed: " + e.Message);
            }
        }

        static string Str(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            if (n < 0)
                n = b.Length;
            return Encoding.UTF8.GetString(b, 0, n);
        }

        static void Stop()
        {
            try
            {
                IwykNative.iwyk_pagehost_stop();
            }
            catch (Exception)
            {
            }
            s_started = false;
        }
    }
}
