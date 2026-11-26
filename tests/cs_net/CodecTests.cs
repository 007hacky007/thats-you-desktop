// NetJson, Envelope, GameBatch and JoinProbe.

using System.Collections.Generic;
using System.Text;
using PortAudio.Tests;

namespace PortNet.Tests
{
    public static class NetJsonTests
    {
        static byte[] B(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        [PaTest]
        public static void RawValueSpansOfNestedValues()
        {
            byte[] b = B(" {\"a\" : {\"x\":[1,{\"y\":\"}\"}],\"z\":null} , \"b\":-1.5e3,\"c\":true,\"d\":\"q\\\"\"} ");
            var m = new List<KeyValuePair<string, JsonSpan>>();
            PaAssert.True(NetJson.TryParseObject(b, 0, b.Length, m));
            PaAssert.SequenceEqual(new[] { "a", "b", "c", "d" }, m.ConvertAll(kv => kv.Key));
            PaAssert.Equal("{\"x\":[1,{\"y\":\"}\"}],\"z\":null}", NetJson.Raw(b, m[0].Value));
            PaAssert.Equal("-1.5e3", NetJson.Raw(b, m[1].Value));
            PaAssert.True(NetJson.TryGetNumber(b, m[1].Value, out double d));
            PaAssert.Near(-1500, d);
            PaAssert.True(NetJson.TryGetBool(b, m[2].Value, out bool t) && t);
            PaAssert.True(NetJson.TryGetString(b, m[3].Value, out string s));
            PaAssert.Equal("q\"", s);
            PaAssert.True(NetJson.IsObject(b, m[0].Value));
            PaAssert.False(NetJson.IsObject(b, m[1].Value));
        }

        [PaTest]
        public static void StringEscapesAndUtf8()
        {
            byte[] b = B("{\"k\":\"a\\n\\t\\\\\\/\\u00e9\\ud83d\\ude00\u00fc\"}");
            PaAssert.True(NetJson.TryFind(b, 0, b.Length, "k", out JsonSpan v));
            PaAssert.True(NetJson.TryGetString(b, v, out string s));
            PaAssert.Equal("a\n\t\\/\u00e9\ud83d\ude00\u00fc", s);
        }

        [PaTest]
        public static void RejectsMalformed()
        {
            string[] bad =
            {
                "", "[]", "\"x\"", "{", "{\"a\"}", "{\"a\":}", "{\"a\":1,}", "{\"a\":01}", "{\"a\":1.}", "{\"a\":tru}",
                "{\"a\":\"x}", "{\"a\":\"\\x\"}", "{\"a\":\"\\u12\"}", "{\"a\":1} x", "{\"a\":\"\u0001\"}", "{a:1}",
                "{\"a\":[1,2}", "{\"a\":1 \"b\":2}"
            };
            foreach (string s in bad)
            {
                byte[] b = B(s);
                PaAssert.False(NetJson.TryParseObject(b, 0, b.Length, null), s);
            }
        }

        [PaTest]
        public static void DepthIsCapped()
        {
            var sb = new StringBuilder("{\"a\":");
            for (int i = 0; i < 100; i++)
                sb.Append('[');
            for (int i = 0; i < 100; i++)
                sb.Append(']');
            sb.Append('}');
            byte[] b = B(sb.ToString());
            PaAssert.False(NetJson.TryParseObject(b, 0, b.Length, null));
        }

        [PaTest]
        public static void IntegersOnlyForTryGetInt()
        {
            byte[] b = B("{\"a\":-7503,\"b\":1.0,\"c\":99999999999,\"d\":\"1\"}");
            var m = new List<KeyValuePair<string, JsonSpan>>();
            PaAssert.True(NetJson.TryParseObject(b, 0, b.Length, m));
            PaAssert.True(NetJson.TryGetInt(b, m[0].Value, out int a));
            PaAssert.Equal(-7503, a);
            PaAssert.False(NetJson.TryGetInt(b, m[1].Value, out _));
            PaAssert.False(NetJson.TryGetInt(b, m[2].Value, out _));
            PaAssert.False(NetJson.TryGetInt(b, m[3].Value, out _));
        }

        [PaTest]
        public static void QuoteEscapes()
        {
            PaAssert.Equal("\"a\\\"b\\\\c\\n\\u0001\u00e9\"", NetJson.Quote("a\"b\\c\n\u0001\u00e9"));
        }
    }

    public static class EnvelopeTests
    {
        static string S(byte[] b)
        {
            return Encoding.UTF8.GetString(b);
        }

        [PaTest]
        public static void ExactFrames()
        {
            PaAssert.Equal("{\"t\":\"joined\"}", S(Envelope.Joined()));
            PaAssert.Equal("{\"t\":\"pending\"}", S(Envelope.Pending()));
            PaAssert.Equal("{\"t\":\"ping\"}", S(Envelope.Ping()));
            PaAssert.Equal("{\"t\":\"refused\",\"reason\":2}", S(Envelope.Refused(2)));
            PaAssert.Equal("{\"t\":\"bye\",\"reason\":\"replaced\"}", S(Envelope.Bye(Envelope.Replaced)));
            PaAssert.Equal("{\"t\":\"bye\",\"reason\":\"closed\"}", S(Envelope.Bye(Envelope.Closed)));
            PaAssert.Equal("{\"t\":\"hello\",\"token\":\"T0k\",\"uid\":\"u-1\"}", S(Envelope.Hello("T0k", "u-1")));
        }

