using System;
using System.Collections.Generic;

namespace PortAudio.Tests
{
    // Shared fixture for the Mixer area: a fake IPaWorld and banks built from PaModel objects.
    public sealed class MixWorld : IPaWorld
    {
        public readonly Dictionary<string, float> Rtpcs = new Dictionary<string, float>();
        public readonly Dictionary<uint, float> Meters = new Dictionary<uint, float>();
        public readonly List<KeyValuePair<uint, float>> MeterWrites = new List<KeyValuePair<uint, float>>();
        public readonly Dictionary<ulong, PaVec3> Positions = new Dictionary<ulong, PaVec3>();
        public uint ListenerMask = 1;
        public float OutputBusVolume = 1f, AttenuationScaling = 1f;
        public PaListenerPose Pose = new PaListenerPose { Front = new PaVec3(0, 0, 1), Top = new PaVec3(0, 1, 0) };
        public bool BgMuted;
        public PaBankStore Store;

        public MixWorld(PaBankStore store) { Store = store; }

        public void SetRtpc(uint id, float v) { Rtpcs[id + "@g"] = v; }
        public void SetRtpc(uint id, ulong go, float v) { Rtpcs[id + "@" + go] = v; }

        public float GetRtpc(uint rtpcId, ulong gameObject)
        {
            float v;
            if (!PaIds.IsGlobal(gameObject) && Rtpcs.TryGetValue(rtpcId + "@" + gameObject, out v)) return v;
            if (Meters.TryGetValue(rtpcId, out v)) return v;
            return Rtpcs.TryGetValue(rtpcId + "@g", out v) ? v : 0f;
        }
        public uint GetState(uint stateGroup) { return 0; }
        public uint GetSwitch(uint switchGroup, ulong gameObject) { return 0; }
        public PaVec3 GetPosition(ulong gameObject) { PaVec3 p; return Positions.TryGetValue(gameObject, out p) ? p : new PaVec3(); }
        public PaVec3 GetFront(ulong gameObject) { return new PaVec3(0, 0, 1); }
        public uint GetListenerMask(ulong gameObject) { return ListenerMask; }
        public float GetOutputBusVolume(ulong gameObject) { return OutputBusVolume; }
        public float GetAttenuationScaling(ulong gameObject) { return AttenuationScaling; }
        public PaListenerPose Listener { get { return Pose; } }
        public bool BackgroundMusicMuted { get { return BgMuted; } }
        public string VoiceLanguageKey { get { return "english_uk"; } }
        public PaRandom Random { get { return null; } }
        public PaBankStore Banks { get { return Store; } }
        public PaMediaIndex Media { get { return null; } }
        public void SetMeterRtpc(uint rtpcId, float value) { Meters[rtpcId] = value; MeterWrites.Add(new KeyValuePair<uint, float>(rtpcId, value)); }
    }

    public static class MixFix
    {
        public const uint Master = 1000, Music = 1001, Sfx = 1002, Voice = 1003;

        public static void Check(bool ok, string what) { if (!ok) throw new Exception("check failed: " + what); }

        public static void Near(double want, double got, double tol, string what)
        {
            if (double.IsNaN(got) || Math.Abs(want - got) > tol)
                throw new Exception("check failed: " + what + ": want " + want + " got " + got);
        }

        public static PaBank Bank(string name, uint id)
        {
            return new PaBank { Format = 1, Name = name, Id = id, Language = "", LanguageKey = "",
                Events = new List<PaEvent>(), Actions = new List<PaAction>(), Nodes = new List<PaNode>(),
                Busses = new List<PaBus>(), Attenuations = new List<PaAttenuation>(),
                GameParameters = new List<PaGameParameter>(), StateGroups = new List<PaStateGroup>(), Effects = new List<PaEffect>() };
        }

        public static PaNode Node(uint id, uint parent, uint bus, float vol)
        {
            return new PaNode { Id = id, Parent = parent, Bus = bus, Type = PaNodeType.Sound, Name = "n" + id,
                Children = new uint[0], Props = new PaProps { Volume = vol }, Rtpcs = new PaRtpc[0] };
        }

        public static PaBus Bus(uint id, uint parent, float vol)
        {
            return new PaBus { Id = id, Parent = parent, Name = "b" + id, Props = new PaProps { Volume = vol },
                Rtpcs = new PaRtpc[0], Ducks = new PaDuck[0], Fx = new PaBusFx[0] };
        }

