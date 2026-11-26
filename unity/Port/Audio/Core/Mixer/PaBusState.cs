// Per-frame bus state for the PortAudio mixer: the summed bus volume, pitch and LPF of every
// bus (with its parents), bus ducking with attack, recovery and maximum, the meter
// approximation that writes sidechain RTPCs through IPaWorld.SetMeterRtpc, and the
// background-music mute. Pure and UnityEngine-free.
//
// Frame protocol (driven by PaUnityVoices.Tick, or by tests):
//   BeginFrame(now, liveOutputBusses)   duck activity from the voices that are audible now
//   BusDb / BusPitch / BusLpf / IsMuted  memoised per frame
//   MeterInput(bus, preBusDb)           once per audible voice
//   EndFrame(now)                       meter smoothing and SetMeterRtpc writes

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaBusState
    {
        sealed class BusTerms { public float Db, Pitch, Lpf, LocalDb; public bool Muted; }

        sealed class DuckState
        {
            public uint Source; public PaDuck Duck; public int RecoveryMs;
            public double Progress;            // 0 = not ducked .. 1 = fully ducked
            public double LastActive = double.NegativeInfinity;
        }

        sealed class MeterState
        {
            public uint Bus; public PaMeter Meter;
            public float Value; public bool Written; public float LastWritten;
            public double HoldUntil = double.NegativeInfinity;
            public float FrameMax; public bool FrameHas;
        }

        readonly IPaWorld world;
        readonly Dictionary<uint, BusTerms> frame = new Dictionary<uint, BusTerms>();
        readonly Dictionary<uint, float> duckDb = new Dictionary<uint, float>();
        readonly HashSet<uint> active = new HashSet<uint>();
        readonly List<DuckState> ducks = new List<DuckState>();
        readonly List<MeterState> meters = new List<MeterState>();
        readonly Dictionary<uint, MeterState> meterByBus = new Dictionary<uint, MeterState>();
        readonly HashSet<uint> warnedFx = new HashSet<uint>();
        int bankVersion = int.MinValue;
        double lastNow = double.NaN;

        public PaBusState(IPaWorld world) { this.world = world; }

        PaBankStore Banks { get { return world == null ? null : world.Banks; } }

        /// <summary>Number of buses with an active duck contribution (diagnostics).</summary>
        public int DuckedBusCount { get { return duckDb.Count; } }

        // ---- structure cache (rebuilt when the loaded bank set changes) ----

        void Refresh()
        {
            var banks = Banks;
            int v = banks == null ? -1 : banks.Version;
            if (v == bankVersion) return;
            bankVersion = v;

            var oldDucks = new Dictionary<string, DuckState>();
            foreach (var d in ducks) oldDucks[d.Source + ":" + d.Duck.Bus] = d;
            var oldMeters = new Dictionary<uint, MeterState>(meterByBus);
            ducks.Clear(); meters.Clear(); meterByBus.Clear();
            if (banks == null) return;

            var seen = new HashSet<uint>();
            foreach (var bank in banks.Loaded)
            {
                if (bank == null || bank.Busses == null) continue;
                foreach (var listed in bank.Busses)
                {
                    if (listed == null || !seen.Add(listed.Id)) continue;
                    var bus = banks.Bus(listed.Id) ?? listed;
                    if (bus.Ducks != null)
                        foreach (var d in bus.Ducks)
                        {
                            if (d == null || d.Bus == 0) continue;
                            DuckState st;
                            if (!oldDucks.TryGetValue(bus.Id + ":" + d.Bus, out st)) st = new DuckState();
                            st.Source = bus.Id; st.Duck = d; st.RecoveryMs = bus.DuckRecoveryMs;
                            ducks.Add(st);
                        }
                    if (bus.Fx != null)
                        foreach (var fx in bus.Fx)
                        {
                            if (fx == null || fx.Effect == 0) continue;
                            var eff = banks.Effect(fx.Effect);
                            if (eff != null && eff.Meter != null && !fx.Bypass)
                            {
                                if (meterByBus.ContainsKey(bus.Id)) continue;
                                MeterState ms;
                                if (!oldMeters.TryGetValue(bus.Id, out ms))
                                {
                                    ms = new MeterState();
                                    ms.Value = eff.Meter.MinDb;
                                }
                                ms.Bus = bus.Id; ms.Meter = eff.Meter;
                                meters.Add(ms); meterByBus[bus.Id] = ms;
                            }
                            else if (warnedFx.Add(bus.Id))
                            {
                                PaLog.Trace("warn", "msg", "bus effect not reproduced", "bus", bus.Id,
                                    "effect", fx.Effect, "plugin", eff == null ? "missing" : (eff.PluginName ?? "other"));
                            }
                        }
                }
            }
        }

        // ---- frame ----

        /// <summary>Start a frame: ducking advances from the output busses of the voices that are
        /// audible now (a source bus is active while any of them is under it).</summary>
        public void BeginFrame(double now, IEnumerable<uint> liveOutputBusses)
        {
            Refresh();
            frame.Clear();
            double dt = double.IsNaN(lastNow) ? 0 : Math.Max(0, now - lastNow);
            lastNow = now;

            active.Clear();
            var banks = Banks;
            if (liveOutputBusses != null && banks != null)
                foreach (uint b in liveOutputBusses)
                {
                    int guard = 0;
                    var bus = banks.Bus(b);
                    while (bus != null && guard++ < 64 && active.Add(bus.Id))
                        bus = bus.Parent == 0 ? null : banks.Bus(bus.Parent);
                }

            duckDb.Clear();
            foreach (var d in ducks)
            {
                if (active.Contains(d.Source))
                {
                    d.LastActive = now;
                    d.Progress = Step(d.Progress, +1, dt, d.Duck.FadeOutMs);
                }
                else
                {
                    // recovery fade starts DuckRecoveryMs after the source went quiet
                    double recovering = now - d.LastActive - Math.Max(0, d.RecoveryMs) / 1000.0;
                    if (recovering >= 0) d.Progress = Step(d.Progress, -1, Math.Min(dt, recovering), d.Duck.FadeInMs);
                }
                if (d.Progress <= 0) continue;
                float db = d.Duck.VolumeDb * (float)PaCurves.Shape(d.Duck.Curve, d.Progress);
                float cur;
                duckDb.TryGetValue(d.Duck.Bus, out cur);
                duckDb[d.Duck.Bus] = cur + db;
            }
            if (banks != null && duckDb.Count > 0)
            {
                var keys = new List<uint>(duckDb.Keys);
                foreach (var k in keys)
                {
                    var target = banks.Bus(k);
                    // MaxDuckDb 0 (the JSON default) means "no limit".
                    if (target != null && target.MaxDuckDb < 0f && duckDb[k] < target.MaxDuckDb) duckDb[k] = target.MaxDuckDb;
                }
            }
            foreach (var m in meters) { m.FrameHas = false; m.FrameMax = float.NegativeInfinity; }
        }

        static double Step(double p, int dir, double dt, int ms)
        {
            if (ms <= 0) return dir > 0 ? 1.0 : 0.0;
            p += dir * dt * 1000.0 / ms;
            return p < 0 ? 0 : (p > 1 ? 1 : p);
        }

        /// <summary>Current duck applied to a bus (dB, &lt;= 0), excluding its parents.</summary>
        public float DuckDb(uint busId)
        {
            float v;
            return duckDb.TryGetValue(busId, out v) ? v : 0f;
        }

        BusTerms Terms(uint busId, int depth)
        {
            BusTerms t;
            if (frame.TryGetValue(busId, out t)) return t;
            t = new BusTerms();
            var banks = Banks;
            var bus = banks == null ? null : banks.Bus(busId);
            if (bus == null || depth > 64) { frame[busId] = t; return t; }
            frame[busId] = t; // cycle guard: a re-entry sees zeros

            float vol = 0f, pitch = 0f, lpf = 0f;
            if (bus.Props != null) { vol += bus.Props.Volume + bus.Props.BusVolume; pitch += bus.Props.Pitch; lpf += bus.Props.Lpf; }
            if (bus.Rtpcs != null)
                foreach (var r in bus.Rtpcs)
                {
                    if (r == null || r.Curve == null || r.Curve.Length == 0) continue;
                    float y = PaCurves.Evaluate(r.Curve, world.GetRtpc(r.Rtpc, PaIds.GlobalObject));
                    switch (PaGainModel.Classify(r.Param))
                    {
                        case PaGainModel.ParamKind.Volume:
                        case PaGainModel.ParamKind.BusVolume: vol += y; break;
                        case PaGainModel.ParamKind.Pitch: pitch += y; break;
                        case PaGainModel.ParamKind.Lpf: lpf += y; break;
                    }
                }
            vol += DuckDb(busId);
            t.LocalDb = vol;
            t.Muted = bus.BackgroundMusic && world.BackgroundMusicMuted;
            if (bus.Parent != 0 && bus.Parent != busId)
            {
                var p = Terms(bus.Parent, depth + 1);
                t.Db = p.Db + vol; t.Pitch = p.Pitch + pitch; t.Lpf = p.Lpf + lpf; t.Muted |= p.Muted;
            }
            else { t.Db = vol; t.Pitch = pitch; t.Lpf = lpf; }
            return t;
        }

        /// <summary>busDb(b) = busDb(parent) + Volume + BusVolume + RTPC(volume, busVolume; global) + duck(b).</summary>
        public float BusDb(uint busId) { return busId == 0 ? 0f : Terms(busId, 0).Db; }
        public float BusPitch(uint busId) { return busId == 0 ? 0f : Terms(busId, 0).Pitch; }
        public float BusLpf(uint busId) { return busId == 0 ? 0f : Terms(busId, 0).Lpf; }
        /// <summary>Background-music mute: muted when the world says so and the bus chain has BackgroundMusic.</summary>
        public bool IsMuted(uint busId) { return busId != 0 && Terms(busId, 0).Muted; }

        /// <summary>Feed one audible voice to the meters: preBusDb is the voice gain without any bus
        /// terms. Each metered bus on the chain sees the gain before that bus.</summary>
        public void MeterInput(uint outputBus, float preBusDb)
        {
            if (meters.Count == 0 || outputBus == 0) return;
            var banks = Banks;
            if (banks == null) return;
            float level = preBusDb;
            int guard = 0;
            var bus = banks.Bus(outputBus);
            while (bus != null && guard++ < 64)
            {
                MeterState m;
                if (meterByBus.TryGetValue(bus.Id, out m))
                {
                    m.FrameHas = true;
                    if (level > m.FrameMax) m.FrameMax = level;
                }
                level += Terms(bus.Id, 0).LocalDb;
                bus = bus.Parent == 0 || bus.Parent == bus.Id ? null : banks.Bus(bus.Parent);
            }
        }

        /// <summary>Finish a frame: smooth each meter toward this frame's level with attack, hold and
        /// release, then write it with World.SetMeterRtpc when it changed.</summary>
        public void EndFrame(double now, double dt)
        {
            foreach (var m in meters)
            {
                var mt = m.Meter;
                float lo = Math.Min(mt.MinDb, mt.MaxDb), hi = Math.Max(mt.MinDb, mt.MaxDb);
                float target = m.FrameHas ? Clamp(m.FrameMax, lo, hi) : lo;
                if (target >= m.Value)
                {
                    m.Value = Smooth(m.Value, target, dt, mt.AttackSec);
                    m.HoldUntil = now + Math.Max(0, mt.HoldSec);
                }
                else if (now >= m.HoldUntil)
                {
                    m.Value = Smooth(m.Value, target, dt, mt.ReleaseSec);
                }
                m.Value = Clamp(m.Value, lo, hi);
                if (!m.Written || Math.Abs(m.Value - m.LastWritten) > 0.001f)
                {
                    m.Written = true; m.LastWritten = m.Value;
                    if (mt.GameParameter != 0) world.SetMeterRtpc(mt.GameParameter, m.Value);
                }
            }
        }

        /// <summary>Current smoothed meter value of a bus, NaN when the bus has no meter.</summary>
        public float MeterValue(uint busId)
        {
            MeterState m;
            return meterByBus.TryGetValue(busId, out m) ? m.Value : float.NaN;
        }

        static float Smooth(float cur, float target, double dt, float tauSec)
        {
            if (!(tauSec > 0f)) return target;
            if (dt <= 0) return cur;
            double k = 1.0 - Math.Exp(-dt / tauSec);
            return (float)(cur + (target - cur) * k);
        }

        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
