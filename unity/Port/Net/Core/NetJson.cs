// Minimal JSON reader/writer for the wire envelope and the config file.
//
// The reader works on UTF-8 bytes and never re-serialises: it validates one value and reports
// the byte span of each top-level member value, so the raw text of a nested object can be
// forwarded unchanged. Strings may contain escapes and raw UTF-8; nesting depth is capped.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PortNet
{
    public struct JsonSpan
    {
        public int Start;
        public int Length;

        public JsonSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }
    }

    public static class NetJson
    {
        public const int MaxDepth = 64;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        // Parses buf[off..off+len) as exactly one JSON object (surrounding whitespace allowed) and
        // lists its members in order with decoded keys. False for anything else.
        public static bool TryParseObject(byte[] buf, int off, int len, List<KeyValuePair<string, JsonSpan>> members)
        {
            members?.Clear();
            if (buf == null || off < 0 || len < 0 || off + len > buf.Length)
                return false;
            int end = off + len;
            int p = SkipWs(buf, off, end);
            if (p >= end || buf[p] != (byte)'{')
                return false;
            int q = ParseObject(buf, p, end, 0, members);
            if (q < 0)
                return false;
            q = SkipWs(buf, q, end);
            if (q != end)
            {
                members?.Clear();
                return false;
            }
            return true;
        }

        public static bool TryParseObject(string text, out byte[] utf8, List<KeyValuePair<string, JsonSpan>> members)
        {
            utf8 = text == null ? null : Utf8.GetBytes(text);
            return utf8 != null && TryParseObject(utf8, 0, utf8.Length, members);
        }

        // First member named key of the object in buf[off..off+len).
        public static bool TryFind(byte[] buf, int off, int len, string key, out JsonSpan value)
        {
            value = default;
            var members = new List<KeyValuePair<string, JsonSpan>>();
            if (!TryParseObject(buf, off, len, members))
                return false;
            foreach (var m in members)
            {
                if (m.Key == key)
                {
                    value = m.Value;
                    return true;
                }
            }
            return false;
        }

        public static bool IsObject(byte[] buf, JsonSpan v)
        {
            return v.Length >= 2 && buf[v.Start] == (byte)'{';
        }

        public static bool IsNull(byte[] buf, JsonSpan v)
        {
            return v.Length == 4 && buf[v.Start] == (byte)'n';
        }

        public static bool TryGetBool(byte[] buf, JsonSpan v, out bool value)
        {
            value = false;
            if (v.Length == 4 && buf[v.Start] == (byte)'t')
            {
                value = true;
                return true;
            }
            return v.Length == 5 && buf[v.Start] == (byte)'f';
        }

        public static bool TryGetNumber(byte[] buf, JsonSpan v, out double value)
        {
            value = 0;
            if (v.Length == 0)
                return false;
            byte c = buf[v.Start];
            if (c != (byte)'-' && (c < (byte)'0' || c > (byte)'9'))
                return false;
            string s = Encoding.ASCII.GetString(buf, v.Start, v.Length);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // Integral numbers only (no fraction, no exponent), within int range.
        public static bool TryGetInt(byte[] buf, JsonSpan v, out int value)
        {
            value = 0;
            if (v.Length == 0 || v.Length > 11)
                return false;
            for (int i = 0; i < v.Length; i++)
            {
                byte c = buf[v.Start + i];
                if (!(c >= (byte)'0' && c <= (byte)'9') && !(i == 0 && c == (byte)'-'))
                    return false;
            }
            string s = Encoding.ASCII.GetString(buf, v.Start, v.Length);
            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        // Decodes a string value (escapes, surrogate pairs, raw UTF-8).
        public static bool TryGetString(byte[] buf, JsonSpan v, out string value)
        {
            value = null;
            if (v.Length < 2 || buf[v.Start] != (byte)'"' || buf[v.Start + v.Length - 1] != (byte)'"')
                return false;
            int p = v.Start + 1, end = v.Start + v.Length - 1;
            var sb = new StringBuilder(v.Length);
            int runStart = p;
            while (p < end)
            {
                byte c = buf[p];
                if (c != (byte)'\\')
                {
                    p++;
                    continue;
                }
                if (p > runStart)
                    sb.Append(Utf8.GetString(buf, runStart, p - runStart));
                if (p + 1 >= end)
                    return false;
                byte e = buf[p + 1];
                p += 2;
                switch (e)
                {
                    case (byte)'"': sb.Append('"'); break;
                    case (byte)'\\': sb.Append('\\'); break;
                    case (byte)'/': sb.Append('/'); break;
                    case (byte)'b': sb.Append('\b'); break;
                    case (byte)'f': sb.Append('\f'); break;
                    case (byte)'n': sb.Append('\n'); break;
                    case (byte)'r': sb.Append('\r'); break;
                    case (byte)'t': sb.Append('\t'); break;
                    case (byte)'u':
                        if (p + 4 > end || !Hex4(buf, p, out int cp))
                            return false;
                        sb.Append((char)cp);
                        p += 4;
                        break;
                    default:
                        return false;
                }
                runStart = p;
            }
            if (end > runStart)
                sb.Append(Utf8.GetString(buf, runStart, end - runStart));
            value = sb.ToString();
            return true;
        }

        public static string Raw(byte[] buf, JsonSpan v)
        {
            return Utf8.GetString(buf, v.Start, v.Length);
        }

        // Appends s as a JSON string literal: quotes, backslash and control characters escaped,
        // everything else (including non-ASCII) kept as is.
        public static StringBuilder AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"');
        }

        public static string Quote(string s)
        {
            return AppendString(new StringBuilder(), s).ToString();
        }

        // ---- validating scanner: each returns the position after the value, or -1 ----

        static int SkipWs(byte[] b, int p, int end)
        {
            while (p < end && (b[p] == (byte)' ' || b[p] == (byte)'\t' || b[p] == (byte)'\n' || b[p] == (byte)'\r'))
                p++;
            return p;
        }

        static int ParseValue(byte[] b, int p, int end, int depth)
        {
            if (p >= end)
                return -1;
            switch (b[p])
            {
                case (byte)'{': return ParseObject(b, p, end, depth, null);
                case (byte)'[': return ParseArray(b, p, end, depth);
                case (byte)'"': return ParseString(b, p, end);
                case (byte)'t': return Literal(b, p, end, "true");
                case (byte)'f': return Literal(b, p, end, "false");
                case (byte)'n': return Literal(b, p, end, "null");
                default: return ParseNumber(b, p, end);
            }
        }

        static int ParseObject(byte[] b, int p, int end, int depth, List<KeyValuePair<string, JsonSpan>> members)
        {
            if (depth >= MaxDepth)
                return -1;
            p = SkipWs(b, p + 1, end);
            if (p < end && b[p] == (byte)'}')
                return p + 1;
            while (true)
            {
                if (p >= end || b[p] != (byte)'"')
                    return -1;
                int keyEnd = ParseString(b, p, end);
                if (keyEnd < 0)
                    return -1;
                string key = null;
                if (members != null && !TryGetString(b, new JsonSpan(p, keyEnd - p), out key))
                    return -1;
                p = SkipWs(b, keyEnd, end);
                if (p >= end || b[p] != (byte)':')
                    return -1;
                p = SkipWs(b, p + 1, end);
                int vEnd = ParseValue(b, p, end, depth + 1);
                if (vEnd < 0)
                    return -1;
                members?.Add(new KeyValuePair<string, JsonSpan>(key, new JsonSpan(p, vEnd - p)));
                p = SkipWs(b, vEnd, end);
                if (p >= end)
                    return -1;
                if (b[p] == (byte)'}')
                    return p + 1;
                if (b[p] != (byte)',')
                    return -1;
                p = SkipWs(b, p + 1, end);
            }
        }

        static int ParseArray(byte[] b, int p, int end, int depth)
        {
            if (depth >= MaxDepth)
                return -1;
            p = SkipWs(b, p + 1, end);
            if (p < end && b[p] == (byte)']')
                return p + 1;
            while (true)
            {
                int vEnd = ParseValue(b, p, end, depth + 1);
                if (vEnd < 0)
                    return -1;
                p = SkipWs(b, vEnd, end);
                if (p >= end)
                    return -1;
                if (b[p] == (byte)']')
                    return p + 1;
                if (b[p] != (byte)',')
                    return -1;
                p = SkipWs(b, p + 1, end);
            }
        }

        static int ParseString(byte[] b, int p, int end)
        {
            p++;
            while (p < end)
            {
                byte c = b[p];
                if (c == (byte)'"')
                    return p + 1;
                if (c < 0x20)
                    return -1;
                if (c == (byte)'\\')
                {
                    if (p + 1 >= end)
                        return -1;
                    byte e = b[p + 1];
                    if (e == (byte)'u')
                    {
                        if (p + 6 > end || !Hex4(b, p + 2, out _))
                            return -1;
                        p += 6;
                        continue;
                    }
                    if (e != (byte)'"' && e != (byte)'\\' && e != (byte)'/' && e != (byte)'b' && e != (byte)'f'
                        && e != (byte)'n' && e != (byte)'r' && e != (byte)'t')
                        return -1;
                    p += 2;
                    continue;
                }
                p++;
            }
            return -1;
        }

        static int ParseNumber(byte[] b, int p, int end)
        {
            int start = p;
            if (p < end && b[p] == (byte)'-')
                p++;
            if (p >= end)
                return -1;
            if (b[p] == (byte)'0')
                p++;
            else if (b[p] >= (byte)'1' && b[p] <= (byte)'9')
                while (p < end && IsDigit(b[p]))
                    p++;
            else
                return -1;
            if (p < end && b[p] == (byte)'.')
            {
                p++;
                if (p >= end || !IsDigit(b[p]))
                    return -1;
                while (p < end && IsDigit(b[p]))
                    p++;
            }
            if (p < end && (b[p] == (byte)'e' || b[p] == (byte)'E'))
            {
                p++;
                if (p < end && (b[p] == (byte)'+' || b[p] == (byte)'-'))
                    p++;
                if (p >= end || !IsDigit(b[p]))
                    return -1;
                while (p < end && IsDigit(b[p]))
                    p++;
            }
            return p > start ? p : -1;
        }

        static int Literal(byte[] b, int p, int end, string word)
        {
            if (p + word.Length > end)
                return -1;
            for (int i = 0; i < word.Length; i++)
                if (b[p + i] != (byte)word[i])
                    return -1;
            return p + word.Length;
        }

        static bool IsDigit(byte c)
        {
            return c >= (byte)'0' && c <= (byte)'9';
        }

        static bool Hex4(byte[] b, int p, out int value)
        {
            value = 0;
            for (int i = 0; i < 4; i++)
            {
                int c = b[p + i], d;
                if (c >= '0' && c <= '9')
                    d = c - '0';
                else if (c >= 'a' && c <= 'f')
                    d = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F')
                    d = c - 'A' + 10;
                else
                    return false;
                value = value * 16 + d;
            }
            return true;
        }
    }
}
