// Maps parsed JSON (PaJson) onto the PaModel classes, following the data rules of the audio
// architecture (section 4.1):
// - key = field name with the first letter lowercased; unknown keys are ignored;
// - a missing key (or JSON null) keeps the field's default; arrays and lists are never null;
// - Props is always present, the other nested objects are null unless present;
// - enum fields are member names read case-insensitively (a JSON integer is accepted too);
//   PaCurve fields are integers (AkCurveInterpolation 0..9), names are accepted as well;
// - PaPoint is [x, y, interp] (interp defaults to Linear), PaRange is {"min":a,"max":b},
//   PaCue is [pos, "label"], Props.Unknown is {"59": 1.5}, Manifest.Names is [[id, "NAME"], ...].
// A value of the wrong type throws PaDataException naming the key.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public static class PaModelReader
    {
        public static PaManifest ReadManifest(string json)
        {
            var o = Root(json, "manifest");
            var m = new PaManifest();
            m.Format = I(o, "format", m.Format);
            m.WwiseBankVersion = I(o, "wwiseBankVersion", m.WwiseBankVersion);
            m.DefaultLanguage = S(o, "defaultLanguage", m.DefaultLanguage);
            m.Languages = Lst(o, "languages", Language);
            m.Banks = Lst(o, "banks", BankInfo);
            m.Names = Names(o, "names");
            return m;
        }

        public static PaBank ReadBank(string json)
        {
            var o = Root(json, "bank");
            var b = new PaBank();
            b.Format = I(o, "format", b.Format);
            b.Name = S(o, "name", b.Name);
            b.Language = S(o, "language", b.Language);
            b.LanguageKey = S(o, "languageKey", b.LanguageKey);
            b.Id = U(o, "id", b.Id);
            b.Events = Lst(o, "events", Event);
            b.Actions = Lst(o, "actions", Action);
            b.Nodes = Lst(o, "nodes", Node);
            b.Busses = Lst(o, "busses", Bus);
            b.Attenuations = Lst(o, "attenuations", Attenuation);
            b.GameParameters = Lst(o, "gameParameters", GameParameter);
            b.StateGroups = Lst(o, "stateGroups", StateGroup);
            b.Effects = Lst(o, "effects", Effect);
            return b;
        }

        public static void ReadMediaTable(string json, out string scope, out string externalPrefix,
            out List<PaMediaEntry> media, out List<PaMediaEntry> external)
        {
            var o = Root(json, "media table");
            string sc = S(o, "scope", "") ?? "";
            scope = sc;
            externalPrefix = S(o, "externalPrefix", "") ?? "";
            Func<Dictionary<string, object>, PaMediaEntry> read = d =>
            {
                PaMediaEntry e = MediaEntry(d);
                if (e.Scope == null)
                    e.Scope = sc;
                return e;
            };
            media = Lst(o, "media", read);
            external = Lst(o, "external", read);
        }

        // ---- manifest ----

        static PaLanguage Language(Dictionary<string, object> o)
        {
            var l = new PaLanguage();
            l.Name = S(o, "name", l.Name);
            l.Key = S(o, "key", l.Key);
            l.BankLanguageId = I(o, "bankLanguageId", l.BankLanguageId);
            return l;
        }

        static PaBankInfo BankInfo(Dictionary<string, object> o)
        {
            var b = new PaBankInfo();
            b.Name = S(o, "name", b.Name);
            b.File = S(o, "file", b.File);
            b.Id = U(o, "id", b.Id);
            b.Localized = B(o, "localized", b.Localized);
            b.Languages = new List<string>(Strings(o, "languages"));
            return b;
        }

        static Dictionary<uint, string> Names(Dictionary<string, object> o, string key)
        {
            var names = new Dictionary<uint, string>();
            object v;
            if (!Has(o, key, out v))
                return names;
            var list = v as List<object>;
            if (list != null)
            {
                foreach (object item in list)
                {
                    var pair = item as List<object>;
                    if (pair == null || pair.Count < 2)
                        throw new PaDataException("names: expected [id, \"NAME\"] pairs");
                    names[ToUInt(pair[0], key)] = ToStr(pair[1], key);
                }
                return names;
            }
            var map = v as Dictionary<string, object>;
            if (map != null)
            {
                foreach (KeyValuePair<string, object> kv in map)
                {
                    uint id;
                    if (!uint.TryParse(kv.Key, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out id))
                        throw new PaDataException("names: invalid id '" + kv.Key + "'");
                    names[id] = ToStr(kv.Value, key);
                }
                return names;
            }
            throw new PaDataException("names: expected an array");
        }

        // ---- bank objects ----

        static PaEvent Event(Dictionary<string, object> o)
        {
            var e = new PaEvent();
            e.Id = U(o, "id", e.Id);
            e.Name = S(o, "name", e.Name);
            e.Actions = UInts(o, "actions");
            return e;
        }

        static PaAction Action(Dictionary<string, object> o)
        {
            var a = new PaAction();
            a.Id = U(o, "id", a.Id);
            a.Target = U(o, "target", a.Target);
            a.StateGroup = U(o, "stateGroup", a.StateGroup);
            a.State = U(o, "state", a.State);
            a.SwitchGroup = U(o, "switchGroup", a.SwitchGroup);
            a.SwitchValue = U(o, "switchValue", a.SwitchValue);
            a.BankId = U(o, "bankId", a.BankId);
            a.Type = En(o, "type", a.Type);
            a.RawType = I(o, "rawType", a.RawType);
            a.TargetMode = En(o, "targetMode", a.TargetMode);
            a.GameObjectScoped = B(o, "gameObjectScoped", a.GameObjectScoped);
            a.TargetIsBus = B(o, "targetIsBus", a.TargetIsBus);
            a.ValueRelative = B(o, "valueRelative", a.ValueRelative);
            a.BypassTransition = B(o, "bypassTransition", a.BypassTransition);
            a.SeekRelative = B(o, "seekRelative", a.SeekRelative);
            a.SeekToMarker = B(o, "seekToMarker", a.SeekToMarker);
            a.DelayMs = I(o, "delayMs", a.DelayMs);
            a.DelayMinMs = I(o, "delayMinMs", a.DelayMinMs);
            a.DelayMaxMs = I(o, "delayMaxMs", a.DelayMaxMs);
            a.TransitionMs = I(o, "transitionMs", a.TransitionMs);
            a.TransitionMinMs = I(o, "transitionMinMs", a.TransitionMinMs);
            a.TransitionMaxMs = I(o, "transitionMaxMs", a.TransitionMaxMs);
            a.Curve = Cv(o, "curve", a.Curve);
            a.Probability = F(o, "probability", a.Probability);
            a.Value = F(o, "value", a.Value);
            a.ValueMin = F(o, "valueMin", a.ValueMin);
            a.ValueMax = F(o, "valueMax", a.ValueMax);
            a.SeekValue = F(o, "seekValue", a.SeekValue);
            a.Exceptions = Arr(o, "exceptions", ObjectRef);
            return a;
        }

        static PaObjectRef ObjectRef(object v, string key)
        {
            var r = new PaObjectRef();
            var o = v as Dictionary<string, object>;
            if (o == null)
            {
                r.Id = ToUInt(v, key);   // a bare id
                return r;
            }
            r.Id = U(o, "id", r.Id);
            r.IsBus = B(o, "isBus", r.IsBus);
            return r;
        }

        static PaNode Node(Dictionary<string, object> o)
        {
            var n = new PaNode();
            n.Id = U(o, "id", n.Id);
            n.Parent = U(o, "parent", n.Parent);
            n.Bus = U(o, "bus", n.Bus);
            n.Type = En(o, "type", n.Type);
            n.Name = S(o, "name", n.Name);
            n.Children = UInts(o, "children");
            n.Props = Props(o, "props");
            n.Positioning = Opt(o, "positioning", Positioning);
            n.Rtpcs = Arr(o, "rtpcs", Rtpc);
            n.Advanced = Opt(o, "advanced", Advanced);
            n.Source = Opt(o, "source", Source);
            n.RanSeq = Opt(o, "ranSeq", RanSeq);
            n.Switch = Opt(o, "switch", Switch);
            n.Music = Opt(o, "music", Music);
            return n;
        }

        static PaProps Props(Dictionary<string, object> parent, string key)
        {
            var p = new PaProps();
            object v;
            Dictionary<string, object> o = Has(parent, key, out v) ? ToObj(v, key) : null;
            if (o != null)
            {
                p.Volume = F(o, "volume", p.Volume);
                p.Pitch = F(o, "pitch", p.Pitch);
                p.Lpf = F(o, "lpf", p.Lpf);
                p.BusVolume = F(o, "busVolume", p.BusVolume);
                p.OutputBusVolume = F(o, "outputBusVolume", p.OutputBusVolume);
                p.MakeUpGain = F(o, "makeUpGain", p.MakeUpGain);
                p.Lfe = F(o, "lfe", p.Lfe);
                p.PanLR = F(o, "panLR", p.PanLR);
                p.PanFR = F(o, "panFR", p.PanFR);
                p.CenterPct = F(o, "centerPct", p.CenterPct);
                p.InitialDelay = F(o, "initialDelay", p.InitialDelay);
                p.Probability = F(o, "probability", p.Probability);
                p.Priority = F(o, "priority", p.Priority);
                p.Loop = I(o, "loop", p.Loop);
                p.VolumeRand = Rg(o, "volumeRand", p.VolumeRand);
                p.PitchRand = Rg(o, "pitchRand", p.PitchRand);
                p.LpfRand = Rg(o, "lpfRand", p.LpfRand);
                p.InitialDelayRand = Rg(o, "initialDelayRand", p.InitialDelayRand);
            }
            p.Unknown = new Dictionary<int, double>();
            object u;
            if (o != null && Has(o, "unknown", out u))
            {
                foreach (KeyValuePair<string, object> kv in ToObj(u, "unknown"))
                {
                    int id;
                    if (!int.TryParse(kv.Key, System.Globalization.NumberStyles.AllowLeadingSign,
                            System.Globalization.CultureInfo.InvariantCulture, out id))
                        throw new PaDataException("unknown: invalid prop id '" + kv.Key + "'");
                    p.Unknown[id] = ToDouble(kv.Value, "unknown");
                }
            }
            return p;
        }

        static PaPositioning Positioning(Dictionary<string, object> o)
        {
            var p = new PaPositioning();
            p.ThreeD = B(o, "threeD", p.ThreeD);
            p.Spatialize = B(o, "spatialize", p.Spatialize);
            p.UsePanner = B(o, "usePanner", p.UsePanner);
            p.Attenuation = U(o, "attenuation", p.Attenuation);
            p.PanLR = F(o, "panLR", p.PanLR);
            p.PanFR = F(o, "panFR", p.PanFR);
            p.CenterPct = F(o, "centerPct", p.CenterPct);
            return p;
        }

        static PaRtpc Rtpc(Dictionary<string, object> o)
        {
            var r = new PaRtpc();
            r.Rtpc = U(o, "rtpc", r.Rtpc);
            r.Param = S(o, "param", r.Param);
            r.Accumulation = S(o, "accumulation", r.Accumulation);
            r.Scaling = S(o, "scaling", r.Scaling);
            r.ParamId = I(o, "paramId", r.ParamId);
            r.Curve = Points(o, "curve");
            return r;
        }

        static PaAdvanced Advanced(Dictionary<string, object> o)
        {
            var a = new PaAdvanced();
            a.MaxInstances = I(o, "maxInstances", a.MaxInstances);
            a.VirtualBehavior = I(o, "virtualBehavior", a.VirtualBehavior);
            a.BelowThreshold = I(o, "belowThreshold", a.BelowThreshold);
            a.KillNewest = B(o, "killNewest", a.KillNewest);
            a.UseVirtual = B(o, "useVirtual", a.UseVirtual);
            a.GlobalLimit = B(o, "globalLimit", a.GlobalLimit);
            return a;
        }

        static PaSource Source(Dictionary<string, object> o)
        {
            var s = new PaSource();
            s.Plugin = U(o, "plugin", s.Plugin);
            s.Media = U(o, "media", s.Media);
            s.InMemorySize = U(o, "inMemorySize", s.InMemorySize);
            s.Kind = S(o, "kind", s.Kind);
            s.Stream = S(o, "stream", s.Stream);
            s.LanguageSpecific = B(o, "languageSpecific", s.LanguageSpecific);
            return s;
        }

        static PaWeighted Weighted(object v, string key)
        {
            var w = new PaWeighted();
            var o = v as Dictionary<string, object>;
            if (o == null)
            {
                var pair = v as List<object>;
                if (pair != null && pair.Count >= 1)
                {
                    w.Id = ToUInt(pair[0], key);
                    if (pair.Count >= 2 && pair[1] != null)
                        w.Weight = ToInt(pair[1], key);
                    return w;
                }
                w.Id = ToUInt(v, key);
                return w;
            }
            w.Id = U(o, "id", w.Id);
            w.Weight = I(o, "weight", w.Weight);
            return w;
        }

        static PaRanSeq RanSeq(Dictionary<string, object> o)
        {
            var r = new PaRanSeq();
            r.Sequence = B(o, "sequence", r.Sequence);
            r.Shuffle = B(o, "shuffle", r.Shuffle);
            r.Continuous = B(o, "continuous", r.Continuous);
            r.ResetEachPlay = B(o, "resetEachPlay", r.ResetEachPlay);
            r.RestartBackward = B(o, "restartBackward", r.RestartBackward);
            r.Global = B(o, "global", r.Global);
            r.UseWeights = B(o, "useWeights", r.UseWeights);
            r.AvoidRepeat = I(o, "avoidRepeat", r.AvoidRepeat);
            r.LoopCount = I(o, "loopCount", r.LoopCount);
            r.LoopMin = I(o, "loopMin", r.LoopMin);
            r.LoopMax = I(o, "loopMax", r.LoopMax);
            r.Transition = S(o, "transition", r.Transition);
            r.TransitionSec = F(o, "transitionSec", r.TransitionSec);
            r.TransitionMinSec = F(o, "transitionMinSec", r.TransitionMinSec);
            r.TransitionMaxSec = F(o, "transitionMaxSec", r.TransitionMaxSec);
            r.Playlist = Arr(o, "playlist", Weighted);
            return r;
        }

        static PaSwitchAssign SwitchAssign(Dictionary<string, object> o)
        {
            var a = new PaSwitchAssign();
            a.Value = U(o, "value", a.Value);
            a.Nodes = UInts(o, "nodes");
            return a;
        }

        static PaSwitchChild SwitchChild(Dictionary<string, object> o)
        {
            var c = new PaSwitchChild();
            c.Node = U(o, "node", c.Node);
            c.FirstOnly = B(o, "firstOnly", c.FirstOnly);
            c.ContinuePlayback = B(o, "continuePlayback", c.ContinuePlayback);
            c.OnSwitchMode = I(o, "onSwitchMode", c.OnSwitchMode);
            c.FadeOutMs = I(o, "fadeOutMs", c.FadeOutMs);
            c.FadeInMs = I(o, "fadeInMs", c.FadeInMs);
            return c;
        }

        static PaSwitch Switch(Dictionary<string, object> o)
        {
            var s = new PaSwitch();
            s.IsState = B(o, "isState", s.IsState);
            s.Continuous = B(o, "continuous", s.Continuous);
            s.Group = U(o, "group", s.Group);
            s.Default = U(o, "default", s.Default);
            s.Assignments = Arr(o, "assignments", SwitchAssign);
            s.ChildParams = Arr(o, "childParams", SwitchChild);
            return s;
        }

        static PaMarker Marker(Dictionary<string, object> o)
        {
            var m = new PaMarker();
            m.Id = U(o, "id", m.Id);
            m.PosMs = D(o, "posMs", m.PosMs);
            m.Name = S(o, "name", m.Name);
            m.Kind = S(o, "kind", m.Kind);
            return m;
        }

        static PaClip Clip(Dictionary<string, object> o)
        {
            var c = new PaClip();
            c.SubTrack = I(o, "subTrack", c.SubTrack);
            c.Media = U(o, "media", c.Media);
            c.PlayAtMs = D(o, "playAtMs", c.PlayAtMs);
            c.BeginTrimMs = D(o, "beginTrimMs", c.BeginTrimMs);
            c.EndTrimMs = D(o, "endTrimMs", c.EndTrimMs);
            c.SrcDurationMs = D(o, "srcDurationMs", c.SrcDurationMs);
            return c;
        }

        static PaAutomation Automation(Dictionary<string, object> o)
        {
            var a = new PaAutomation();
            a.Clip = I(o, "clip", a.Clip);
            a.Type = S(o, "type", a.Type);
            a.Points = Points(o, "points");
            return a;
        }

        static PaPlaylistItem PlaylistItem(Dictionary<string, object> o)
        {
            var p = new PaPlaylistItem();
            p.Id = U(o, "id", p.Id);
            p.Segment = U(o, "segment", p.Segment);
            p.Type = S(o, "type", p.Type);
            p.Loop = I(o, "loop", p.Loop);
            p.Weight = I(o, "weight", p.Weight);
            p.AvoidRepeat = I(o, "avoidRepeat", p.AvoidRepeat);
            p.Shuffle = B(o, "shuffle", p.Shuffle);
            p.Children = Arr(o, "children", PlaylistItem);
            return p;
        }

        static PaMusicArg MusicArg(Dictionary<string, object> o)
        {
            var a = new PaMusicArg();
            a.Group = U(o, "group", a.Group);
            a.IsState = B(o, "isState", a.IsState);
            return a;
        }

        static PaMusicPath MusicPath(Dictionary<string, object> o)
        {
            var p = new PaMusicPath();
            p.Keys = UInts(o, "keys");
            p.Node = U(o, "node", p.Node);
            p.Weight = I(o, "weight", p.Weight);
            return p;
        }

        static PaStinger Stinger(Dictionary<string, object> o)
        {
            var s = new PaStinger();
            s.Trigger = U(o, "trigger", s.Trigger);
            s.Segment = U(o, "segment", s.Segment);
            s.CueFilter = U(o, "cueFilter", s.CueFilter);
            s.Sync = S(o, "sync", s.Sync);
            s.DontRepeatMs = I(o, "dontRepeatMs", s.DontRepeatMs);
            s.LookAheadSegments = I(o, "lookAheadSegments", s.LookAheadSegments);
            return s;
        }

        static PaTransitionSrc TransitionSrc(Dictionary<string, object> o)
        {
            var t = new PaTransitionSrc();
            t.FadeMs = I(o, "fadeMs", t.FadeMs);
            t.FadeOffsetMs = I(o, "fadeOffsetMs", t.FadeOffsetMs);
            t.SyncRaw = I(o, "syncRaw", t.SyncRaw);
            t.Curve = Cv(o, "curve", t.Curve);
            t.Sync = S(o, "sync", t.Sync);
            t.CueFilter = U(o, "cueFilter", t.CueFilter);
            t.PlayPostExit = B(o, "playPostExit", t.PlayPostExit);
            return t;
        }

        static PaTransitionDst TransitionDst(Dictionary<string, object> o)
        {
            var t = new PaTransitionDst();
            t.FadeMs = I(o, "fadeMs", t.FadeMs);
            t.FadeOffsetMs = I(o, "fadeOffsetMs", t.FadeOffsetMs);
            t.EntryRaw = I(o, "entryRaw", t.EntryRaw);
            t.Curve = Cv(o, "curve", t.Curve);
            t.CueFilter = U(o, "cueFilter", t.CueFilter);
            t.JumpTo = U(o, "jumpTo", t.JumpTo);
            t.Entry = S(o, "entry", t.Entry);
            t.PlayPreEntry = B(o, "playPreEntry", t.PlayPreEntry);
            t.MatchSourceCueName = B(o, "matchSourceCueName", t.MatchSourceCueName);
            return t;
        }

        static PaTransitionSegment TransitionSegment(Dictionary<string, object> o)
        {
            var t = new PaTransitionSegment();
            t.Segment = U(o, "segment", t.Segment);
            t.FadeInMs = I(o, "fadeInMs", t.FadeInMs);
            t.FadeOutMs = I(o, "fadeOutMs", t.FadeOutMs);
            t.FadeInCurve = Cv(o, "fadeInCurve", t.FadeInCurve);
            t.FadeOutCurve = Cv(o, "fadeOutCurve", t.FadeOutCurve);
            t.PlayPreEntry = B(o, "playPreEntry", t.PlayPreEntry);
            t.PlayPostExit = B(o, "playPostExit", t.PlayPostExit);
            return t;
        }

        static PaTransitionRule TransitionRule(Dictionary<string, object> o)
        {
            var r = new PaTransitionRule();
            r.Src = Ints(o, "src");
            r.Dst = Ints(o, "dst");
            r.SrcRule = Opt(o, "srcRule", TransitionSrc);
            r.DstRule = Opt(o, "dstRule", TransitionDst);
            r.Segment = Opt(o, "segment", TransitionSegment);
            return r;
        }

        static PaMusic Music(Dictionary<string, object> o)
        {
            var m = new PaMusic();
            m.Tempo = F(o, "tempo", m.Tempo);
            m.TimeSigNum = I(o, "timeSigNum", m.TimeSigNum);
            m.TimeSigDen = I(o, "timeSigDen", m.TimeSigDen);
            m.LookAheadMs = I(o, "lookAheadMs", m.LookAheadMs);
            m.GridPeriodMs = D(o, "gridPeriodMs", m.GridPeriodMs);
            m.GridOffsetMs = D(o, "gridOffsetMs", m.GridOffsetMs);
            m.DurationMs = D(o, "durationMs", m.DurationMs);
            m.EntryMs = D(o, "entryMs", m.EntryMs);
            m.ExitMs = D(o, "exitMs", m.ExitMs);
            m.Stingers = Arr(o, "stingers", Stinger);
            m.Transitions = Arr(o, "transitions", TransitionRule);
            m.Markers = Arr(o, "markers", Marker);
            m.TrackType = S(o, "trackType", m.TrackType);
            m.Mode = S(o, "mode", m.Mode);
            m.Sources = Arr(o, "sources", Source);
            m.Clips = Arr(o, "clips", Clip);
            m.Automation = Arr(o, "automation", Automation);
            m.Playlist = Opt(o, "playlist", PlaylistItem);
            m.ContinuePlaying = B(o, "continuePlaying", m.ContinuePlaying);
            m.Arguments = Arr(o, "arguments", MusicArg);
            m.Paths = Arr(o, "paths", MusicPath);
            return m;
        }

        static PaDuck Duck(Dictionary<string, object> o)
        {
            var d = new PaDuck();
            d.Bus = U(o, "bus", d.Bus);
            d.VolumeDb = F(o, "volumeDb", d.VolumeDb);
            d.FadeOutMs = I(o, "fadeOutMs", d.FadeOutMs);
            d.FadeInMs = I(o, "fadeInMs", d.FadeInMs);
            d.Curve = Cv(o, "curve", d.Curve);
            d.TargetProp = S(o, "targetProp", d.TargetProp);
            return d;
        }

        static PaBusFx BusFx(Dictionary<string, object> o)
        {
            var f = new PaBusFx();
            f.Index = I(o, "index", f.Index);
            f.Effect = U(o, "effect", f.Effect);
            f.Bypass = B(o, "bypass", f.Bypass);
            return f;
        }

        static PaBus Bus(Dictionary<string, object> o)
        {
            var b = new PaBus();
            b.Id = U(o, "id", b.Id);
            b.Parent = U(o, "parent", b.Parent);
            b.Name = S(o, "name", b.Name);
            b.IsAux = B(o, "isAux", b.IsAux);
            b.BackgroundMusic = B(o, "backgroundMusic", b.BackgroundMusic);
            b.Props = Props(o, "props");
            b.Rtpcs = Arr(o, "rtpcs", Rtpc);
            b.Ducks = Arr(o, "ducks", Duck);
            b.DuckRecoveryMs = I(o, "duckRecoveryMs", b.DuckRecoveryMs);
            b.MaxDuckDb = F(o, "maxDuckDb", b.MaxDuckDb);
            b.Fx = Arr(o, "fx", BusFx);
            return b;
        }

        static PaAttenuationCurve AttenuationCurve(Dictionary<string, object> o)
        {
            var c = new PaAttenuationCurve();
            c.Use = S(o, "use", c.Use);
            c.Scaling = S(o, "scaling", c.Scaling);
            c.Points = Points(o, "points");
            return c;
        }

        static PaAttenuation Attenuation(Dictionary<string, object> o)
        {
            var a = new PaAttenuation();
            a.Id = U(o, "id", a.Id);
            a.ConeUse = B(o, "coneUse", a.ConeUse);
            a.ConeInnerDeg = F(o, "coneInnerDeg", a.ConeInnerDeg);
            a.ConeOuterDeg = F(o, "coneOuterDeg", a.ConeOuterDeg);
            a.ConeMaxDb = F(o, "coneMaxDb", a.ConeMaxDb);
            a.ConeLpf = F(o, "coneLpf", a.ConeLpf);
            a.Curves = Arr(o, "curves", AttenuationCurve);
            a.Rtpcs = Arr(o, "rtpcs", Rtpc);
            return a;
        }

        static PaGameParameter GameParameter(Dictionary<string, object> o)
        {
            var g = new PaGameParameter();
            g.Id = U(o, "id", g.Id);
            g.Name = S(o, "name", g.Name);
            g.RampType = S(o, "rampType", g.RampType);
            g.Default = F(o, "default", g.Default);
            g.RampUp = F(o, "rampUp", g.RampUp);
            g.RampDown = F(o, "rampDown", g.RampDown);
            return g;
        }

        static PaStateGroup StateGroup(Dictionary<string, object> o)
        {
            var s = new PaStateGroup();
            s.Id = U(o, "id", s.Id);
            s.Name = S(o, "name", s.Name);
            s.DefaultTransitionMs = I(o, "defaultTransitionMs", s.DefaultTransitionMs);
            return s;
        }

        static PaMeter Meter(Dictionary<string, object> o)
        {
            var m = new PaMeter();
            m.AttackSec = F(o, "attackSec", m.AttackSec);
            m.ReleaseSec = F(o, "releaseSec", m.ReleaseSec);
            m.HoldSec = F(o, "holdSec", m.HoldSec);
            m.MinDb = F(o, "minDb", m.MinDb);
            m.MaxDb = F(o, "maxDb", m.MaxDb);
            m.Mode = S(o, "mode", m.Mode);
            m.GameParameter = U(o, "gameParameter", m.GameParameter);
            return m;
        }

        static PaEffect Effect(Dictionary<string, object> o)
        {
            var e = new PaEffect();
            e.Id = U(o, "id", e.Id);
            e.Plugin = U(o, "plugin", e.Plugin);
            e.PluginName = S(o, "pluginName", e.PluginName);
            e.RawParams = S(o, "rawParams", e.RawParams);
            e.ShareSet = B(o, "shareSet", e.ShareSet);
            e.Meter = Opt(o, "meter", Meter);
            e.GainDb = F(o, "gainDb", e.GainDb);
            e.Rtpcs = Arr(o, "rtpcs", Rtpc);
            return e;
        }

        // ---- media ----

        static PaCue Cue(object v, string key)
        {
            var c = new PaCue();
            var pair = v as List<object>;
            if (pair != null)
            {
                if (pair.Count < 1)
                    throw new PaDataException(key + ": expected [pos, \"label\"]");
                c.Pos = ToLong(pair[0], key);
                if (pair.Count >= 2 && pair[1] != null)
                    c.Label = ToStr(pair[1], key);
                return c;
            }
            var o = v as Dictionary<string, object>;
            if (o == null)
                throw new PaDataException(key + ": expected [pos, \"label\"]");
            c.Pos = L(o, "pos", c.Pos);
            c.Label = S(o, "label", c.Label);
            return c;
        }

        static PaMediaEntry MediaEntry(Dictionary<string, object> o)
        {
            var m = new PaMediaEntry();
            m.Id = U(o, "id", m.Id);
            m.Name = S(o, "name", m.Name);
            m.Scope = S(o, "scope", m.Scope);
            m.File = S(o, "file", m.File);
            m.Format = S(o, "format", m.Format);
            m.Label = S(o, "label", m.Label);
            m.FullPath = S(o, "fullPath", m.FullPath);
            m.Rate = I(o, "rate", m.Rate);
            m.Channels = I(o, "channels", m.Channels);
            m.Samples = L(o, "samples", m.Samples);
            m.Bytes = L(o, "bytes", m.Bytes);
            m.Cues = Arr(o, "cues", Cue);
            return m;
        }

        // ---- helpers: structure ----

        static Dictionary<string, object> Root(string json, string what)
        {
            object v = PaJson.Parse(json);
            var o = v as Dictionary<string, object>;
            if (o == null)
                throw new PaDataException(what + ": the top level is not a JSON object");
            return o;
        }

        static bool Has(Dictionary<string, object> o, string key, out object v)
        {
            return o.TryGetValue(key, out v) && v != null;
        }

        static Dictionary<string, object> ToObj(object v, string key)
        {
            var o = v as Dictionary<string, object>;
            if (o == null)
                throw new PaDataException(key + ": expected an object");
            return o;
        }

        static List<object> ToList(object v, string key)
        {
            var l = v as List<object>;
            if (l == null)
                throw new PaDataException(key + ": expected an array");
            return l;
        }

        static T Opt<T>(Dictionary<string, object> o, string key, Func<Dictionary<string, object>, T> read) where T : class
        {
            object v;
            return Has(o, key, out v) ? read(ToObj(v, key)) : null;
        }

        static T[] Arr<T>(Dictionary<string, object> o, string key, Func<Dictionary<string, object>, T> read)
        {
            object v;
            if (!Has(o, key, out v))
                return new T[0];
            List<object> l = ToList(v, key);
            var a = new T[l.Count];
            for (int i = 0; i < a.Length; i++)
                a[i] = read(ToObj(l[i], key));
            return a;
        }

        static T[] Arr<T>(Dictionary<string, object> o, string key, Func<object, string, T> read)
        {
            object v;
            if (!Has(o, key, out v))
                return new T[0];
            List<object> l = ToList(v, key);
            var a = new T[l.Count];
            for (int i = 0; i < a.Length; i++)
                a[i] = read(l[i], key);
            return a;
        }

        static List<T> Lst<T>(Dictionary<string, object> o, string key, Func<Dictionary<string, object>, T> read)
        {
            return new List<T>(Arr(o, key, read));
        }

        static uint[] UInts(Dictionary<string, object> o, string key)
        {
            return Arr(o, key, (Func<object, string, uint>)ToUInt);
        }

        static int[] Ints(Dictionary<string, object> o, string key)
        {
            return Arr(o, key, (Func<object, string, int>)ToInt);
        }

        static string[] Strings(Dictionary<string, object> o, string key)
        {
            return Arr(o, key, (Func<object, string, string>)ToStr);
        }

        static PaPoint[] Points(Dictionary<string, object> o, string key)
        {
            return Arr(o, key, (Func<object, string, PaPoint>)Point);
        }

        static PaPoint Point(object v, string key)
        {
            var l = v as List<object>;
            if (l != null)
            {
                if (l.Count < 2)
                    throw new PaDataException(key + ": expected [x, y, interp]");
                PaCurve interp = PaCurve.Linear;
                if (l.Count >= 3 && l[2] != null)
                    interp = ToCurve(l[2], key);
                return new PaPoint(ToFloat(l[0], key), ToFloat(l[1], key), interp);
            }
            var o = v as Dictionary<string, object>;
            if (o == null)
                throw new PaDataException(key + ": expected [x, y, interp]");
            return new PaPoint(F(o, "x", 0f), F(o, "y", 0f), Cv(o, "interp", PaCurve.Linear));
        }

        static PaRange Rg(Dictionary<string, object> o, string key, PaRange def)
        {
            object v;
            if (!Has(o, key, out v))
                return def;
            var l = v as List<object>;
            if (l != null && l.Count == 2)
                return new PaRange { Min = ToFloat(l[0], key), Max = ToFloat(l[1], key) };
            Dictionary<string, object> r = ToObj(v, key);
            return new PaRange { Min = F(r, "min", def.Min), Max = F(r, "max", def.Max) };
        }

        // ---- helpers: scalars ----

        static uint U(Dictionary<string, object> o, string key, uint def)
        {
            object v;
            return Has(o, key, out v) ? ToUInt(v, key) : def;
        }

        static int I(Dictionary<string, object> o, string key, int def)
        {
            object v;
            return Has(o, key, out v) ? ToInt(v, key) : def;
        }

        static long L(Dictionary<string, object> o, string key, long def)
        {
            object v;
            return Has(o, key, out v) ? ToLong(v, key) : def;
        }

        static float F(Dictionary<string, object> o, string key, float def)
        {
            object v;
            return Has(o, key, out v) ? ToFloat(v, key) : def;
        }

        static double D(Dictionary<string, object> o, string key, double def)
        {
            object v;
            return Has(o, key, out v) ? ToDouble(v, key) : def;
        }

        static bool B(Dictionary<string, object> o, string key, bool def)
        {
            object v;
            return Has(o, key, out v) ? ToBool(v, key) : def;
        }

        static string S(Dictionary<string, object> o, string key, string def)
        {
            object v;
            return Has(o, key, out v) ? ToStr(v, key) : def;
        }

        static T En<T>(Dictionary<string, object> o, string key, T def) where T : struct
        {
            object v;
            return Has(o, key, out v) ? ToEnum<T>(v, key) : def;
        }

        static PaCurve Cv(Dictionary<string, object> o, string key, PaCurve def)
        {
            object v;
            return Has(o, key, out v) ? ToCurve(v, key) : def;
        }

        static PaCurve ToCurve(object v, string key)
        {
            if (v is long)
            {
                long n = (long)v;
                if (n < 0 || n > 9)
                    throw new PaDataException(key + ": curve " + n + " outside 0..9");
                return (PaCurve)(int)n;
            }
            return ToEnum<PaCurve>(v, key);
        }

        static T ToEnum<T>(object v, string key) where T : struct
        {
            string s = v as string;
            if (s != null)
            {
                T r;
                s = s.Trim();
                if (s.Length > 0 && !char.IsDigit(s[0]) && s[0] != '-' && s.IndexOf(',') < 0 && Enum.TryParse(s, true, out r) &&
                    Enum.IsDefined(typeof(T), r))
                    return r;
                throw new PaDataException(key + ": unknown " + typeof(T).Name + " '" + s + "'");
            }
            if (v is long)
            {
                long n = (long)v;
                if (n >= int.MinValue && n <= int.MaxValue)
                {
                    object e = Enum.ToObject(typeof(T), (int)n);
                    if (Enum.IsDefined(typeof(T), e))
                        return (T)e;
                }
                throw new PaDataException(key + ": unknown " + typeof(T).Name + " " + n);
            }
            throw new PaDataException(key + ": expected a " + typeof(T).Name + " name");
        }

        static long ToLong(object v, string key)
        {
            if (v is long)
                return (long)v;
            if (v is double)
            {
                double d = (double)v;
                if (d == Math.Floor(d) && d >= -9.2233720368547758E18 && d < 9.2233720368547758E18)
                    return (long)d;
            }
            throw new PaDataException(key + ": expected an integer");
        }

        static uint ToUInt(object v, string key)
        {
            long n = ToLong(v, key);
            if (n < 0 || n > uint.MaxValue)
                throw new PaDataException(key + ": " + n + " is not an unsigned 32-bit id");
            return (uint)n;
        }

        static int ToInt(object v, string key)
        {
            long n = ToLong(v, key);
            if (n < int.MinValue || n > int.MaxValue)
                throw new PaDataException(key + ": " + n + " is outside the int range");
            return (int)n;
        }

        static double ToDouble(object v, string key)
        {
            if (v is double)
                return (double)v;
            if (v is long)
                return (long)v;
            throw new PaDataException(key + ": expected a number");
        }

        static float ToFloat(object v, string key)
        {
            return (float)ToDouble(v, key);
        }

        static bool ToBool(object v, string key)
        {
            if (v is bool)
                return (bool)v;
            if (v is long)
                return (long)v != 0;
            throw new PaDataException(key + ": expected true or false");
        }

        static string ToStr(object v, string key)
        {
            if (v == null)
                return null;
            string s = v as string;
            if (s == null)
                throw new PaDataException(key + ": expected a string");
            return s;
        }
    }
}
