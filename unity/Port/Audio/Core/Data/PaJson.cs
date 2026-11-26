// Minimal JSON reader for the PortAudio data files (RFC 8259, recursive descent).
//
// Values map to: object -> Dictionary<string, object> (ordinal keys; a repeated key keeps the last
// value), array -> List<object>, string, integer -> long (exact; integers outside the long range
// become double), number with fraction or exponent -> double, true/false -> bool, null -> null.
// Malformed input throws PaDataException with the character offset.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PortAudio
{
    public static class PaJson
    {
        const int MaxDepth = 256;

        public static object Parse(string text)
        {
            if (text == null)
                throw new PaDataException("json: null input");
            var p = new Parser(text);
            p.SkipBom();
            p.SkipWhitespace();
            object value = p.ReadValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd)
                throw p.Error("unexpected data after the value");
            return value;
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            readonly StringBuilder sb = new StringBuilder();

            public Parser(string text)
            {
                s = text;
            }

            public bool AtEnd
            {
                get { return i >= s.Length; }
            }

            public PaDataException Error(string what)
            {
                return new PaDataException("json: " + what + " at offset " + i);
            }

            public void SkipBom()
            {
                if (i < s.Length && s[i] == '\uFEFF')
                    i++;
            }

            public void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                        i++;
                    else
                        break;
                }
            }

            public object ReadValue(int depth)
            {
                if (i >= s.Length)
                    throw Error("unexpected end of input");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                    throw Error("invalid literal");
                i += word.Length;
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                if (depth >= MaxDepth)
                    throw Error("nesting too deep");
                i++; // {
                var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != '"')
                        throw Error("expected a string key");
                    string key = ReadString();
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != ':')
                        throw Error("expected ':'");
                    i++;
                    SkipWhitespace();
                    obj[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (i >= s.Length)
                        throw Error("unterminated object");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == '}')
                    {
                        i++;
                        return obj;
                    }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> ReadArray(int depth)
            {
                if (depth >= MaxDepth)
                    throw Error("nesting too deep");
                i++; // [
                var list = new List<object>();
                SkipWhitespace();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (i >= s.Length)
                        throw Error("unterminated array");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == ']')
                    {
                        i++;
                        return list;
                    }
                    throw Error("expected ',' or ']'");
                }
            }

            string ReadString()
            {
                i++; // opening quote
                int start = i;
                // Fast path: no escapes.
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '"')
                    {
                        string plain = s.Substring(start, i - start);
                        i++;
                        return plain;
                    }
                    if (c == '\\')
                        break;
                    if (c < 0x20)
                        throw Error("control character in string");
                    i++;
                }
                sb.Length = 0;
                sb.Append(s, start, i - start);
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c < 0x20)
                    {
                        i--;
                        throw Error("control character in string");
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length)
                        break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length)
                                throw Error("truncated \\u escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int h = Hex(s[i + k]);
                                if (h < 0)
                                    throw Error("invalid \\u escape");
                                code = code * 16 + h;
                            }
                            i += 4;
                            sb.Append((char)code);   // surrogate pairs arrive as two escapes
                            break;
                        default:
                            i--;
                            throw Error("invalid escape '\\" + e + "'");
                    }
                }
                throw Error("unterminated string");
            }

            static int Hex(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            object ReadNumber()
            {
                int start = i;
                if (s[i] == '-')
                    i++;
                if (i >= s.Length)
                    throw Error("truncated number");
                if (s[i] == '0')
                {
                    i++;
                    if (i < s.Length && s[i] >= '0' && s[i] <= '9')
                        throw Error("leading zero in number");
                }
                else if (s[i] >= '1' && s[i] <= '9')
                {
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                        i++;
                }
                else
                {
                    throw Error("invalid number");
                }
                bool integer = true;
                if (i < s.Length && s[i] == '.')
                {
                    integer = false;
                    i++;
                    if (i >= s.Length || s[i] < '0' || s[i] > '9')
                        throw Error("digit expected after '.'");
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                        i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    integer = false;
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                        i++;
                    if (i >= s.Length || s[i] < '0' || s[i] > '9')
                        throw Error("digit expected in exponent");
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                        i++;
                }
                string token = s.Substring(start, i - start);
                if (integer)
                {
                    long l;
                    if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l))
                        return l;
                }
                double d;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Error("invalid number");
                return d;
            }
        }
    }
}
