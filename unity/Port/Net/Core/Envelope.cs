// Text frames of the data channel envelope (contract 2.1).
//
// Outgoing frames are written byte-exact without whitespace. A game message is wrapped as
// {"t":"msg","id":-7503,"json":<game JSON bytes>} without re-serialising it, and unwrapping
// returns the span of the raw "json" object text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PortNet
{
    public struct EnvelopeFrame
    {
        public string T;          // frame type ("hello", "msg", ...); null when missing
        public bool HasId;
        public int Id;            // msg: transfer id
        public bool HasJson;
        public JsonSpan Json;     // msg: raw value span of "json"
        public string Token;      // hello
        public string Uid;        // hello
        public int Reason;        // refused
        public string ByeReason;  // bye
    }

    public static class Envelope
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);
        static readonly byte[] MsgPrefix = Encoding.ASCII.GetBytes("{\"t\":\"msg\",\"id\":-7503,\"json\":");

        public const string Replaced = "replaced";
        public const string Closed = "closed";
        public const string Shutdown = "shutdown";

        public static byte[] Joined()
        {
            return Encoding.ASCII.GetBytes("{\"t\":\"joined\"}");
        }

        public static byte[] Pending()
        {
            return Encoding.ASCII.GetBytes("{\"t\":\"pending\"}");
        }

        public static byte[] Ping()
        {
            return Encoding.ASCII.GetBytes("{\"t\":\"ping\"}");
        }

        public static byte[] Refused(int reason)
        {
            return Encoding.ASCII.GetBytes("{\"t\":\"refused\",\"reason\":" + reason.ToString(CultureInfo.InvariantCulture) + "}");
        }

        public static byte[] Bye(string reason)
        {
            return Utf8.GetBytes("{\"t\":\"bye\",\"reason\":" + NetJson.Quote(reason) + "}");
        }

        public static byte[] Hello(string token, string uid)
        {
            var sb = new StringBuilder("{\"t\":\"hello\",\"token\":");
            NetJson.AppendString(sb, token).Append(",\"uid\":");
            NetJson.AppendString(sb, uid).Append('}');
            return Utf8.GetBytes(sb.ToString());
        }

        // Size of the wrapped frame for a game JSON payload of jsonLen bytes.
        public static int WrappedLength(int jsonLen)
        {
            return MsgPrefix.Length + jsonLen + 1;
        }

        public static byte[] WrapMsg(byte[] json, int off, int len)
        {
            if (json == null || off < 0 || len < 0 || off + len > json.Length)
                throw new ArgumentException("bad json span");
            var frame = new byte[WrappedLength(len)];
            Buffer.BlockCopy(MsgPrefix, 0, frame, 0, MsgPrefix.Length);
            Buffer.BlockCopy(json, off, frame, MsgPrefix.Length, len);
            frame[frame.Length - 1] = (byte)'}';
            return frame;
        }

        // Parses a received text frame. False when it is not a JSON object.
        public static bool TryParse(byte[] data, int len, out EnvelopeFrame frame)
        {
            frame = default;
            var members = new List<KeyValuePair<string, JsonSpan>>();
            if (!NetJson.TryParseObject(data, 0, len, members))
                return false;
            bool seenT = false, seenId = false, seenJson = false, seenToken = false, seenUid = false, seenReason = false;
            foreach (var m in members)
            {
                switch (m.Key)
                {
                    case "t":
                        if (!seenT)
                        {
                            seenT = true;
                            NetJson.TryGetString(data, m.Value, out frame.T);
                        }
                        break;
                    case "id":
                        if (!seenId)
                        {
                            seenId = true;
                            frame.HasId = NetJson.TryGetInt(data, m.Value, out frame.Id);
                        }
                        break;
                    case "json":
                        if (!seenJson)
                        {
                            seenJson = true;
                            frame.HasJson = true;
                            frame.Json = m.Value;
                        }
                        break;
                    case "token":
                        if (!seenToken)
                        {
                            seenToken = true;
                            NetJson.TryGetString(data, m.Value, out frame.Token);
                        }
                        break;
                    case "uid":
                        if (!seenUid)
                        {
                            seenUid = true;
                            NetJson.TryGetString(data, m.Value, out frame.Uid);
                        }
                        break;
                    case "reason":
                        if (!seenReason)
                        {
                            seenReason = true;
                            if (!NetJson.TryGetInt(data, m.Value, out frame.Reason))
                                NetJson.TryGetString(data, m.Value, out frame.ByeReason);
                        }
                        break;
                }
            }
            return true;
        }

        // A msg frame with id -7503 and an object "json" value: returns that object's raw span.
        public static bool TryUnwrapMsg(byte[] data, int len, out JsonSpan json)
        {
            json = default;
            if (!TryParse(data, len, out EnvelopeFrame f))
                return false;
            if (f.T != "msg" || !f.HasId || f.Id != NetLimits.JsonTransferId || !f.HasJson || !NetJson.IsObject(data, f.Json))
                return false;
            json = f.Json;
            return true;
        }

        // uid rule of the hello frame: 1..128 UTF-8 bytes, no control characters.
        public static bool IsValidUid(string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return false;
            foreach (char c in uid)
                if (c < 0x20 || c == 0x7f)
                    return false;
            int n;
            try
            {
                n = new UTF8Encoding(false, true).GetByteCount(uid);
            }
            catch (ArgumentException)
            {
                return false;
            }
            return n >= 1 && n <= NetLimits.MaxUidBytes;
        }
    }
}
