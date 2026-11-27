// Network configuration (contract 4.7): iwyk-net.json in the game dir, then the same file in the
// user's persistent data dir, then IWYK_* environment variables; later sources win per key.
// Invalid values are ignored with a warning. Warnings never contain the page URL itself.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PortNet
{
    public sealed class NetConfig
    {
        public const string FileName = "iwyk-net.json";
        public const string KeyPageUrl = "phone_page_url";
        public const string KeyUdpPort = "udp_port";
        public const string KeyLanIp = "lan_ip";
        public const string KeyLogLevel = "log_level";
        public const string KeyPageHost = "page_host";
        public const string PageHostUrl = "url";        // phone_page_url points at a hosted page
        public const string PageHostTunnel = "tunnel";  // the game serves the page through a quick tunnel

        public const string EnvPageUrl = "IWYK_PAGE_URL";
        public const string EnvUdpPort = "IWYK_UDP_PORT";
        public const string EnvLanIp = "IWYK_LAN_IP";
        public const string EnvLogLevel = "IWYK_LOG_LEVEL";
        public const string EnvPageHost = "IWYK_PAGE_HOST";

        static readonly string[] LevelNames = { "debug", "info", "warn", "error", "off" };

        public string PhonePageUrl;                     // normalised (no fragment) or null
        public int UdpPort = NetLimits.DefaultUdpPort;  // 0 = any free port
        public string LanIp = "";                       // "" = automatic
        public int LogLevel = NetCodes.LogInfo;
        public string PageHost = PageHostUrl;

        // Where the files live (informational: used in problem texts).
        public string GameConfigPath;
        public string UserConfigPath;

        public NetConfig Clone()
        {
            return (NetConfig)MemberwiseClone();
        }

        // defaults < game dir file < user file < environment
        public static NetConfig Load(string gameDirJson, string userJson, Func<string, string> getEnv, ICollection<string> warnings,
                                     string gameConfigPath = null, string userConfigPath = null)
        {
            var c = new NetConfig { GameConfigPath = gameConfigPath, UserConfigPath = userConfigPath };
            if (gameDirJson != null)
                c.ApplyJson(gameDirJson, gameConfigPath ?? "game dir " + FileName, warnings);
            if (userJson != null)
                c.ApplyJson(userJson, userConfigPath ?? "user " + FileName, warnings);
            if (getEnv != null)
                c.ApplyEnv(getEnv, warnings);
            return c;
        }

        public void ApplyJson(string text, string source, ICollection<string> warnings)
        {
            var members = new List<KeyValuePair<string, JsonSpan>>();
            if (!NetJson.TryParseObject(text, out byte[] b, members))
            {
                warnings?.Add(source + ": not a JSON object, ignored");
                return;
            }
            foreach (var m in members)
            {
                JsonSpan v = m.Value;
                switch (m.Key)
                {
                    case KeyPageUrl:
                        if (NetJson.IsNull(b, v))
                            PhonePageUrl = null;
                        else if (NetJson.TryGetString(b, v, out string url))
                            SetUrl(url, source + " " + KeyPageUrl, warnings);
                        else
                            warnings?.Add(source + ": " + KeyPageUrl + " is not a string, ignored");
                        break;
                    case KeyUdpPort:
                        if (NetJson.TryGetInt(b, v, out int port))
                            SetPort(port, source + " " + KeyUdpPort, warnings);
                        else
                            warnings?.Add(source + ": " + KeyUdpPort + " is not an integer, ignored");
                        break;
                    case KeyLanIp:
                        if (NetJson.TryGetString(b, v, out string ip))
                            SetLanIp(ip, source + " " + KeyLanIp, warnings);
                        else
                            warnings?.Add(source + ": " + KeyLanIp + " is not a string, ignored");
                        break;
                    case KeyPageHost:
                        if (NetJson.TryGetString(b, v, out string host))
                            SetPageHost(host, source + " " + KeyPageHost, warnings);
                        else
                            warnings?.Add(source + ": " + KeyPageHost + " is not a string, ignored");
                        break;
                    case KeyLogLevel:
                        if (NetJson.TryGetString(b, v, out string lvl))
                            SetLevel(lvl, source + " " + KeyLogLevel, warnings);
                        else if (NetJson.TryGetInt(b, v, out int n) && n >= 0 && n < LevelNames.Length)
                            LogLevel = n;
                        else
                            warnings?.Add(source + ": " + KeyLogLevel + " is invalid, ignored");
                        break;
                }
            }
        }

        public void ApplyEnv(Func<string, string> getEnv, ICollection<string> warnings)
        {
            string s = getEnv(EnvPageUrl);
            if (!string.IsNullOrEmpty(s))
                SetUrl(s, EnvPageUrl, warnings);
            s = getEnv(EnvUdpPort);
            if (!string.IsNullOrEmpty(s))
            {
                if (int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port))
                    SetPort(port, EnvUdpPort, warnings);
                else
                    warnings?.Add(EnvUdpPort + " is not a port number, ignored");
            }
            s = getEnv(EnvLanIp);
            if (!string.IsNullOrEmpty(s))
                SetLanIp(s.Trim(), EnvLanIp, warnings);
            s = getEnv(EnvLogLevel);
            if (!string.IsNullOrEmpty(s))
                SetLevel(s, EnvLogLevel, warnings);
            s = getEnv(EnvPageHost);
            if (!string.IsNullOrEmpty(s))
                SetPageHost(s.Trim(), EnvPageHost, warnings);
        }

        void SetPageHost(string host, string what, ICollection<string> warnings)
        {
            if (host == PageHostUrl || host == PageHostTunnel)
                PageHost = host;
            else
                warnings?.Add(what + ": must be \"url\" or \"tunnel\", ignored");
        }

        void SetUrl(string url, string what, ICollection<string> warnings)
        {
            if (url.Length == 0)
            {
                PhonePageUrl = null;
                return;
            }
            string n = NormalizeUrl(url);
            if (n == null)
                warnings?.Add(what + ": must start with https:// or http://, ignored");
            else
                PhonePageUrl = n;
        }

        void SetPort(int port, string what, ICollection<string> warnings)
        {
            if (port < 0 || port > 65535)
                warnings?.Add(what + ": " + port.ToString(CultureInfo.InvariantCulture) + " is not in 0..65535, ignored");
            else
                UdpPort = port;
        }

        void SetLanIp(string ip, string what, ICollection<string> warnings)
        {
            if (ip.Length == 0 || IsIPv4(ip))
                LanIp = ip;
            else
                warnings?.Add(what + ": not an IPv4 address, ignored");
        }

        void SetLevel(string name, string what, ICollection<string> warnings)
        {
            if (TryParseLogLevel(name, out int lvl))
                LogLevel = lvl;
            else
                warnings?.Add(what + ": use debug, info, warn, error or off; ignored");
        }

        // http(s) URL with a host part, fragment removed; null when invalid.
        public static string NormalizeUrl(string url)
        {
            if (url == null)
                return null;
            url = url.Trim();
            int hash = url.IndexOf('#');
            if (hash >= 0)
                url = url.Substring(0, hash);
            string rest;
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                rest = url.Substring(8);
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                rest = url.Substring(7);
            else
                return null;
            if (rest.Length == 0 || rest[0] == '/')
                return null;
            foreach (char c in url)
                if (c <= ' ' || c == 0x7f)
                    return null;
            return url;
        }

        public static bool IsIPv4(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            string[] parts = s.Split('.');
            if (parts.Length != 4)
                return false;
            foreach (string p in parts)
            {
                if (p.Length == 0 || p.Length > 3 || (p.Length > 1 && p[0] == '0'))
                    return false;
                int v = 0;
                foreach (char c in p)
                {
                    if (c < '0' || c > '9')
                        return false;
                    v = v * 10 + (c - '0');
                }
                if (v > 255)
                    return false;
            }
            return true;
        }

        public static bool TryParseLogLevel(string name, out int level)
        {
            level = NetCodes.LogInfo;
            if (name == null)
                return false;
            string n = name.Trim().ToLowerInvariant();
            if (n == "warning")
                n = "warn";
            for (int i = 0; i < LevelNames.Length; i++)
            {
                if (LevelNames[i] == n)
                {
                    level = i;
                    return true;
                }
            }
            return false;
        }

        public static string LogLevelName(int level)
        {
            return level >= 0 && level < LevelNames.Length ? LevelNames[level] : "info";
        }

        // Game dir from Unity's Application.dataPath: its parent, except in a macOS player
        // (dataPath ".../Name.app/Contents"), where it is the folder that contains the .app.
        public static string GameDirFromDataPath(string dataPath)
        {
            if (string.IsNullOrEmpty(dataPath))
                return null;
            string p = dataPath.Replace('\\', '/').TrimEnd('/');
            if (p.EndsWith(".app/Contents", StringComparison.OrdinalIgnoreCase))
                p = Parent(p);
            return Parent(p);
        }

        static string Parent(string p)
        {
            int i = p.LastIndexOf('/');
            if (i < 0)
                return "";
            return i == 0 ? "/" : p.Substring(0, i);
        }

        // Returns existing (a flat JSON object, or null/invalid for a new one) with key set to the
        // raw JSON value rawValue, other members kept verbatim and in order. One member per line.
        public static string SetKey(string existing, string key, string rawValue)
        {
            var members = new List<KeyValuePair<string, JsonSpan>>();
            var outMembers = new List<KeyValuePair<string, string>>();
            if (existing != null && NetJson.TryParseObject(existing, out byte[] b, members))
            {
                foreach (var m in members)
                    if (m.Key != key)
                        outMembers.Add(new KeyValuePair<string, string>(m.Key, NetJson.Raw(b, m.Value)));
            }
            outMembers.Add(new KeyValuePair<string, string>(key, rawValue));
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < outMembers.Count; i++)
            {
                sb.Append("  ");
                NetJson.AppendString(sb, outMembers[i].Key).Append(": ").Append(outMembers[i].Value);
                sb.Append(i + 1 < outMembers.Count ? ",\n" : "\n");
            }
            return sb.Append("}\n").ToString();
        }
    }
}
