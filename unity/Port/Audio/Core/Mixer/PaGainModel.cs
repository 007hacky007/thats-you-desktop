// Per-voice gain model of the PortAudio mixer (design 7.5). It sums the node hierarchy
// properties and RTPC curves, the output bus graph, 3D attenuation, the engine-rolled spec
// offsets, gain slots and fades, and the output trim, and maps the result to AudioSource
// volume, pitch, stereo pan and low-pass cutoff. Static chain properties are cached when the
// voice starts; RTPC, bus and 3D terms are recomputed every frame. Pure and UnityEngine-free.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    /// <summary>One frame of mixer output for a voice.</summary>
    public struct PaMix
    {
        public float GainDb;        // total gain, dB
        public float PreBusDb;      // gain without bus terms (meter input)
        public float PitchCents;
        public float Lpf;           // 0..100
        public float Pan;           // -1..1
        public bool Muted;          // listener mask, background-music mute or zero output-bus volume
        public float Volume;        // AudioSource.volume = min(1, 10^(GainDb/20)), 0 when muted
        public bool Clamped;        // the linear gain was above 1 and clamped
        public float PitchRatio;    // AudioSource.pitch
        public float CutoffHz;      // AudioLowPassFilter.cutoffFrequency
        public bool LpfEnabled;     // filter enabled (lpf >= 0.5)
        public bool ThreeD;
        public PaSpatialResult Spatial;
    }

    public sealed class PaGainModel
    {
        public enum ParamKind { Other, Volume, MakeUpGain, BusVolume, OutputBusVolume, Pitch, Lpf, PanLR }

        public const float MinPitchRatio = 0.05f, MaxPitchRatio = 3f;
        public const float MinCutoffHz = 20f, MaxCutoffHz = 22000f;
        public const float LpfOffBelow = 0.5f;

        struct NodeRtpc { public PaRtpc Rtpc; public ParamKind Kind; }

        readonly IPaWorld world;
        readonly PaVoiceSpec spec;
        readonly List<NodeRtpc> chainRtpcs = new List<NodeRtpc>();
        readonly List<PaRtpc> outputBusRtpcs = new List<PaRtpc>();

        public uint OutputBus { get; private set; }
        public uint BusSetterNode { get; private set; }
        public PaPositioning Positioning { get; private set; }
        public PaAttenuation Attenuation { get; private set; }
        public float StaticDb { get; private set; }         // sum of chain Volume + MakeUpGain
        public float StaticOutputBusDb { get; private set; }
        public float StaticPitch { get; private set; }
        public float StaticLpf { get; private set; }
        public float Priority { get; private set; }         // leaf Props.Priority (0..100, 50 default)
        public int ChainLength { get; private set; }

        public PaGainModel(IPaWorld world, PaVoiceSpec spec)
        {
            this.world = world;
            this.spec = spec ?? new PaVoiceSpec();
            Priority = 50f;
            var banks = world == null ? null : world.Banks;
            if (banks == null) return;

            List<uint> chain = banks.Chain(this.spec.NodeId) ?? new List<uint>();
            PaNode setter = null, last = null;
            float db = 0f, pitch = 0f, lpf = 0f;
            int count = 0;
            foreach (uint id in chain)
            {
                var n = banks.Node(id);
                if (n == null) continue;
                if (count == 0 && n.Props != null) Priority = n.Props.Priority;
                count++;
                last = n;
                if (setter == null && n.Bus != 0) setter = n;
                if (n.Props != null) { db += n.Props.Volume + n.Props.MakeUpGain; pitch += n.Props.Pitch; lpf += n.Props.Lpf; }
                if (n.Rtpcs != null)
                    foreach (var r in n.Rtpcs)
                    {
                        if (r == null || r.Curve == null || r.Curve.Length == 0) continue;
                        var k = Classify(r.Param);
                        if (k == ParamKind.Volume || k == ParamKind.MakeUpGain || k == ParamKind.Pitch || k == ParamKind.Lpf)
                            chainRtpcs.Add(new NodeRtpc { Rtpc = r, Kind = k });
                    }
            }
            ChainLength = count;
            StaticDb = db; StaticPitch = pitch; StaticLpf = lpf;
            if (setter == null) setter = last;                 // the top of the chain routes to the master bus
            if (setter != null)
            {
                BusSetterNode = setter.Id;
                if (setter.Props != null) StaticOutputBusDb = setter.Props.OutputBusVolume;
                if (setter.Rtpcs != null)
                    foreach (var r in setter.Rtpcs)
                        if (r != null && r.Curve != null && r.Curve.Length > 0 && Classify(r.Param) == ParamKind.OutputBusVolume)
                            outputBusRtpcs.Add(r);
            }
            OutputBus = banks.OutputBus(this.spec.NodeId);
            Positioning = banks.EffectivePositioning(this.spec.NodeId);
            if (Positioning != null && Positioning.ThreeD && Positioning.Attenuation != 0)
                Attenuation = banks.Attenuation(Positioning.Attenuation);
        }

        public bool ThreeD { get { return Positioning != null && Positioning.ThreeD; } }

        public static ParamKind Classify(string param)
        {
            if (string.IsNullOrEmpty(param)) return ParamKind.Other;
            switch (param.ToLowerInvariant())
            {
                case "volume": return ParamKind.Volume;
                case "makeupgain": return ParamKind.MakeUpGain;
                case "busvolume": return ParamKind.BusVolume;
                case "outputbusvolume": return ParamKind.OutputBusVolume;
                case "pitch": return ParamKind.Pitch;
                case "lpf": return ParamKind.Lpf;
                case "panlr": return ParamKind.PanLR;
                default: return ParamKind.Other;
            }
        }

        /// <summary>Evaluate this frame. buses may be null (no bus terms); voiceDb is the voice's
        /// gain slots plus fades in dB; trimDb is the output trim.</summary>
        public PaMix Evaluate(PaBusState buses, float voiceDb, float trimDb)
        {
            var m = new PaMix();
            ulong go = spec.GameObject;

            float db = StaticDb, pitch = StaticPitch, lpf = StaticLpf, outBus = StaticOutputBusDb;
            if (world != null)
            {
                foreach (var nr in chainRtpcs)
                {
                    float y = PaCurves.Evaluate(nr.Rtpc.Curve, world.GetRtpc(nr.Rtpc.Rtpc, go));
                    switch (nr.Kind)
                    {
                        case ParamKind.Volume: case ParamKind.MakeUpGain: db += y; break;
                        case ParamKind.Pitch: pitch += y; break;
                        case ParamKind.Lpf: lpf += y; break;
                    }
                }
                foreach (var r in outputBusRtpcs) outBus += PaCurves.Evaluate(r.Curve, world.GetRtpc(r.Rtpc, go));
            }
            db += outBus;

            // 3D or 2D panning and distance terms
            m.ThreeD = ThreeD;
            if (m.ThreeD && world != null)
            {
                m.Spatial = PaSpatial.Evaluate(world.Listener, world.GetPosition(go), world.GetAttenuationScaling(go), Attenuation);
                db += m.Spatial.VolumeDb;
                lpf += m.Spatial.Lpf;
                m.Pan = m.Spatial.Pan;
            }
            else if (Positioning != null && Positioning.UsePanner)
            {
                m.Pan = Clamp(Positioning.PanLR / 100f, -1f, 1f);
            }

            db += spec.VolumeDb + voiceDb + trimDb;
            bool muted = false;
            if (world != null)
            {
                float obv = world.GetOutputBusVolume(go);
                if (!(obv > 0f)) muted = true;
                else db += 20f * (float)Math.Log10(obv);
                if ((world.GetListenerMask(go) & 1u) == 0) muted = true;
            }
            m.PreBusDb = db;

            if (buses != null && OutputBus != 0)
            {
                db += buses.BusDb(OutputBus);
                pitch += buses.BusPitch(OutputBus);
                lpf += buses.BusLpf(OutputBus);
                if (buses.IsMuted(OutputBus)) muted = true;
            }

            m.GainDb = db;
            m.PitchCents = pitch + spec.PitchCents;
            m.Lpf = Clamp(lpf + spec.Lpf, 0f, 100f);
            m.Muted = muted;

            bool clamped;
            float vol = VolumeFromDb(db, out clamped);
            m.Volume = muted ? 0f : vol;
            m.Clamped = !muted && clamped;
            m.PitchRatio = PitchRatio(m.PitchCents);
            m.CutoffHz = CutoffHz(m.Lpf);
            m.LpfEnabled = m.Lpf >= LpfOffBelow;
            return m;
        }

        /// <summary>min(1, 10^(db/20)); clamped is true when the linear value exceeded 1.</summary>
        public static float VolumeFromDb(float db, out bool clamped)
        {
            clamped = false;
            if (float.IsNaN(db) || db <= -200f) return 0f;
            double lin = Math.Pow(10.0, db / 20.0);
            if (lin > 1.0 + 1e-4) { clamped = true; return 1f; }
            return lin > 1.0 ? 1f : (float)lin;
        }

        /// <summary>clamp(2^(cents/1200), 0.05, 3).</summary>
        public static float PitchRatio(float cents)
        {
            if (float.IsNaN(cents)) return 1f;
            return Clamp((float)Math.Pow(2.0, cents / 1200.0), MinPitchRatio, MaxPitchRatio);
        }

        /// <summary>clamp(20 * 2^((100 - lpf)/10), 20, 22000) Hz, lpf in 0..100.</summary>
        public static float CutoffHz(float lpf)
        {
            float l = Clamp(float.IsNaN(lpf) ? 0f : lpf, 0f, 100f);
            return Clamp((float)(20.0 * Math.Pow(2.0, (100.0 - l) / 10.0)), MinCutoffHz, MaxCutoffHz);
        }

        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