        public static PaRtpc Rtpc(uint id, string param, params float[] xy)
        {
            var pts = new PaPoint[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PaPoint(xy[2 * i], xy[2 * i + 1], PaCurve.Linear);
            return new PaRtpc { Rtpc = id, Param = param, Accumulation = "additive", Curve = pts };
        }

        public static PaPoint[] Points(params float[] xy)
        {
            var pts = new PaPoint[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PaPoint(xy[2 * i], xy[2 * i + 1], PaCurve.Linear);
            return pts;
        }

        public static PaManifest Manifest()
        {
            return new PaManifest { Format = 1, WwiseBankVersion = 113, DefaultLanguage = "English(UK)",
                Languages = new List<PaLanguage>(), Banks = new List<PaBankInfo>(), Names = new Dictionary<uint, string>() };
        }

        /// <summary>Init-like bus graph: Master (-1 dB) with Music, Sfx and Voice children.</summary>
        public static PaBank InitBank()
        {
            var b = Bank("Init", 1355168291);
            b.Busses.Add(Bus(Master, 0, -1f));
            b.Busses.Add(Bus(Music, Master, 0f));
            b.Busses.Add(Bus(Sfx, Master, 0f));
            b.Busses.Add(Bus(Voice, Master, 0f));
            return b;
        }

        public static MixWorld World(params PaBank[] banks)
        {
            var store = new PaBankStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pa-mixer-none"), Manifest());
            foreach (var b in banks) store.Register(b);
            return new MixWorld(store);
        }

        public static PaVoiceSpec Spec(uint node, ulong go)
        {
            return new PaVoiceSpec { NodeId = node, GameObject = go, PlayingId = 1,
                Media = new PaMediaEntry { Id = 1, Rate = 48000, Channels = 1, Samples = 96000, Format = "ogg", File = "media/1.ogg" } };
        }
    }

    public static class PaGainModelTests
    {
        static void Check(bool ok, string what) { MixFix.Check(ok, what); }
        static void Near(double w, double g, double t, string what) { MixFix.Near(w, g, t, what); }

        [PaTest]

        public static void ChainAndBusSummation()
        {
            var init = MixFix.InitBank();
            init.Busses[2].Props.BusVolume = -0.5f;                       // Sfx: 0 + -0.5
            var bank = MixFix.Bank("SoundEffect_SoundBank", 2);
            var top = MixFix.Node(10, 0, MixFix.Sfx, -2f);
            top.Type = PaNodeType.ActorMixer;
            top.Props.OutputBusVolume = -1f;
            var leaf = MixFix.Node(11, 10, 0, -3f);
            leaf.Props.MakeUpGain = 1f;
            bank.Nodes.Add(top); bank.Nodes.Add(leaf);
            var w = MixFix.World(init, bank);

            var spec = MixFix.Spec(11, 5);
            spec.VolumeDb = -1f;
            var gm = new PaGainModel(w, spec);
            Check(gm.OutputBus == MixFix.Sfx, "output bus from the overriding ancestor");
            Check(gm.BusSetterNode == 10, "bus setter node");
            Near(-4f, gm.StaticDb, 1e-5, "static chain sum");

            var buses = new PaBusState(w);
            buses.BeginFrame(0, new uint[0]);
            Near(-1.5, buses.BusDb(MixFix.Sfx), 1e-5, "bus chain sum");
            var m = gm.Evaluate(buses, -0.25f, -2f);
            // chain -3+1-2, output bus volume -1, buses -1.5, spec -1, voice -0.25, trim -2
            Near(-9.75, m.GainDb, 1e-4, "total gain");
            Near(-8.25, m.PreBusDb, 1e-4, "pre-bus gain");
            Near(Math.Pow(10, -9.75 / 20), m.Volume, 1e-5, "linear volume");
            Check(!m.Clamped && !m.Muted, "no clamp, not muted");
        }

        [PaTest]

        public static void MasterBusWhenNoOverride()
        {
            var init = MixFix.InitBank();
            var bank = MixFix.Bank("B", 2);
            var top = MixFix.Node(20, 0, 0, 0f);
            top.Props.OutputBusVolume = -3f;
            bank.Nodes.Add(top);
            bank.Nodes.Add(MixFix.Node(21, 20, 0, 0f));
            var w = MixFix.World(init, bank);
            var gm = new PaGainModel(w, MixFix.Spec(21, 5));
            Check(gm.OutputBus == MixFix.Master, "master bus");
            Check(gm.BusSetterNode == 20, "top of chain sets the bus");
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            Near(-4.0, gm.Evaluate(buses, 0, 0).GainDb, 1e-4, "output bus volume + master");
        }

        [PaTest]

        public static void MusicVolRtpcCurveOnBus()
        {
            const uint MusicVol = 0xAA01;
            var init = MixFix.InitBank();
            init.Busses[1].Rtpcs = new[] { MixFix.Rtpc(MusicVol, "busVolume", 0f, -96f, 100f, 0f) };
            var bank = MixFix.Bank("Frontend", 3);
            bank.Nodes.Add(MixFix.Node(30, 0, MixFix.Music, 0f));
            var w = MixFix.World(init, bank);
            w.SetRtpc(MusicVol, 75f);
            w.SetRtpc(MusicVol, 5, 0f);                                  // object value must not be used for busses
            var gm = new PaGainModel(w, MixFix.Spec(30, 5));
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            Near(-24.0 - 1.0, buses.BusDb(MixFix.Music), 1e-3, "Music_Vol 75 -> -24 dB plus master");
            Near(-25.0, gm.Evaluate(buses, 0, 0).GainDb, 1e-3, "voice sees the bus RTPC");
            w.SetRtpc(MusicVol, 0f);
            buses.BeginFrame(0.02, null);
            Near(-97.0, gm.Evaluate(buses, 0, 0).GainDb, 1e-3, "RTPC recomputed per frame");
        }

        [PaTest]

        public static void NodeRtpcsUseObjectScope()
        {
            const uint Fade = 0xBB01, Lpf = 0xBB02, Rec = 0xBB03;
            var init = MixFix.InitBank();
            var bank = MixFix.Bank("S", 4);
            var top = MixFix.Node(40, 0, MixFix.Music, 0f);
            top.Rtpcs = new[] { MixFix.Rtpc(Fade, "volume", 0f, -96f, 100f, 0f), MixFix.Rtpc(Lpf, "lpf", 0f, 0f, 100f, 80f) };
            var leaf = MixFix.Node(41, 40, 0, 0f);
            leaf.Type = PaNodeType.MusicTrack;
            leaf.Rtpcs = new[] { MixFix.Rtpc(Rec, "VOLUME", 0f, -10f, 1f, 0f) };
            bank.Nodes.Add(top); bank.Nodes.Add(leaf);
            var w = MixFix.World(init, bank);
            w.SetRtpc(Fade, 100f);
            w.SetRtpc(Fade, 7, 50f);
            w.SetRtpc(Lpf, 7, 50f);
            w.SetRtpc(Rec, 1f);
            var gm = new PaGainModel(w, MixFix.Spec(41, 7));
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            var m = gm.Evaluate(buses, 0, 0);
            Near(-48.0 - 1.0, m.GainDb, 1e-3, "object-scoped FADEVOLUME, global RECORDPLAYER");
            Near(40.0, m.Lpf, 1e-3, "LOWPASSFILTER RTPC");
            Check(m.LpfEnabled, "filter on");
        }

        [PaTest]

        public static void PitchSumAndClamp()
        {
            var init = MixFix.InitBank();
            init.Busses[2].Props.Pitch = 600f;
            var bank = MixFix.Bank("S", 4);
            var n = MixFix.Node(50, 0, MixFix.Sfx, 0f);
            n.Props.Pitch = 400f;
            bank.Nodes.Add(n);
            var w = MixFix.World(init, bank);
            var spec = MixFix.Spec(50, 1);
            spec.PitchCents = 200f;
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            var m = new PaGainModel(w, spec).Evaluate(buses, 0, 0);
            Near(1200.0, m.PitchCents, 1e-3, "pitch sum");
            Near(2.0, m.PitchRatio, 1e-5, "ratio");
            Near(3.0, PaGainModel.PitchRatio(2400f), 1e-6, "clamp high");
            Near(0.05, PaGainModel.PitchRatio(-9600f), 1e-6, "clamp low");
            Near(1.0, PaGainModel.PitchRatio(0f), 1e-6, "unity");
        }

        [PaTest]

        public static void LpfCutoffMapping()
        {
            Near(20480.0, PaGainModel.CutoffHz(0f), 0.5, "lpf 0");
            Near(20.0, PaGainModel.CutoffHz(100f), 1e-3, "lpf 100");
            Near(640.0, PaGainModel.CutoffHz(50f), 1e-2, "lpf 50");
            Near(20.0 * Math.Pow(2, 7.5), PaGainModel.CutoffHz(25f), 1e-2, "lpf 25");
            Near(20.0, PaGainModel.CutoffHz(150f), 1e-3, "clamped above 100");
            Near(20480.0, PaGainModel.CutoffHz(-20f), 0.5, "clamped below 0");

            var init = MixFix.InitBank();
            var bank = MixFix.Bank("S", 4);
            var n = MixFix.Node(60, 0, MixFix.Sfx, 0f);
            n.Props.Lpf = 0.4f;
            bank.Nodes.Add(n);
            var w = MixFix.World(init, bank);
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            var m = new PaGainModel(w, MixFix.Spec(60, 1)).Evaluate(buses, 0, 0);
            Check(!m.LpfEnabled, "filter disabled below 0.5");
            n.Props.Lpf = 70f;
            var spec = MixFix.Spec(60, 1);
            spec.Lpf = 50f;
            m = new PaGainModel(w, spec).Evaluate(buses, 0, 0);
            Near(100.0, m.Lpf, 1e-4, "lpf sum clamped to 100");
            Check(m.LpfEnabled, "filter enabled");
        }

        [PaTest]

        public static void VolumeClampAndTrim()
        {
            var init = MixFix.InitBank();
            init.Busses[0].Props.Volume = 0f;
            var bank = MixFix.Bank("S", 4);
            bank.Nodes.Add(MixFix.Node(70, 0, MixFix.Sfx, 6f));
            var w = MixFix.World(init, bank);
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            var gm = new PaGainModel(w, MixFix.Spec(70, 1));
            var m = gm.Evaluate(buses, 0, 0);
            Check(m.Clamped && m.Volume == 1f, "+6 dB clamps at 1");
            m = gm.Evaluate(buses, 0, -12f);
            Check(!m.Clamped, "trim brings it under 1");
            Near(Math.Pow(10, -6.0 / 20), m.Volume, 1e-5, "trimmed volume");
            bool c;
            Check(PaGainModel.VolumeFromDb(-200f, out c) == 0f && !c, "floor is silence");
        }

        [PaTest]

        public static void MutesAndOutputBusVolume()
        {
            var init = MixFix.InitBank();
            init.Busses[1].BackgroundMusic = true;
            var bank = MixFix.Bank("S", 4);
            bank.Nodes.Add(MixFix.Node(80, 0, MixFix.Music, 0f));
            bank.Nodes.Add(MixFix.Node(81, 0, MixFix.Sfx, 0f));
            var w = MixFix.World(init, bank);
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            var music = new PaGainModel(w, MixFix.Spec(80, 1));
            var sfx = new PaGainModel(w, MixFix.Spec(81, 1));
            Check(!music.Evaluate(buses, 0, 0).Muted, "music audible");
            w.BgMuted = true;
            buses.BeginFrame(0.01, null);
            var m = music.Evaluate(buses, 0, 0);
            Check(m.Muted && m.Volume == 0f, "background music muted");
            Check(!sfx.Evaluate(buses, 0, 0).Muted, "sfx not affected");
            w.BgMuted = false;
            w.OutputBusVolume = 0.5f;
            buses.BeginFrame(0.02, null);
            Near(-1.0 + 20 * Math.Log10(0.5), sfx.Evaluate(buses, 0, 0).GainDb, 1e-4, "game object output bus volume");
            w.OutputBusVolume = 0f;
            Check(sfx.Evaluate(buses, 0, 0).Muted, "zero output bus volume mutes");
            w.OutputBusVolume = 1f;
            w.ListenerMask = 2;
            Check(sfx.Evaluate(buses, 0, 0).Muted, "listener mask without bit 0 mutes");
        }

        [PaTest]

        public static void Pan2D()
        {
            var init = MixFix.InitBank();
            var bank = MixFix.Bank("S", 4);
            var n = MixFix.Node(90, 0, MixFix.Sfx, 0f);
            n.Positioning = new PaPositioning { UsePanner = true, PanLR = -50f };
            bank.Nodes.Add(n);
            bank.Nodes.Add(MixFix.Node(91, 90, 0, 0f));
            var w = MixFix.World(init, bank);
            var m = new PaGainModel(w, MixFix.Spec(91, 1)).Evaluate(null, 0, 0);
            Near(-0.5, m.Pan, 1e-6, "panner PanLR/100 from the inherited positioning");
            n.Positioning.UsePanner = false;
            m = new PaGainModel(w, MixFix.Spec(91, 1)).Evaluate(null, 0, 0);
            Near(0.0, m.Pan, 1e-6, "centre without the panner");
        }
    }
}
