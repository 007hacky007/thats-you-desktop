using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace PortAudio.Tests
{
    // Test-only serializer following the data rules (4.1): key = field name with the first letter
    // lowercased, defaults omitted, enums as member names, PaCurve as int, PaPoint [x,y,interp],
    // PaRange {min,max}, PaCue [pos,label], Props.Unknown {"id":v}, Names [[id,name]]. It records
    // which fields of each type carried a value, so a fixture can be checked for full coverage.
    sealed class PaTestWriter
    {
        public readonly Dictionary<Type, HashSet<string>> Written = new Dictionary<Type, HashSet<string>>();

        public object Tree(object value)
        {
            if (value == null)
                return null;
            Type t = value.GetType();
            if (value is string) return value;
            if (value is bool) return value;
            if (value is PaCurve) return (long)(int)(PaCurve)value;
            if (t.IsEnum) return value.ToString();
            if (value is uint) return (long)(uint)value;
            if (value is int) return (long)(int)value;
            if (value is long) return value;
            if (value is ushort) return (long)(ushort)value;
            if (value is float) return (double)(float)value;
            if (value is double) return value;
            if (value is PaPoint)
            {
                var p = (PaPoint)value;
                return new List<object> { (double)p.X, (double)p.Y, (long)(int)p.Interp };
            }
            if (value is PaRange)
            {
                var r = (PaRange)value;
                return new Dictionary<string, object> { { "min", (double)r.Min }, { "max", (double)r.Max } };
            }
            if (value is PaCue)
            {
                var c = (PaCue)value;
                var l = new List<object> { c.Pos };
                if (c.Label != null)
                    l.Add(c.Label);
                return l;
            }
            if (value is Dictionary<int, double>)
            {
                var d = new Dictionary<string, object>();
                foreach (KeyValuePair<int, double> kv in (Dictionary<int, double>)value)
                    d[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value;
                return d;
            }
            if (value is Dictionary<uint, string>)
            {
                var l = new List<object>();
                foreach (KeyValuePair<uint, string> kv in (Dictionary<uint, string>)value)
                    l.Add(new List<object> { (long)kv.Key, kv.Value });
                return l;
            }
            if (value is IEnumerable)
            {
                var l = new List<object>();
                foreach (object o in (IEnumerable)value)
                    l.Add(Tree(o));
                return l;
            }
            return Object(value);
        }

        Dictionary<string, object> Object(object value)
        {
            Type t = value.GetType();
            object fresh = Activator.CreateInstance(t);
            var d = new Dictionary<string, object>();
            HashSet<string> seen;
            if (!Written.TryGetValue(t, out seen))
                Written[t] = seen = new HashSet<string>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(value);
                if (IsDefault(v, f.GetValue(fresh)))
                    continue;
                object tree = Tree(v);
                var asDict = tree as Dictionary<string, object>;
                if (asDict != null && asDict.Count == 0 && f.FieldType == typeof(PaProps))
                    continue;
                d[char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1)] = tree;
                seen.Add(f.Name);
            }
            return d;
        }

        static bool IsDefault(object v, object def)
        {
            if (v == null)
                return true;
            var coll = v as ICollection;
            if (coll != null)
                return coll.Count == 0;
            return v.Equals(def);
        }

        public static bool DeepEqual(object a, object b, string path, List<string> diffs)
        {
            if (a == null || b == null)
            {
                if (a == null && b == null)
                    return true;
                diffs.Add(path + ": " + PaAssert.Show(a) + " vs " + PaAssert.Show(b));
                return false;
            }
            if ((a is long || a is double) && (b is long || b is double))
            {
                double x = Convert.ToDouble(a, CultureInfo.InvariantCulture), y = Convert.ToDouble(b, CultureInfo.InvariantCulture);
                if (Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Abs(x)))
                    return true;
                diffs.Add(path + ": " + x + " vs " + y);
                return false;
            }
            var da = a as Dictionary<string, object>;
            var db = b as Dictionary<string, object>;
            if (da != null && db != null)
            {
                bool ok = true;
                foreach (string k in da.Keys)
                {
                    if (!db.ContainsKey(k)) { diffs.Add(path + "." + k + ": missing on the right"); ok = false; }
                    else ok &= DeepEqual(da[k], db[k], path + "." + k, diffs);
                }
                foreach (string k in db.Keys)
                {
                    if (!da.ContainsKey(k)) { diffs.Add(path + "." + k + ": missing on the left"); ok = false; }
                }
                return ok;
            }
            var la = a as List<object>;
            var lb = b as List<object>;
            if (la != null && lb != null)
            {
                if (la.Count != lb.Count)
                {
                    diffs.Add(path + ": length " + la.Count + " vs " + lb.Count);
                    return false;
                }
                bool ok = true;
                for (int i = 0; i < la.Count; i++)
                    ok &= DeepEqual(la[i], lb[i], path + "[" + i + "]", diffs);
                return ok;
            }
            if (a.Equals(b))
                return true;
            diffs.Add(path + ": " + PaAssert.Show(a) + " vs " + PaAssert.Show(b));
            return false;
        }
    }

    public static class PaModelReaderTests
    {
        // Single quotes keep the fixtures readable; no value contains a quote.
        internal static string J(string s)
        {
            return s.Replace('\'', '"');
        }

        const string FullProps =
            "{'volume':-3,'pitch':-100,'lpf':20,'busVolume':-1,'outputBusVolume':-2,'makeUpGain':3,'lfe':-6,'panLR':-10," +
            "'panFR':10,'centerPct':50,'initialDelay':0.5,'probability':75,'priority':80,'loop':3," +
            "'volumeRand':{'min':-2,'max':2},'pitchRand':{'min':-50,'max':50},'lpfRand':{'min':0,'max':10}," +
            "'initialDelayRand':{'min':0,'max':0.25},'unknown':{'59':1.5,'7':-2}}";

        // A hand-written bank touching every node type and every model field.
        internal static readonly string FullBank = J(
            "{'format':1,'name':'Test_Bank','id':123,'language':'English(UK)','languageKey':'english_uk'," +
            "'events':[{'id':1001,'name':'Play_Test','actions':[2001,2002]}]," +
            "'actions':[" +
            "{'id':2001,'target':3001,'bankId':123,'type':'Play','rawType':1027,'delayMs':10,'delayMinMs':-5,'delayMaxMs':5," +
            "'transitionMs':100,'transitionMinMs':-10,'transitionMaxMs':10,'curve':5,'probability':50}," +
            "{'id':2002,'target':3002,'type':'Stop','rawType':265,'targetMode':'AllExcept','gameObjectScoped':true,'targetIsBus':true," +
            "'exceptions':[{'id':3002},{'id':5001,'isBus':true}]}," +
            "{'id':2003,'target':6001,'type':'SetGameParameter','valueRelative':true,'bypassTransition':true,'value':1.5,'valueMin':-1,'valueMax':2}," +
            "{'id':2004,'stateGroup':7001,'state':7002,'type':'SetState'}," +
            "{'id':2005,'switchGroup':7101,'switchValue':7102,'type':'SetSwitch'}," +
            "{'id':2006,'type':'Seek','seekRelative':true,'seekToMarker':true,'seekValue':0.25}]," +
            "'nodes':[" +
            "{'id':3000,'bus':5002,'type':'ActorMixer','name':'Mixer','children':[3001,3002,3003,3004],'props':" + FullProps + "," +
            "'positioning':{'threeD':true,'spatialize':true,'usePanner':true,'attenuation':8001,'panLR':-50,'panFR':25,'centerPct':75}," +
            "'rtpcs':[{'rtpc':6001,'param':'volume','accumulation':'additive','scaling':'db','curve':[[0,-96,4],[100,0,9]]}]," +
            "'advanced':{'maxInstances':2,'virtualBehavior':1,'belowThreshold':2,'killNewest':true,'useVirtual':true,'globalLimit':true}}," +
            "{'id':3001,'parent':3000,'props':{'volume':-3}," +
            "'source':{'plugin':262145,'media':9001,'inMemorySize':1024,'kind':'vorbis','stream':'embedded','languageSpecific':true}}," +
            "{'id':3002,'parent':3000,'type':'RanSeq','children':[3001],'props':{'loop':0}," +
            "'ranSeq':{'sequence':true,'shuffle':true,'continuous':true,'resetEachPlay':true,'restartBackward':true,'global':true," +
            "'useWeights':true,'avoidRepeat':2,'loopCount':0,'loopMin':1,'loopMax':3,'transition':'delay','transitionSec':0.5," +
            "'transitionMinSec':-0.25,'transitionMaxSec':0.25,'playlist':[{'id':3001,'weight':50000}]}}," +
            "{'id':3003,'parent':3000,'type':'Switch','children':[3001],'props':{'pitch':100}," +
            "'switch':{'isState':true,'continuous':true,'group':7001,'default':7002,'assignments':[{'value':7002,'nodes':[3001]}]," +
            "'childParams':[{'node':3001,'firstOnly':true,'continuePlayback':true,'onSwitchMode':1,'fadeOutMs':200,'fadeInMs':300}]}}," +
            "{'id':3004,'parent':3000,'type':'Layer','children':[3001],'props':{'lpf':10}}," +
            "{'id':4000,'type':'MusicSwitch','name':'Music','children':[4001],'props':{'volume':-1}," +
            "'music':{'stingers':[{'trigger':7201,'segment':4002,'cueFilter':14,'sync':'nextBar','dontRepeatMs':1000,'lookAheadSegments':1}]," +
            "'transitions':[{'src':[-1],'dst':[4002,4001]," +
            "'srcRule':{'fadeMs':500,'fadeOffsetMs':-100,'syncRaw':7,'curve':2,'sync':'exitMarker','cueFilter':11,'playPostExit':true}," +
            "'dstRule':{'fadeMs':400,'fadeOffsetMs':50,'entryRaw':3,'curve':7,'cueFilter':12,'jumpTo':13,'entry':'randomUserMarker'," +
            "'playPreEntry':true,'matchSourceCueName':true}," +
            "'segment':{'segment':4002,'fadeInMs':10,'fadeOutMs':20,'fadeInCurve':1,'fadeOutCurve':3,'playPreEntry':true,'playPostExit':true}}]," +
            "'continuePlaying':true,'arguments':[{'group':7101,'isState':true}],'paths':[{'keys':[7102,0],'node':4001,'weight':50}]}}," +
            "{'id':4001,'parent':4000,'type':'MusicRanSeq','children':[4002],'props':{'volume':-2}," +
            "'music':{'playlist':{'id':4101,'type':'sequenceContinuous','loop':0,'children':[" +
            "{'id':4102,'segment':4002,'type':'randomStep','loop':2,'weight':50000,'avoidRepeat':1,'shuffle':true}]}}}," +
            "{'id':4002,'parent':4001,'type':'MusicSegment','children':[4003],'props':{'volume':-4}," +
            "'music':{'tempo':120,'timeSigNum':3,'timeSigDen':8,'lookAheadMs':100,'gridPeriodMs':500,'gridOffsetMs':10,'durationMs':16000," +
            "'entryMs':500,'exitMs':15500,'markers':[{'id':1,'posMs':500,'name':'Entry','kind':'entry'},{'id':2,'posMs':15500,'kind':'exit'}," +
            "{'id':3,'posMs':8000.5,'name':'1j','kind':'custom'}]}}," +
            "{'id':4003,'parent':4002,'type':'MusicTrack','props':{'volume':-5}," +
            "'music':{'trackType':'normal','mode':'bestMatch','sources':[{'media':9002,'kind':'vorbis','stream':'streamed'}]," +
            "'clips':[{'subTrack':1,'media':9002,'playAtMs':-250,'beginTrimMs':250,'endTrimMs':-100,'srcDurationMs':16000}]," +
            "'automation':[{'clip':1,'type':'fadeIn','points':[[0,0,4],[500,1,4]]}]}}]," +
            "'busses':[" +
            "{'id':5001,'name':'Master Audio Bus','props':{'volume':-1},'fx':[{'index':1,'effect':10001,'bypass':true}]}," +
            "{'id':5002,'parent':5001,'name':'Music','isAux':true,'backgroundMusic':true,'props':{'busVolume':-2}," +
            "'rtpcs':[{'rtpc':6001,'param':'busVolume','curve':[[0,-96,4],[100,0,4]]}]," +
            "'ducks':[{'bus':5003,'volumeDb':-6,'fadeOutMs':100,'fadeInMs':200,'curve':4,'targetProp':'busVolume'}]," +
            "'duckRecoveryMs':500,'maxDuckDb':-12}]," +
            "'attenuations':[{'id':8001,'coneUse':true,'coneInnerDeg':90,'coneOuterDeg':180,'coneMaxDb':-6,'coneLpf':10," +
            "'curves':[{'use':'volumeDry','scaling':'db','points':[[0,0,4],[50,-20,2]]}]," +
            "'rtpcs':[{'rtpc':6001,'param':'other','paramId':59,'curve':[[0,0,4]]}]}]," +
            "'gameParameters':[{'id':6001,'name':'Music_Vol','rampType':'slewRate','default':100,'rampUp':10,'rampDown':20}]," +
            "'stateGroups':[{'id':7001,'name':'GAMESTATE','defaultTransitionMs':250}]," +
            "'effects':[{'id':10001,'plugin':129,'pluginName':'meter','rawParams':'00ff','shareSet':true," +
            "'meter':{'attackSec':0.5,'releaseSec':0.25,'holdSec':0.125,'minDb':-48,'maxDb':6,'mode':'peak','gameParameter':6002}," +
            "'gainDb':-3,'rtpcs':[{'rtpc':6002,'param':'other','curve':[[0,0,4]]}]}]}");

        static readonly Type[] BankTypes =
        {
            typeof(PaBank), typeof(PaEvent), typeof(PaAction), typeof(PaObjectRef), typeof(PaNode), typeof(PaProps),
            typeof(PaPositioning), typeof(PaRtpc), typeof(PaAdvanced), typeof(PaSource), typeof(PaWeighted), typeof(PaRanSeq),
            typeof(PaSwitchAssign), typeof(PaSwitchChild), typeof(PaSwitch), typeof(PaMarker), typeof(PaClip), typeof(PaAutomation),
            typeof(PaPlaylistItem), typeof(PaMusicArg), typeof(PaMusicPath), typeof(PaStinger), typeof(PaTransitionSrc),
            typeof(PaTransitionDst), typeof(PaTransitionSegment), typeof(PaTransitionRule), typeof(PaMusic), typeof(PaDuck),
            typeof(PaBusFx), typeof(PaBus), typeof(PaAttenuationCurve), typeof(PaAttenuation), typeof(PaGameParameter),
            typeof(PaStateGroup), typeof(PaMeter), typeof(PaEffect),
        };

        [PaTest]
        public static void BankRoundTripCoversEveryNodeTypeAndField()
        {
            PaBank bank = PaModelReader.ReadBank(FullBank);
            var w = new PaTestWriter();
            object back = w.Tree(bank);
            var diffs = new List<string>();
            bool same = PaTestWriter.DeepEqual(PaJson.Parse(FullBank), back, "bank", diffs);
            PaAssert.True(same, "round trip differs: " + string.Join("; ", diffs));

            var missing = new List<string>();
            foreach (Type t in BankTypes)
            {
                HashSet<string> seen;
                w.Written.TryGetValue(t, out seen);
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (seen == null || !seen.Contains(f.Name))
                        missing.Add(t.Name + "." + f.Name);
                }
            }
            PaAssert.True(missing.Count == 0, "fixture leaves fields at their default: " + string.Join(", ", missing));

            var types = new HashSet<PaNodeType>();
            foreach (PaNode n in bank.Nodes)
                types.Add(n.Type);
            foreach (PaNodeType t in Enum.GetValues(typeof(PaNodeType)))
                PaAssert.True(types.Contains(t), "node type " + t + " in the fixture");
        }

        [PaTest]
        public static void BankValuesLandInTheRightFields()
        {
            PaBank b = PaModelReader.ReadBank(FullBank);
            PaAssert.Equal(123u, b.Id);
            PaAssert.Equal("english_uk", b.LanguageKey);
            PaAction stop = b.Actions[1];
            PaAssert.Equal(PaActionType.Stop, stop.Type);
            PaAssert.Equal(PaTargetMode.AllExcept, stop.TargetMode);
            PaAssert.Equal(2, stop.Exceptions.Length);
            PaAssert.True(stop.Exceptions[1].IsBus && stop.Exceptions[1].Id == 5001u, "bus exception");
            PaAssert.Equal(PaCurve.SCurve, b.Actions[0].Curve);
            PaNode mixer = b.Nodes[0];
            PaAssert.Equal(PaNodeType.ActorMixer, mixer.Type);
            PaAssert.Near(-2, mixer.Props.VolumeRand.Min);
            PaAssert.Near(0.25, mixer.Props.InitialDelayRand.Max);
            PaAssert.Near(1.5, mixer.Props.Unknown[59]);
            PaAssert.Near(-2, mixer.Props.Unknown[7]);
            PaAssert.Equal(3, mixer.Props.Loop);
            PaPoint p = mixer.Rtpcs[0].Curve[1];
            PaAssert.Near(100, p.X);
            PaAssert.Near(0, p.Y);
            PaAssert.Equal(PaCurve.Constant, p.Interp);
            PaNode sw = b.Nodes[5];
            PaAssert.SequenceEqual(new[] { -1 }, sw.Music.Transitions[0].Src);
            PaAssert.SequenceEqual(new uint[] { 7102, 0 }, sw.Music.Paths[0].Keys);
            PaAssert.Equal(PaCurve.Log1, sw.Music.Transitions[0].SrcRule.Curve);
            PaNode seg = b.Nodes[7];
            PaAssert.Near(8000.5, seg.Music.Markers[2].PosMs);
            PaNode track = b.Nodes[8];
            PaAssert.Near(-250, track.Music.Clips[0].PlayAtMs);
            PaAssert.Equal(4102u, b.Nodes[6].Music.Playlist.Children[0].Id);
        }

        [PaTest]
        public static void EmptyBankHasEmptyLists()
        {
            PaBank b = PaModelReader.ReadBank("{}");
            PaAssert.Equal(0, b.Events.Count);
            PaAssert.Equal(0, b.Actions.Count);
            PaAssert.Equal(0, b.Nodes.Count);
            PaAssert.Equal(0, b.Busses.Count);
            PaAssert.Equal(0, b.Attenuations.Count);
            PaAssert.Equal(0, b.GameParameters.Count);
            PaAssert.Equal(0, b.StateGroups.Count);
            PaAssert.Equal(0, b.Effects.Count);
            PaAssert.Null(b.Name);
        }

        [PaTest]
        public static void Defaults()
        {
            PaBank b = PaModelReader.ReadBank(J(
                "{'events':[{'id':1}],'actions':[{'id':2}],'nodes':[{'id':3},{'id':4,'type':'RanSeq','ranSeq':{}}," +
                "{'id':5,'type':'MusicSegment','music':{'transitions':[{'srcRule':{},'dstRule':{}}]}}," +
                "{'id':6,'type':'MusicRanSeq','music':{'playlist':{}}}],'busses':[{'id':7}]}"));
            PaAssert.Equal(0, b.Events[0].Actions.Length);
            PaAction a = b.Actions[0];
            PaAssert.Equal(PaActionType.Unknown, a.Type);
            PaAssert.Equal(PaTargetMode.Element, a.TargetMode);
            PaAssert.Equal(PaCurve.Linear, a.Curve);
            PaAssert.Near(100, a.Probability);
            PaAssert.Equal(0, a.Exceptions.Length);

            PaNode n = b.Nodes[0];
            PaAssert.Equal(PaNodeType.Sound, n.Type);
            PaAssert.NotNull(n.Props, "props always present");
            PaAssert.Near(100, n.Props.Probability);
            PaAssert.Near(50, n.Props.Priority);
            PaAssert.Equal(-1, n.Props.Loop);
            PaAssert.Equal(0, n.Props.Unknown.Count);
            PaAssert.Equal(0, n.Children.Length);
            PaAssert.Equal(0, n.Rtpcs.Length);
            PaAssert.Null(n.Positioning);
            PaAssert.Null(n.Advanced);
            PaAssert.Null(n.Source);
            PaAssert.Null(n.RanSeq);
            PaAssert.Null(n.Switch);
            PaAssert.Null(n.Music);

            PaRanSeq r = b.Nodes[1].RanSeq;
            PaAssert.Equal(1, r.LoopCount);
            PaAssert.Equal("none", r.Transition);
            PaAssert.Equal(0, r.Playlist.Length);

            PaMusic m = b.Nodes[2].Music;
            PaAssert.Equal(PaCurve.Linear, m.Transitions[0].SrcRule.Curve);
            PaAssert.Equal(PaCurve.Linear, m.Transitions[0].DstRule.Curve);
            PaAssert.Null(m.Transitions[0].Segment);
            PaAssert.Equal(0, m.Transitions[0].Src.Length);
            PaAssert.Equal(0, m.Markers.Length);
            PaAssert.Equal(0, m.Sources.Length);
            PaAssert.Null(m.Playlist);
            PaPlaylistItem pl = b.Nodes[3].Music.Playlist;
            PaAssert.Equal(1, pl.Loop);
            PaAssert.Equal(0, pl.Children.Length);

            PaBus bus = b.Busses[0];
            PaAssert.NotNull(bus.Props, "bus props always present");
            PaAssert.Equal(0, bus.Ducks.Length);
            PaAssert.Equal(0, bus.Fx.Length);
            PaAssert.Equal(0, bus.Rtpcs.Length);
        }

        [PaTest]
        public static void NullMeansDefault()
        {
            PaBank b = PaModelReader.ReadBank(J("{'nodes':[{'id':1,'props':null,'children':null,'source':null,'name':null}]}"));
            PaAssert.NotNull(b.Nodes[0].Props);
            PaAssert.Equal(0, b.Nodes[0].Children.Length);
            PaAssert.Null(b.Nodes[0].Source);
        }

        [PaTest]
        public static void EnumsAreCaseInsensitive()
        {
            PaBank b = PaModelReader.ReadBank(J(
                "{'actions':[{'type':'setgameparameter','targetMode':'ALLEXCEPT'},{'type':'PlayEvent','targetMode':'all'}]," +
                "'nodes':[{'type':'musicswitch'},{'type':'MUSICTRACK'},{'type':'ranseq'}]}"));
            PaAssert.Equal(PaActionType.SetGameParameter, b.Actions[0].Type);
            PaAssert.Equal(PaTargetMode.AllExcept, b.Actions[0].TargetMode);
            PaAssert.Equal(PaActionType.PlayEvent, b.Actions[1].Type);
            PaAssert.Equal(PaTargetMode.All, b.Actions[1].TargetMode);
            PaAssert.Equal(PaNodeType.MusicSwitch, b.Nodes[0].Type);
            PaAssert.Equal(PaNodeType.MusicTrack, b.Nodes[1].Type);
            PaAssert.Equal(PaNodeType.RanSeq, b.Nodes[2].Type);
        }

        [PaTest]
        public static void BadEnumsAndCurvesThrow()
        {
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'type':'Speaker'}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'type':'Sound,Layer'}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'type':'1'}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'actions':[{'curve':10}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'actions':[{'curve':-1}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'actions':[{'curve':'wobbly'}]}")));
            PaAction a = PaModelReader.ReadBank(J("{'actions':[{'curve':'scurve'}]}")).Actions[0];
            PaAssert.Equal(PaCurve.SCurve, a.Curve);
        }

        [PaTest]
        public static void TypeErrorsNameTheKey()
        {
            var e = PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'id':-1}]}")));
            PaAssert.True(e.Message.StartsWith("id:"), e.Message);
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'id':4294967296}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'id':'12'}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'id':1.5}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':{}}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[1]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'name':5}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'busses':[{'isAux':'yes'}]}")));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank("[]"));
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank("{"));
            PaAssert.Equal(4294967295u, PaModelReader.ReadBank(J("{'id':4294967295}")).Id);
            PaAssert.Equal(7u, PaModelReader.ReadBank(J("{'id':7.0}")).Id);
        }

        [PaTest]
        public static void PointsAndRanges()
        {
            PaBank b = PaModelReader.ReadBank(J(
                "{'attenuations':[{'curves':[{'points':[[0,1],[10.5,-3,9],[20,2,4]]}]}]," +
                "'nodes':[{'props':{'pitchRand':{'min':-100,'max':100},'volumeRand':{'max':3}}}]}"));
            PaPoint[] pts = b.Attenuations[0].Curves[0].Points;
            PaAssert.Equal(3, pts.Length);
            PaAssert.Equal(PaCurve.Linear, pts[0].Interp);
            PaAssert.Near(1, pts[0].Y);
            PaAssert.Near(10.5, pts[1].X);
            PaAssert.Equal(PaCurve.Constant, pts[1].Interp);
            PaAssert.Near(-100, b.Nodes[0].Props.PitchRand.Min);
            PaAssert.Near(100, b.Nodes[0].Props.PitchRand.Max);
            PaAssert.Near(0, b.Nodes[0].Props.VolumeRand.Min);
            PaAssert.Near(3, b.Nodes[0].Props.VolumeRand.Max);
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'attenuations':[{'curves':[{'points':[[1]]}]}]}")));
        }

        [PaTest]
        public static void UnknownPropsKeepRawIds()
        {
            PaProps p = PaModelReader.ReadBank(J("{'nodes':[{'props':{'unknown':{'59':1.5,'3':-2,'100':7}}}]}")).Nodes[0].Props;
            PaAssert.Equal(3, p.Unknown.Count);
            PaAssert.Near(1.5, p.Unknown[59]);
            PaAssert.Near(-2, p.Unknown[3]);
            PaAssert.Near(7, p.Unknown[100]);
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadBank(J("{'nodes':[{'props':{'unknown':{'vol':1}}}]}")));
        }

        [PaTest]
        public static void UnknownKeysAreIgnored()
        {
            PaBank b = PaModelReader.ReadBank(J("{'future':{'x':[1,2]},'nodes':[{'id':1,'Volume':5,'extra':true}]}"));
            PaAssert.Equal(1u, b.Nodes[0].Id);
            PaAssert.Near(0, b.Nodes[0].Props.Volume, 0, "keys are case-sensitive field names");
        }

        internal static readonly string Manifest = J(
            "{'format':1,'wwiseBankVersion':113,'defaultLanguage':'English(UK)'," +
            "'languages':[{'name':'Czech','key':'czech','bankLanguageId':5},{'name':'English(UK)','key':'english_uk','bankLanguageId':11}," +
            "{'name':'French(France)','key':'french_france','bankLanguageId':13}]," +
            "'banks':[{'name':'Init','id':1355168291,'file':'banks/Init.json'}," +
            "{'name':'Commentary_Global_SoundBank','id':2713970726,'localized':true,'file':'banks/{lang}/Commentary_Global_SoundBank.json'," +
            "'languages':['czech','english_uk']}]," +
            "'names':[[1355168291,'INIT'],[3991942870,'PLAY_MUSIC_FRONTEND']]}");

        [PaTest]
        public static void ManifestReads()
        {
            PaManifest m = PaModelReader.ReadManifest(Manifest);
            PaAssert.Equal(1, m.Format);
            PaAssert.Equal(113, m.WwiseBankVersion);
            PaAssert.Equal("English(UK)", m.DefaultLanguage);
            PaAssert.Equal(3, m.Languages.Count);
            PaAssert.Equal("english_uk", m.Languages[1].Key);
            PaAssert.Equal(11, m.Languages[1].BankLanguageId);
            PaAssert.Equal(2, m.Banks.Count);
            PaAssert.False(m.Banks[0].Localized);
            PaAssert.Equal(0, m.Banks[0].Languages.Count);
            PaAssert.True(m.Banks[1].Localized);
            PaAssert.Equal(2713970726u, m.Banks[1].Id);
            PaAssert.SequenceEqual(new[] { "czech", "english_uk" }, m.Banks[1].Languages);
            PaAssert.Equal(2, m.Names.Count);
            PaAssert.Equal("PLAY_MUSIC_FRONTEND", m.Names[3991942870u]);
            PaAssert.Throws<PaDataException>(() => PaModelReader.ReadManifest(J("{'names':[[1]]}")));
            PaAssert.Equal(0, PaModelReader.ReadManifest("{}").Names.Count);
        }

        [PaTest]
        public static void ResolveLanguage()
        {
            PaManifest m = PaModelReader.ReadManifest(Manifest);
            PaAssert.Equal("czech", m.ResolveLanguage("Czech", null).Key);
            PaAssert.Equal("czech", m.ResolveLanguage("CZECH", null).Key, "case-insensitive name");
            PaAssert.Equal("french_france", m.ResolveLanguage("french_france", null).Key, "by key");
            PaAssert.Equal("french_france", m.ResolveLanguage("Czech", "French(France)").Key, "forced wins");
            PaAssert.Equal("czech", m.ResolveLanguage("Czech", "Klingon").Key, "unknown forced ignored");
            PaAssert.Equal("english_uk", m.ResolveLanguage("English(US)", null).Key, "unknown -> default");
            PaAssert.Equal("english_uk", m.ResolveLanguage(null, null).Key);
            m.DefaultLanguage = "German";
            PaAssert.Equal("czech", m.ResolveLanguage("English(US)", null).Key, "unknown default -> first");
            m.Languages.Clear();
            PaAssert.Null(m.ResolveLanguage("Czech", null));
        }

        [PaTest]
        public static void MediaTable()
        {
            string json = J(
                "{'format':1,'scope':'english_uk','language':'English(UK)','externalPrefix':'en-GB_'," +
                "'media':[{'id':591433127,'file':'lang/english_uk/591433127.ogg','format':'ogg','rate':48000,'channels':2," +
                "'samples':9709392,'bytes':4123456,'label':'Mus_Wild_L1','cues':[[0,'Entry'],[48000],[96000,'Exit']]}]," +
                "'external':[{'name':'en-gb_ty2225','file':'lang/english_uk/ext/en-GB_TY2225.ogg','format':'wav','rate':24000," +
                "'channels':1,'samples':121891,'bytes':70123,'label':'en-GB_TY2225'}]}");
            string scope, prefix;
            List<PaMediaEntry> media, external;
            PaModelReader.ReadMediaTable(json, out scope, out prefix, out media, out external);
            PaAssert.Equal("english_uk", scope);
            PaAssert.Equal("en-GB_", prefix);
            PaAssert.Equal(1, media.Count);
            PaMediaEntry e = media[0];
            PaAssert.Equal(591433127u, e.Id);
            PaAssert.Equal("english_uk", e.Scope);
            PaAssert.Equal(9709392L, e.Samples);
            PaAssert.Equal(4123456L, e.Bytes);
            PaAssert.Near(9709392 / 48000.0, e.Duration, 1e-9);
            PaAssert.Equal(3, e.Cues.Length);
            PaAssert.Equal(48000L, e.Cues[1].Pos);
            PaAssert.Null(e.Cues[1].Label);
            PaAssert.Equal("Exit", e.Cues[2].Label);
            PaAssert.Null(e.FullPath);
            PaMediaEntry x = external[0];
            PaAssert.Equal("en-gb_ty2225", x.Name);
            PaAssert.Equal("wav", x.Format);
            PaAssert.Equal("english_uk", x.Scope);
            PaAssert.Equal(0, x.Cues.Length);

            PaModelReader.ReadMediaTable("{}", out scope, out prefix, out media, out external);
            PaAssert.Equal("", scope);
            PaAssert.Equal("", prefix);
            PaAssert.Equal(0, media.Count);
            PaAssert.Equal(0, external.Count);
            PaAssert.Near(0, new PaMediaEntry { Samples = 10 }.Duration, 0, "no rate -> 0");
        }
    }

    // Shared contract helpers (PaContracts.cs).
    public static class PaContractsTests
    {
        [PaTest]
        public static void HashIsWwiseShortId()
        {
            PaAssert.Equal(1355168291u, PaIds.Hash("Init"));
            PaAssert.Equal(2713970726u, PaIds.Hash("Commentary_Global_SoundBank"));
            PaAssert.Equal(PaIds.Hash("play_music_frontend"), PaIds.Hash("PLAY_MUSIC_FRONTEND"), "lowercased first");
            PaAssert.Equal(0u, PaIds.Hash(null));
            PaAssert.Equal(0u, PaIds.Hash(""));
            // FNV-1 by hand for "a": (2166136261 * 16777619) ^ 0x61
            PaAssert.Equal(unchecked(2166136261u * 16777619u) ^ 0x61u, PaIds.Hash("A"));
        }

        [PaTest]
        public static void ObjectIds()
        {
            PaAssert.Equal(ulong.MaxValue, PaIds.GlobalObject);
            PaAssert.True(PaIds.IsGlobal(ulong.MaxValue));
            PaAssert.True(PaIds.IsGlobal(0xFFFFFFFFUL));
            PaAssert.False(PaIds.IsGlobal(0));
            PaAssert.False(PaIds.IsGlobal(12345));
            PaAssert.Equal(ulong.MaxValue, PaIds.FromRegisteredId(-1));
            PaAssert.Equal(0xFFFFFFFFFFFFFF85UL, PaIds.FromRegisteredId(-123));
            PaAssert.Equal(42UL, PaIds.FromRegisteredId(42));
        }

        [PaTest]
        public static void Decibels()
        {
            PaAssert.Near(1.0, PaDb.ToLinear(0));
            PaAssert.Near(0.5011872, PaDb.ToLinear(-6), 1e-6);
            PaAssert.Near(0.0, PaDb.ToLinear(-200));
            PaAssert.Near(0.0, PaDb.ToLinear(-500));
            PaAssert.Near(0.0, PaDb.FromLinear(1));
            PaAssert.Near(-6.0206, PaDb.FromLinear(0.5f), 1e-3);
            PaAssert.Near(-200, PaDb.FromLinear(0));
            PaAssert.Near(-200, PaDb.FromLinear(-1));
            PaAssert.Near(-200, PaDb.FromLinear(1e-30f));
            PaAssert.Near(6, PaDb.FromLinear(PaDb.ToLinear(6)), 1e-4);
        }

        [PaTest]
        public static void CurveShapes()
        {
            foreach (PaCurve c in System.Enum.GetValues(typeof(PaCurve)))
            {
                PaAssert.Near(0, PaCurves.Shape(c, 0), 1e-9, c + " at 0");
                PaAssert.Near(1, PaCurves.Shape(c, 1), 1e-9, c + " at 1");
                PaAssert.Near(0, PaCurves.Shape(c, -3), 1e-9, c + " clamped below");
                PaAssert.Near(1, PaCurves.Shape(c, 7), 1e-9, c + " clamped above");
            }
            PaAssert.Near(0.5, PaCurves.Shape(PaCurve.Linear, 0.5));
            PaAssert.Near(0.875, PaCurves.Shape(PaCurve.Log3, 0.5));
            PaAssert.Near(0.125, PaCurves.Shape(PaCurve.Exp3, 0.5));
            PaAssert.Near(1 - System.Math.Pow(0.5, 1.41), PaCurves.Shape(PaCurve.Log1, 0.5));
            PaAssert.Near(System.Math.Pow(0.5, 1.41), PaCurves.Shape(PaCurve.Exp1, 0.5));
            PaAssert.Near(System.Math.Sqrt(0.5), PaCurves.Shape(PaCurve.Sine, 0.5));
            PaAssert.Near(1 - System.Math.Sqrt(0.5), PaCurves.Shape(PaCurve.SineRecip, 0.5));
            PaAssert.Near(0.5, PaCurves.Shape(PaCurve.SCurve, 0.5));
            PaAssert.Near(0.5, PaCurves.Shape(PaCurve.InvSCurve, 0.5));
            PaAssert.Near(0.25, PaCurves.Shape(PaCurve.SCurve, 1.0 / 3.0), 1e-9);
            PaAssert.Near(0, PaCurves.Shape(PaCurve.Constant, 0.999));
        }

        [PaTest]
        public static void CurveEvaluate()
        {
            var pts = new[] { new PaPoint(0, -96, PaCurve.Linear), new PaPoint(100, 0, PaCurve.Constant), new PaPoint(200, 10, PaCurve.Linear) };
            PaAssert.Near(-96, PaCurves.Evaluate(pts, -50), 1e-6, "clamped left");
            PaAssert.Near(-96, PaCurves.Evaluate(pts, 0));
            PaAssert.Near(-48, PaCurves.Evaluate(pts, 50), 1e-5);
            PaAssert.Near(0, PaCurves.Evaluate(pts, 100));
            PaAssert.Near(0, PaCurves.Evaluate(pts, 150), 1e-6, "constant segment holds the left value");
            PaAssert.Near(10, PaCurves.Evaluate(pts, 200));
            PaAssert.Near(10, PaCurves.Evaluate(pts, 1000), 1e-6, "clamped right");
            PaAssert.Near(0, PaCurves.Evaluate(null, 3));
            PaAssert.Near(0, PaCurves.Evaluate(new PaPoint[0], 3));
            PaAssert.Near(7, PaCurves.Evaluate(new[] { new PaPoint(5, 7, PaCurve.Linear) }, 100));
            var vertical = new[] { new PaPoint(0, 1, PaCurve.Linear), new PaPoint(0, 5, PaCurve.Linear), new PaPoint(10, 5, PaCurve.Linear) };
            PaAssert.Near(5, PaCurves.Evaluate(vertical, 0.5f));
        }

        [PaTest]
        public static void RandomIsDeterministic()
        {
            var a = new PaRandom(42);
            var b = new PaRandom(42);
            var c = new PaRandom(43);
            bool differs = false;
            var first = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < 100; i++)
            {
                uint x = a.NextUInt();
                first.Add(x);
                PaAssert.Equal(x, b.NextUInt());
                differs |= x != c.NextUInt();
            }
            PaAssert.True(differs, "another seed gives another sequence");
            a.Reseed(42);
            for (int i = 0; i < 100; i++)
                PaAssert.Equal(first[i], a.NextUInt(), "reseed restarts the sequence");
            var z = new PaRandom(0);
            PaAssert.NotEqual(z.NextUInt(), z.NextUInt());
        }

        [PaTest]
        public static void RandomRanges()
        {
            var r = new PaRandom(7);
            var hits = new int[5];
            for (int i = 0; i < 5000; i++)
            {
                double d = r.NextDouble();
                PaAssert.True(d >= 0 && d < 1, "NextDouble in [0,1)");
                int k = r.Range(0, 5);
                PaAssert.True(k >= 0 && k < 5, "int range");
                hits[k]++;
                float f = r.Range(-2f, 3f);
                PaAssert.True(f >= -2f && f <= 3f, "float range");
                int n = r.Range(int.MinValue, int.MaxValue);
                PaAssert.True(n < int.MaxValue, "wide int range");
            }
            foreach (int h in hits)
                PaAssert.True(h > 800, "roughly uniform");
            PaAssert.Equal(3, r.Range(3, 3));
            PaAssert.Equal(3, r.Range(3, 1));
            PaAssert.Near(2, r.Range(2f, 2f));
            PaAssert.Near(2, r.Range(2f, -1f));
        }

        sealed class Sink : IPaTraceSink
        {
            public readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public bool Throw;
            public void Write(string kind, object[] keyValues)
            {
                if (Throw)
                    throw new System.InvalidOperationException("boom");
                Lines.Add(kind + ":" + string.Join(",", System.Array.ConvertAll(keyValues, v => PaAssert.Show(v))));
            }
            public void Flush() { }
            public void Close() { }
        }

        sealed class Platform : IPaPlatform
        {
            public readonly System.Collections.Generic.List<string> Logged = new System.Collections.Generic.List<string>();
            public string StreamingAssetsPath { get { return ""; } }
            public string[] CommandLineArgs { get { return new string[0]; } }
            public double RealtimeSeconds { get { return 0; } }
            public int FrameCount { get { return 0; } }
            public bool IsMainThread { get { return true; } }
            public PaSettings Settings { get { return new PaSettings(); } }
            public IPaTraceSink CreateTraceSink(string path) { return null; }
            public IPaVoices CreateVoices(PaMixerConfig config) { return null; }
            public IPaMusic CreateMusic() { return null; }
            public void StartDriver(System.Action tick) { }
            public void StartRecorder(string wavPath) { }
            public void StopRecorder() { }
            public void Log(int level, string message) { Logged.Add(level + " " + message); }
        }

        [PaTest]
        public static void LogRoutesToPlatformAndTrace()
        {
            IPaTraceSink oldSink = PaLog.Sink;
            IPaPlatform oldPlatform = PaRuntime.Platform;
            try
            {
                var sink = new Sink();
                var platform = new Platform();
                PaLog.Sink = null;
                PaAssert.False(PaLog.Tracing);
                PaLog.Trace("post", "id", 1u);   // no-op
                PaLog.Sink = sink;
                PaRuntime.Platform = platform;
                PaAssert.True(PaLog.Tracing);
                PaLog.Trace("post", "id", 123u, "name", "Play_X");
                PaLog.Info("hello");
                PaLog.Warn("careful");
                PaLog.Error("bad");
                PaLog.Trace("empty");
                PaAssert.SequenceEqual(new[] { "post:\"id\",123,\"name\",\"Play_X\"",
                    "warn:\"msg\",\"careful\"", "error:\"msg\",\"bad\"", "empty:" }, sink.Lines);
                PaAssert.SequenceEqual(new[] { "0 [PortAudio] hello", "1 [PortAudio] careful", "2 [PortAudio] bad" }, platform.Logged);
                sink.Throw = true;
                PaLog.Warn("sink failure is swallowed");
            }
            finally
            {
                PaLog.Sink = oldSink;
                PaRuntime.Platform = oldPlatform;
            }
        }
    }
}
