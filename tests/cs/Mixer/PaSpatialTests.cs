using System;

namespace PortAudio.Tests
{
    public static class PaSpatialTests
    {
        static void Check(bool ok, string what) { MixFix.Check(ok, what); }
        static void Near(double w, double g, double t, string what) { MixFix.Near(w, g, t, what); }

        static PaListenerPose Pose(PaVec3 pos, PaVec3 front, PaVec3 top)
        {
            return new PaListenerPose { Position = pos, Front = front, Top = top };
        }

        [PaTest]

        public static void PanLeftAndRightInUnitySpace()
        {
            var p = Pose(new PaVec3(0, 0, 0), new PaVec3(0, 0, 1), new PaVec3(0, 1, 0));
            Near(1.0, PaSpatial.Pan(PaSpatial.Azimuth(p, new PaVec3(5, 0, 0)), 0), 1e-5, "+X is right");
            Near(-1.0, PaSpatial.Pan(PaSpatial.Azimuth(p, new PaVec3(-5, 0, 0)), 0), 1e-5, "-X is left");
            Near(0.0, PaSpatial.Pan(PaSpatial.Azimuth(p, new PaVec3(0, 0, 5)), 0), 1e-5, "ahead is centre");
            Near(0.0, PaSpatial.Pan(PaSpatial.Azimuth(p, new PaVec3(0, 0, -5)), 0), 1e-5, "behind is centre");
            Near(Math.Sin(Math.PI / 4), PaSpatial.Pan(PaSpatial.Azimuth(p, new PaVec3(3, 0, 3)), 0), 1e-5, "45 degrees right");
            Near(0.0, PaSpatial.Azimuth(p, new PaVec3(0, 0, 0)), 1e-6, "emitter on the listener");

            // listener at (10,0,0) facing +X: right = cross(up, +X) = -Z
            var q = Pose(new PaVec3(10, 0, 0), new PaVec3(2, 0, 0), new PaVec3(0, 3, 0));
            Near(1.0, PaSpatial.Pan(PaSpatial.Azimuth(q, new PaVec3(10, 0, -4)), 0), 1e-5, "rotated listener: -Z is right");
            Near(-1.0, PaSpatial.Pan(PaSpatial.Azimuth(q, new PaVec3(10, 0, 4)), 0), 1e-5, "rotated listener: +Z is left");

            var zero = new PaListenerPose();
            Near(1.0, PaSpatial.Pan(PaSpatial.Azimuth(zero, new PaVec3(1, 0, 0)), 0), 1e-5, "unset pose uses the identity");
        }

        [PaTest]

        public static void SpreadNarrowsPan()
        {
            Near(0.5, PaSpatial.Pan((float)(Math.PI / 2), 50f), 1e-5, "spread 50");
            Near(0.0, PaSpatial.Pan((float)(Math.PI / 2), 100f), 1e-5, "spread 100");
            Near(-1.0, PaSpatial.Pan((float)(-Math.PI / 2), -10f), 1e-5, "spread clamped at 0");
        }

        static PaAttenuation Att()
        {
            return new PaAttenuation { Id = 500, Rtpcs = new PaRtpc[0], Curves = new[] {
                new PaAttenuationCurve { Use = "volumeDry", Points = MixFix.Points(0, 0, 10, -6, 100, -96) },
                new PaAttenuationCurve { Use = "lpf", Points = MixFix.Points(0, 0, 100, 50) },
                new PaAttenuationCurve { Use = "spread", Points = MixFix.Points(0, 100, 20, 0) } } };
        }

        [PaTest]

        public static void AttenuationAtDistances()
        {
            var att = Att();
            var p = Pose(new PaVec3(0, 0, 0), new PaVec3(0, 0, 1), new PaVec3(0, 1, 0));
            var r = PaSpatial.Evaluate(p, new PaVec3(0, 0, 5), 1f, att);
            Near(5.0, r.Distance, 1e-5, "distance");
            Near(-3.0, r.VolumeDb, 1e-4, "half way to 10");
            Near(2.5, r.Lpf, 1e-4, "lpf curve");
            Near(75.0, r.Spread, 1e-4, "spread curve");
            r = PaSpatial.Evaluate(p, new PaVec3(0, 30, 40), 1f, att);
            Near(50.0, r.Distance, 1e-4, "3-4-5");
            Near(-6.0 - 90.0 * 40.0 / 90.0, r.VolumeDb, 1e-3, "on the second segment");
            r = PaSpatial.Evaluate(p, new PaVec3(0, 0, 500), 1f, att);
            Near(-96.0, r.VolumeDb, 1e-4, "clamped past the last point");
            r = PaSpatial.Evaluate(p, new PaVec3(20, 0, 0), 2f, att);
            Near(10.0, r.CurveX, 1e-5, "scaling divides the distance");
            Near(-6.0, r.VolumeDb, 1e-4, "scaled attenuation");
            Near(50.0, r.Spread, 1e-4, "spread at the scaled distance");
            Near(0.5, r.Pan, 1e-4, "right, halved by spread 50");
            Near(20.0, PaSpatial.ScaledDistance(20f, 0f), 1e-6, "zero scaling counts as 1");
            Near(0.0, PaSpatial.EvalCurve(null, "volumeDry", 10f), 1e-6, "no attenuation");
        }

        [PaTest]

        public static void GainModel3D()
        {
            var init = MixFix.InitBank();
            var bank = MixFix.Bank("Scenario", 5);
            bank.Attenuations.Add(Att());
            var amb = MixFix.Node(100, 0, MixFix.Sfx, 0f);
            amb.Positioning = new PaPositioning { ThreeD = true, Attenuation = 500, UsePanner = true, PanLR = -100 };
            bank.Nodes.Add(amb);
            bank.Nodes.Add(MixFix.Node(101, 100, 0, 0f));
            var w = MixFix.World(init, bank);
            w.Positions[9] = new PaVec3(-10, 0, 0);
            var gm = new PaGainModel(w, MixFix.Spec(101, 9));
            Check(gm.ThreeD && gm.Attenuation != null, "3D with attenuation");
            var buses = new PaBusState(w);
            buses.BeginFrame(0, null);
            var m = gm.Evaluate(buses, 0, 0);
            Near(-6.0 - 1.0, m.GainDb, 1e-3, "volumeDry at 10 plus master");
            Near(5.0, m.Lpf, 1e-3, "attenuation lpf");
            Near(-0.5, m.Pan, 1e-4, "left, spread 50");
            w.Positions[9] = new PaVec3(30, 0, 0);
            m = gm.Evaluate(buses, 0, 0);
            Near(1.0, m.Pan, 1e-4, "moved to the right, no spread");
            w.AttenuationScaling = 3f;
            m = gm.Evaluate(buses, 0, 0);
            Near(-6.0 - 1.0, m.GainDb, 1e-3, "scaling 3 at 30 equals 10");
        }
    }
}
