using System;
using System.Collections.Generic;

namespace PortAudio.Tests
{
    public static class PaBusStateTests
    {
        static void Check(bool ok, string what) { MixFix.Check(ok, what); }
        static void Near(double w, double g, double t, string what) { MixFix.Near(w, g, t, what); }

        // Voice bus ducks Music by -12 dB: 1000 ms attack, 500 ms recovery fade after 250 ms.
        static MixWorld DuckWorld(float maxDuck)
        {
            var init = MixFix.InitBank();
            init.Busses[3].Ducks = new[] { new PaDuck { Bus = MixFix.Music, VolumeDb = -12f, FadeOutMs = 1000, FadeInMs = 500, Curve = PaCurve.Linear } };
            init.Busses[3].DuckRecoveryMs = 250;
            init.Busses[1].MaxDuckDb = maxDuck;
            return MixFix.World(init);
        }

        [PaTest]

        public static void DuckAttackAndRecovery()
        {
            var w = DuckWorld(0f);
            var b = new PaBusState(w);
            var voice = new uint[] { MixFix.Voice };
            var none = new uint[0];
            b.BeginFrame(0.0, voice);
            Near(0.0, b.DuckDb(MixFix.Music), 1e-6, "first frame has no elapsed time");
            b.BeginFrame(0.5, voice);
            Near(-6.0, b.DuckDb(MixFix.Music), 1e-4, "half way through the attack");
            Near(-1.0 - 6.0, b.BusDb(MixFix.Music), 1e-4, "duck is part of busDb");
            Near(-1.0, b.BusDb(MixFix.Voice), 1e-4, "the ducking bus is untouched");
            b.BeginFrame(1.0, voice);
            Near(-12.0, b.DuckDb(MixFix.Music), 1e-4, "fully ducked");
            b.BeginFrame(1.5, voice);
            Near(-12.0, b.DuckDb(MixFix.Music), 1e-4, "held while active");
            b.BeginFrame(1.6, none);
            Near(-12.0, b.DuckDb(MixFix.Music), 1e-4, "recovery delay");
            b.BeginFrame(1.75, none);
            Near(-12.0, b.DuckDb(MixFix.Music), 1e-3, "recovery starts 250 ms after the source went quiet");
            b.BeginFrame(1.95, none);
            Near(-12.0 * (1 - 0.2 / 0.5), b.DuckDb(MixFix.Music), 1e-3, "recovering");
            b.BeginFrame(2.0, none);
            Near(-12.0 * (1 - 0.25 / 0.5), b.DuckDb(MixFix.Music), 1e-3, "recovering, half way");
            b.BeginFrame(2.5, none);
            Near(0.0, b.DuckDb(MixFix.Music), 1e-6, "recovered");
        }

        [PaTest]

        public static void DuckActiveForChildBusAndMaxClamp()
        {
            var init = MixFix.InitBank();
            var child = MixFix.Bus(2000, MixFix.Voice, 0f);
            init.Busses.Add(child);
            var sfxDuck = new PaDuck { Bus = MixFix.Music, VolumeDb = -12f, Curve = PaCurve.Linear };
            init.Busses[2].Ducks = new[] { sfxDuck };
            init.Busses[3].Ducks = new[] { new PaDuck { Bus = MixFix.Music, VolumeDb = -12f, Curve = PaCurve.Linear } };
            init.Busses[1].MaxDuckDb = -18f;
            var w = MixFix.World(init);
            var b = new PaBusState(w);
            b.BeginFrame(0.0, new uint[] { 2000 });
            Near(-12.0, b.DuckDb(MixFix.Music), 1e-4, "a voice on a child bus activates the parent's duck (0 ms fade)");
            b.BeginFrame(0.1, new uint[] { 2000, MixFix.Sfx });
            Near(-18.0, b.DuckDb(MixFix.Music), 1e-4, "two ducks clamped at MaxDuckDb");
            b.BeginFrame(0.2, new uint[0]);
            Near(0.0, b.DuckDb(MixFix.Music), 1e-4, "instant recovery");
        }

        [PaTest]

        public static void DuckCurveShape()
        {
            var init = MixFix.InitBank();
            init.Busses[3].Ducks = new[] { new PaDuck { Bus = MixFix.Sfx, VolumeDb = -10f, FadeOutMs = 1000, Curve = PaCurve.SCurve } };
            var w = MixFix.World(init);
            var b = new PaBusState(w);
            b.BeginFrame(0, new uint[] { MixFix.Voice });
            b.BeginFrame(0.25, new uint[] { MixFix.Voice });
            Near(-10.0 * (1 - Math.Cos(Math.PI * 0.25)) / 2, b.DuckDb(MixFix.Sfx), 1e-4, "S-curve duck");
        }

        static PaBank MeterBank(float attack, float release, float hold)
        {
            var init = MixFix.InitBank();
            init.Effects.Add(new PaEffect { Id = 900, PluginName = "meter", Rtpcs = new PaRtpc[0],
                Meter = new PaMeter { AttackSec = attack, ReleaseSec = release, HoldSec = hold, MinDb = -48f, MaxDb = 0f, GameParameter = 777 } });
            init.Busses[3].Fx = new[] { new PaBusFx { Index = 0, Effect = 900 } };
            init.Busses.Add(MixFix.Bus(2000, MixFix.Voice, -2f));
            return init;
        }

        [PaTest]

