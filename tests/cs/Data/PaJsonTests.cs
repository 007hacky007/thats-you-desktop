using System.Collections.Generic;

namespace PortAudio.Tests
{
    public static class PaJsonTests
    {
        static Dictionary<string, object> Obj(string json)
        {
            var o = PaJson.Parse(json) as Dictionary<string, object>;
            PaAssert.NotNull(o, "object");
            return o;
        }

        [PaTest]
        public static void Scalars()
        {
            PaAssert.Equal<object>(true, PaJson.Parse("true"));
            PaAssert.Equal<object>(false, PaJson.Parse(" false "));
            PaAssert.Null(PaJson.Parse("null"));
            PaAssert.Equal<object>("x", PaJson.Parse("\"x\""));
            PaAssert.Equal<object>(0L, PaJson.Parse("0"));
            PaAssert.Equal<object>(-0L, PaJson.Parse("-0"));
        }

        [PaTest]
        public static void IntegersAreExactLongs()
        {
            PaAssert.Equal<object>(4294967295L, PaJson.Parse("4294967295"));
            PaAssert.Equal<object>(2713970726L, PaJson.Parse("2713970726"));
            PaAssert.Equal<object>(9007199254740993L, PaJson.Parse("9007199254740993"));   // not representable as double
            PaAssert.Equal<object>(long.MaxValue, PaJson.Parse("9223372036854775807"));
            PaAssert.Equal<object>(long.MinValue, PaJson.Parse("-9223372036854775808"));
            PaAssert.Equal<object>(-1L, PaJson.Parse("-1"));
        }

        [PaTest]
        public static void BigIntegersFallBackToDouble()
        {
            object v = PaJson.Parse("18446744073709551616");
            PaAssert.True(v is double, "double");
            PaAssert.Near(18446744073709551616.0, (double)v, 1.0);
        }

        [PaTest]
        public static void FractionsAndExponents()
        {
            PaAssert.Equal<object>(1.5, PaJson.Parse("1.5"));
            PaAssert.Equal<object>(-0.25, PaJson.Parse("-0.25"));
            PaAssert.Equal<object>(1000.0, PaJson.Parse("1e3"));
            PaAssert.Equal<object>(1000.0, PaJson.Parse("1E+3"));
            PaAssert.Equal<object>(0.0012, PaJson.Parse("1.2e-3"));
            PaAssert.Equal<object>(-120.0, PaJson.Parse("-1.2E2"));
        }

        [PaTest]
        public static void Escapes()
        {
            PaAssert.Equal<object>("a\"b\\c/d\b\f\n\r\t", PaJson.Parse("\"a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t\""));
            PaAssert.Equal<object>("\u00e9A", PaJson.Parse("\"\\u00e9\\u0041\""));
            PaAssert.Equal<object>("\U0001F600", PaJson.Parse("\"\\ud83d\\ude00\""));
            PaAssert.Equal<object>("English(UK)\\en-GB_TY1.wem", PaJson.Parse("\"English(UK)\\\\en-GB_TY1.wem\""));
            PaAssert.Equal<object>("\u010de\u0161tina", PaJson.Parse("\"\u010de\u0161tina\""));   // raw UTF-16 text
        }

        [PaTest]
        public static void NestedStructures()
        {
            var o = Obj("{\"a\":[1,[2,3],{\"b\":null}],\"c\":{},\"d\":[],\"e\":\"\"}");
            var a = (List<object>)o["a"];
            PaAssert.Equal(3, a.Count);
            PaAssert.Equal<object>(1L, a[0]);
            PaAssert.SequenceEqual(new object[] { 2L, 3L }, (List<object>)a[1]);
            var inner = (Dictionary<string, object>)a[2];
            PaAssert.True(inner.ContainsKey("b") && inner["b"] == null, "null member kept");
            PaAssert.Equal(0, ((Dictionary<string, object>)o["c"]).Count);
            PaAssert.Equal(0, ((List<object>)o["d"]).Count);
            PaAssert.Equal<object>("", o["e"]);
        }

        [PaTest]
        public static void WhitespaceAndBom()
        {
            var o = Obj("\uFEFF \r\n\t{ \"k\" :\n [ 1 , 2 ] \t}\n");
            PaAssert.SequenceEqual(new object[] { 1L, 2L }, (List<object>)o["k"]);
        }

        [PaTest]
        public static void RepeatedKeyKeepsLast()
        {
            PaAssert.Equal<object>(2L, Obj("{\"k\":1,\"k\":2}")["k"]);
        }

        [PaTest]
        public static void KeysAreCaseSensitive()
        {
            var o = Obj("{\"id\":1,\"Id\":2}");
            PaAssert.Equal(2, o.Count);
        }

        [PaTest]
        public static void MalformedInputThrows()
        {
            string[] bad =
            {
                "", " ", "{", "}", "[1,]", "{\"a\":1,}", "{\"a\" 1}", "{a:1}", "[1 2]", "01", "1.", ".5", "-", "+1",
                "1e", "1e+", "\"abc", "\"\\x\"", "\"\\u12\"", "\"\\u12G4\"", "\"a\nb\"", "tru", "nul", "[1]]", "{} {}",
                "'a'", "NaN", "Infinity", "[", "[[]", "{\"a\":}",
            };
            foreach (string s in bad)
                PaAssert.Throws<PaDataException>(() => PaJson.Parse(s), "input " + PaAssert.Show(s));
            PaAssert.Throws<PaDataException>(() => PaJson.Parse(null), "null input");
        }

        [PaTest]
        public static void ErrorNamesTheOffset()
        {
            var e = PaAssert.Throws<PaDataException>(() => PaJson.Parse("[1, 2, x]"));
            PaAssert.True(e.Message.Contains("offset 7"), e.Message);
        }

        [PaTest]
        public static void DeepNestingIsRejectedNotCrashing()
        {
            string deep = new string('[', 10000) + new string(']', 10000);
            PaAssert.Throws<PaDataException>(() => PaJson.Parse(deep));
            string ok = new string('[', 100) + new string(']', 100);
            PaAssert.NotNull(PaJson.Parse(ok));
        }
    }
}
