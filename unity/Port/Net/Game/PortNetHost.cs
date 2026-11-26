// Unity glue for the PortNet engine: config files, certificate path, log sink and shutdown.
//
// Config (contract 4.7): <game dir>/iwyk-net.json, then <persistentDataPath>/iwyk-net.json,
// then IWYK_* environment variables. Certificate: <persistentDataPath>/iwyknet/dtls.pem (same
// file = same fingerprint across runs). The engine stops itself on Application.quitting,
// because UDPCommsProvider.OnDestroy never reaches it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PortNet
{
    public static class PortNetHost
    {
        public static readonly INetLog Log = new UnityLog();

        static bool s_quitHooked;

        // The engine the join screen shows (the last one started).
        public static NetEngine Current { get; private set; }

        public static string GameDir => NetConfig.GameDirFromDataPath(Application.dataPath);
        public static string GameConfigPath => Path.Combine(GameDir ?? ".", NetConfig.FileName);
        public static string UserConfigPath => Path.Combine(Application.persistentDataPath, NetConfig.FileName);
        public static string CertPath => Path.Combine(Path.Combine(Application.persistentDataPath, "iwyknet"), "dtls.pem");

        public static NetConfig LoadConfig()
        {
            var warnings = new List<string>();
            NetConfig c = NetConfig.Load(ReadText(GameConfigPath), ReadText(UserConfigPath), Environment.GetEnvironmentVariable,
                                         warnings, GameConfigPath, UserConfigPath);
            foreach (string w in warnings)
                Log.Warn(w);
            return c;
        }

        // Never throws: on failure the engine stays offline and its Problem says why.
        public static bool StartEngine(NetEngine engine)
        {
            try
            {
                if (Current != null && Current != engine)
                    Current.Stop();
                Current = engine;
                if (!s_quitHooked)
                {
                    Application.quitting += OnQuitting;
                    s_quitHooked = true;
                }
                return engine.Start(LoadConfig(), CertPath);
            }
            catch (Exception e)
            {
                Log.Error("network start failed: " + e);
                return false;
            }
        }

        public static void StopEngine(NetEngine engine)
        {
            try
            {
                engine?.Stop();
            }
            catch (Exception e)
            {
                Log.Error("network stop failed: " + e);
            }
        }

        // Persists the page address in the user config file and applies it to the running engine.
        public static void SetPageUrl(string url)
        {
            string normalized = string.IsNullOrEmpty(url) ? null : NetConfig.NormalizeUrl(url);
            if (!string.IsNullOrEmpty(url) && normalized == null)
            {
                Log.Warn("phone page address must start with https:// or http://; not saved");
                return;
            }
            try
            {
                string path = UserConfigPath;
                string json = NetConfig.SetKey(ReadText(path), NetConfig.KeyPageUrl, normalized == null ? "null" : NetJson.Quote(normalized));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json, new UTF8Encoding(false));
                Log.Info("phone page address saved to " + path);
            }
            catch (Exception e)
            {
                Log.Error("cannot save the phone page address: " + e.Message);
            }
            Current?.SetPageUrl(normalized);
        }

        static string ReadText(string path)
        {
            try
            {
                return path != null && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (Exception e)
            {
                Log.Warn("cannot read " + path + ": " + e.Message);
                return null;
            }
        }

        static void OnQuitting()
        {
            StopEngine(Current);
        }

        sealed class UnityLog : INetLog
        {
            public void Info(string m)
            {
                Debug.Log("[PortNet] " + m);
            }

            public void Warn(string m)
            {
                Debug.LogWarning("[PortNet] " + m);
            }

            public void Error(string m)
            {
                Debug.LogError("[PortNet] " + m);
            }
        }
    }
}