        public static void MeterWritesSidechainRtpc()
        {
            var w = MixFix.World(MeterBank(0f, 0f, 0f));
            var b = new PaBusState(w);
            b.BeginFrame(0, null);
            b.EndFrame(0, 0);
            Near(-48.0, w.Meters[777], 1e-5, "no voices: MinDb");
            b.BeginFrame(0.02, new uint[] { 2000 });
            b.MeterInput(2000, -6f);
            b.MeterInput(MixFix.Voice, -20f);
            b.MeterInput(MixFix.Sfx, 0f);                               // not under the metered bus
            b.EndFrame(0.02, 0.02);
            Near(-8.0, w.Meters[777], 1e-4, "max of the gains before the bus (child bus -2 included)");
            int writes = w.MeterWrites.Count;
            b.BeginFrame(0.04, new uint[] { 2000 });
            b.MeterInput(2000, -6f);
            b.EndFrame(0.04, 0.02);
            Check(w.MeterWrites.Count == writes, "unchanged value is not rewritten");
            b.BeginFrame(0.06, new uint[] { 2000 });
            b.MeterInput(2000, 12f);
            b.EndFrame(0.06, 0.02);
            Near(0.0, w.Meters[777], 1e-5, "clamped at MaxDb");
            Check(w.GetRtpc(777, PaIds.GlobalObject) == 0f, "the world sees the meter value");
        }

        [PaTest]

        public static void MeterAttackHoldRelease()
        {
            var w = MixFix.World(MeterBank(0.1f, 0.5f, 0.2f));
            var b = new PaBusState(w);
            b.BeginFrame(0, null);
            b.EndFrame(0, 0);
            b.BeginFrame(0.1, new uint[] { MixFix.Voice });
            b.MeterInput(MixFix.Voice, -6f);
            b.EndFrame(0.1, 0.1);
            double v = -48.0 + 42.0 * (1 - Math.Exp(-1.0));
            Near(v, w.Meters[777], 1e-3, "attack time constant");
            for (int i = 2; i <= 20; i++)
            {
                b.BeginFrame(0.1 * i, new uint[] { MixFix.Voice });
                b.MeterInput(MixFix.Voice, -6f);
                b.EndFrame(0.1 * i, 0.1);
            }
            Near(-6.0, w.Meters[777], 1e-2, "settled");
            b.BeginFrame(2.1, null);
            b.EndFrame(2.1, 0.1);
            Near(-6.0, w.Meters[777], 1e-2, "held");
            b.BeginFrame(2.2, null);
            b.EndFrame(2.2, 0.1);
            b.BeginFrame(2.3, null);
            b.EndFrame(2.3, 0.1);
            Check(w.Meters[777] < -6.5f && w.Meters[777] > -48f, "releasing after hold: " + w.Meters[777]);
            Near(b.MeterValue(MixFix.Voice), w.Meters[777], 1e-6, "MeterValue");
            Check(float.IsNaN(b.MeterValue(MixFix.Sfx)), "no meter on Sfx");
        }

        [PaTest]

        public static void SidechainRtpcDucksThroughBusCurve()
        {
            var init = MeterBank(0f, 0f, 0f);
            init.Busses[1].Rtpcs = new[] { MixFix.Rtpc(777, "volume", -48f, 0f, -12f, 0f, 0f, -9f) };
            var w = MixFix.World(init);
            var b = new PaBusState(w);
            b.BeginFrame(0, null);
            b.EndFrame(0, 0);
            Near(-1.0, b.BusDb(MixFix.Music), 1e-4, "quiet sidechain");
            b.BeginFrame(0.02, new uint[] { MixFix.Voice });
            b.MeterInput(MixFix.Voice, 0f);
            b.EndFrame(0.02, 0.02);
            b.BeginFrame(0.04, new uint[] { MixFix.Voice });
            Near(-10.0, b.BusDb(MixFix.Music), 1e-4, "loud voice ducks music next frame");
        }

        [PaTest]

        public static void BusCacheIsPerFrame()
        {
            const uint SfxVol = 0xCC01;
            var init = MixFix.InitBank();
            init.Busses[2].Rtpcs = new[] { MixFix.Rtpc(SfxVol, "volume", 0f, -100f, 100f, 0f), MixFix.Rtpc(SfxVol, "pitch", 0f, -1200f, 100f, 0f),
                MixFix.Rtpc(SfxVol, "lpf", 0f, 100f, 100f, 0f) };
            var w = MixFix.World(init);
            w.SetRtpc(SfxVol, 100f);
            var b = new PaBusState(w);
            b.BeginFrame(0, null);
            Near(-1.0, b.BusDb(MixFix.Sfx), 1e-4, "full volume");
            w.SetRtpc(SfxVol, 50f);
            Near(-1.0, b.BusDb(MixFix.Sfx), 1e-4, "cached within the frame");
            b.BeginFrame(0.02, null);
            Near(-51.0, b.BusDb(MixFix.Sfx), 1e-4, "recomputed next frame");
            Near(-600.0, b.BusPitch(MixFix.Sfx), 1e-3, "bus pitch RTPC");
            Near(50.0, b.BusLpf(MixFix.Sfx), 1e-3, "bus lpf RTPC");
            Near(0.0, b.BusDb(0), 1e-6, "no bus");
            Near(0.0, b.BusDb(424242), 1e-6, "unknown bus");
        }

        [PaTest]

        public static void BusCycleIsSafe()
        {
            var bank = MixFix.Bank("Cyc", 9);
            bank.Busses.Add(MixFix.Bus(1, 2, -1f));
            bank.Busses.Add(MixFix.Bus(2, 1, -1f));
            bank.Busses.Add(MixFix.Bus(3, 0, 0f));
            var w = MixFix.World(bank);
            var b = new PaBusState(w);
            b.BeginFrame(0, new uint[] { 1 });
            Check(!float.IsNaN(b.BusDb(1)), "terminates");
        }
    }
}