        [PaTest]
        public static void WrapKeepsGameBytes()
        {
            // odd but valid spacing and escapes must survive byte for byte
            byte[] game = Encoding.UTF8.GetBytes("xx{\"TypeString\":\"SessionStateMessage\", \"S\":\"\\u00e9\u00e9\",\"N\":1.50}yy");
            byte[] frame = Envelope.WrapMsg(game, 2, game.Length - 4);
            PaAssert.Equal("{\"t\":\"msg\",\"id\":-7503,\"json\":{\"TypeString\":\"SessionStateMessage\", \"S\":\"\\u00e9\u00e9\",\"N\":1.50}}", S(frame));
            PaAssert.Equal(frame.Length, Envelope.WrappedLength(game.Length - 4));
        }

        [PaTest]
        public static void UnwrapReturnsRawObjectText()
        {
            string inner = "{ \"TypeString\" : \"ClientRequestPlayerIDMessage\",\"UID\":\"a\\\"b\",\"L\":[1, 2] }";
            byte[] f = Encoding.UTF8.GetBytes("{\"t\":\"msg\",\"extra\":{\"q\":1},\"json\":" + inner + ",\"id\":-7503}");
            PaAssert.True(Envelope.TryUnwrapMsg(f, f.Length, out JsonSpan j));
            PaAssert.Equal(inner, NetJson.Raw(f, j));
        }

        [PaTest]
        public static void UnwrapRejects()
        {
            string[] bad =
            {
                "{\"t\":\"msg\",\"id\":5,\"json\":{}}",
                "{\"t\":\"msg\",\"json\":{}}",
                "{\"t\":\"msg\",\"id\":-7503,\"json\":\"{}\"}",
                "{\"t\":\"msg\",\"id\":-7503}",
                "{\"t\":\"ping\",\"id\":-7503,\"json\":{}}",
                "{\"t\":\"msg\",\"id\":-7503,\"json\":{}",
            };
            foreach (string s in bad)
            {
                byte[] b = Encoding.UTF8.GetBytes(s);
                PaAssert.False(Envelope.TryUnwrapMsg(b, b.Length, out _), s);
            }
        }

        [PaTest]
        public static void ParsesHelloRefusedBye()
        {
            byte[] h = Encoding.UTF8.GetBytes("{\"t\":\"hello\",\"token\":\"abc\",\"uid\":\"dev\\u00e9\",\"x\":1}");
            PaAssert.True(Envelope.TryParse(h, h.Length, out EnvelopeFrame f));
            PaAssert.Equal("hello", f.T);
            PaAssert.Equal("abc", f.Token);
            PaAssert.Equal("dev\u00e9", f.Uid);
            byte[] r = Envelope.Refused(3);
            PaAssert.True(Envelope.TryParse(r, r.Length, out f));
            PaAssert.Equal(3, f.Reason);
            byte[] y = Envelope.Bye("shutdown");
            PaAssert.True(Envelope.TryParse(y, y.Length, out f));
            PaAssert.Equal("shutdown", f.ByeReason);
        }

        [PaTest]
        public static void UidRule()
        {
            PaAssert.True(Envelope.IsValidUid("a"));
            PaAssert.True(Envelope.IsValidUid(new string('x', 128)));
            PaAssert.False(Envelope.IsValidUid(new string('x', 129)));
            PaAssert.False(Envelope.IsValidUid(new string('\u00e9', 65)));  // 130 bytes
            PaAssert.False(Envelope.IsValidUid(""));
            PaAssert.False(Envelope.IsValidUid(null));
            PaAssert.False(Envelope.IsValidUid("a\nb"));
        }
    }

    public static class GameBatchTests
    {
        public static byte[] Batch(params object[] segs)
        {
            var l = new List<byte>(GameBatch.StartMarker);
            for (int i = 0; i < segs.Length; i += 2)
            {
                int id = (int)segs[i];
                byte[] d = segs[i + 1] is string s ? Encoding.UTF8.GetBytes(s) : (byte[])segs[i + 1];
                var h = new byte[8];
                GameBatch.WriteInt32(h, 0, d.Length);
                GameBatch.WriteInt32(h, 4, id);
                l.AddRange(h);
                l.AddRange(d);
            }
            l.AddRange(GameBatch.EndMarker);
            return l.ToArray();
        }

        static string Seg(byte[] b, BatchSegment s)
        {
            return s.TransferId + ":" + Encoding.UTF8.GetString(b, s.Offset, s.Length);
        }

        [PaTest]
        public static void OneJson()
        {
            byte[] b = Batch(-7503, "{\"a\":1}");
            var segs = new List<BatchSegment>();
            PaAssert.True(GameBatch.IsBatch(b, b.Length));
            PaAssert.True(GameBatch.TrySplit(b, b.Length, segs));
            PaAssert.Equal(1, segs.Count);
            PaAssert.Equal("-7503:{\"a\":1}", Seg(b, segs[0]));
        }

        [PaTest]
        public static void SeveralJsonAndContent()
        {
            byte[] content = new byte[40000];
            content[0] = 0x32;  // a length whose low bytes look like the end marker must not confuse us
            content[1] = 0x90;
            byte[] b = Batch(-7503, "{\"a\":1}", -7503, "{\"b\":2}", 77, content);
            var segs = new List<BatchSegment>();
            PaAssert.True(GameBatch.TrySplit(b, b.Length, segs));
            PaAssert.Equal(3, segs.Count);
            PaAssert.Equal("-7503:{\"b\":2}", Seg(b, segs[1]));
            PaAssert.Equal(77, segs[2].TransferId);
            PaAssert.Equal(40000, segs[2].Length);
        }

        [PaTest]
        public static void LengthLookingLikeEndMarker()
        {
            byte[] b = Batch(-7503, new byte[0x9032]);  // segLen bytes 32 90 00 00
            var segs = new List<BatchSegment>();
            PaAssert.True(GameBatch.TrySplit(b, b.Length, segs));
            PaAssert.Equal(0x9032, segs[0].Length);
        }

        [PaTest]
        public static void MalformedAndTruncated()
        {
            var segs = new List<BatchSegment>();
            byte[] good = Batch(-7503, "{\"a\":1}", 5, "xyz");
            for (int cut = 0; cut < good.Length; cut++)
                PaAssert.False(GameBatch.TrySplit(good, cut, segs), "truncated at " + cut);
            PaAssert.Equal(0, segs.Count);

            byte[] empty = Batch();
            PaAssert.False(GameBatch.TrySplit(empty, empty.Length, segs), "no segments");

            byte[] neg = Batch(-7503, "{}");
            GameBatch.WriteInt32(neg, 4, -1);
            PaAssert.False(GameBatch.TrySplit(neg, neg.Length, segs), "negative length");

            byte[] huge = Batch(-7503, "{}");
            GameBatch.WriteInt32(huge, 4, int.MaxValue);
            PaAssert.False(GameBatch.TrySplit(huge, huge.Length, segs), "huge length");

            byte[] zero = Batch(-7503, "{}");
            GameBatch.WriteInt32(zero, 4, 0);
            PaAssert.False(GameBatch.TrySplit(zero, zero.Length, segs), "zero length");

            byte[] badEnd = Batch(-7503, "{}");
            badEnd[badEnd.Length - 1] = 0;
            PaAssert.False(GameBatch.TrySplit(badEnd, badEnd.Length, segs), "bad end marker");

            PaAssert.False(GameBatch.IsBatch(Encoding.UTF8.GetBytes("{\"a\":1}"), 7));
        }

        [PaTest]
        public static void OneTrailingByteNeverLoops()
        {
            var segs = new List<BatchSegment>();
            byte[] b = Batch(-7503, "{}");
            var plus = new byte[b.Length + 1];
            System.Array.Copy(b, plus, b.Length);
            PaAssert.False(GameBatch.TrySplit(plus, plus.Length, segs), "trailing byte after the end marker");
            // the original parser loops forever when exactly one byte remains without an end marker
            byte[] one = { 0x84, 0x12, 0x40, 0xEE, 0x07 };
            PaAssert.False(GameBatch.TrySplit(one, one.Length, segs));
            byte[] noEnd = new byte[b.Length - 1];
            System.Array.Copy(b, noEnd, noEnd.Length);
            PaAssert.False(GameBatch.TrySplit(noEnd, noEnd.Length, segs));
        }
    }

    public static class JoinProbeTests
    {
        [PaTest]
        public static void RequestLayout()
        {
            byte[] d = JoinProbe.BuildRequest("u\u00e9");
            PaAssert.Equal(12 + 3, d.Length);
            PaAssert.SequenceEqual(new byte[] { 0xC1, 0x7C, 0xDC, 0x01, 0x43, 0xCF, 0x93, 0, 3, 0, 0, 0, (byte)'u', 0xC3, 0xA9 }, d);
        }

        [PaTest]
        public static void DecodesReplies()
        {
            byte[] ok = { 0x6A, 0xE4, 0x41, 0x2E, 0xB9, 0x41, 0xC1, 0 };
            PaAssert.True(JoinProbe.TryDecodeReply(ok, 8, out JoinDecision d));
            PaAssert.Equal(JoinOutcome.Joined, d.Outcome);
            byte[] no = { 0x98, 0xAB, 0x64, 0x59, 0x44, 0x4E, 0x2D, 3 };
            PaAssert.True(JoinProbe.TryDecodeReply(no, 8, out d));
            PaAssert.Equal(JoinOutcome.Refused, d.Outcome);
            PaAssert.Equal(3, d.RefuseReason);
            PaAssert.False(JoinProbe.TryDecodeReply(no, 7, out _));
            PaAssert.False(JoinProbe.TryDecodeReply(new byte[8], 8, out _));
        }
    }
}
