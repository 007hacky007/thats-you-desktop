// 3D positioning math for the PortAudio mixer: emitter-to-listener distance, attenuation
// scaling, attenuation curve lookup (volumeDry, lpf, spread) and azimuth panning in Unity's
// left-handed listener space. Pure and UnityEngine-free.

using System;

namespace PortAudio
{
    /// <summary>Result of evaluating an attenuation for one emitter.</summary>
    public struct PaSpatialResult
    {
        public float Distance;      // raw emitter-listener distance (world units)
        public float CurveX;        // distance used on the curves (Distance / scaling)
        public float VolumeDb;      // volumeDry curve, dB (0 when absent)
        public float Lpf;           // lpf curve, 0..100 (0 when absent)
        public float Spread;        // spread curve, 0..100 (0 when absent)
        public float Azimuth;       // radians, 0 = straight ahead, +pi/2 = right
        public float Pan;           // -1 (left) .. +1 (right)
    }

    public static class PaSpatial
    {
        public const string UseVolumeDry = "volumeDry";
        public const string UseLpf = "lpf";
        public const string UseSpread = "spread";

        static readonly PaVec3 DefaultFront = new PaVec3(0f, 0f, 1f);
        static readonly PaVec3 DefaultTop = new PaVec3(0f, 1f, 0f);

        public static float Dot(PaVec3 a, PaVec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static PaVec3 Cross(PaVec3 a, PaVec3 b)
        {
            return new PaVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        public static PaVec3 Sub(PaVec3 a, PaVec3 b) { return new PaVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }

        public static float Length(PaVec3 v) { return (float)Math.Sqrt(Dot(v, v)); }

        public static float Distance(PaVec3 a, PaVec3 b) { return Length(Sub(a, b)); }

        static PaVec3 Normalize(PaVec3 v, PaVec3 fallback)
        {
            float len = Length(v);
            if (len < 1e-6f || float.IsNaN(len) || float.IsInfinity(len)) return fallback;
            return new PaVec3(v.X / len, v.Y / len, v.Z / len);
        }

        /// <summary>Distance on the attenuation curves. Wwise's scaling factor stretches the
        /// curves, so the lookup distance is divided by it. Non-positive or invalid factors count as 1.</summary>
        public static float ScaledDistance(float distance, float scaling)
        {
            if (!(scaling > 0f) || float.IsInfinity(scaling)) scaling = 1f;
            return distance / scaling;
        }

        /// <summary>Find the first curve with the given use. Null when absent.</summary>
        public static PaAttenuationCurve FindCurve(PaAttenuation att, string use)
        {
            if (att == null || att.Curves == null) return null;
            foreach (var c in att.Curves)
                if (c != null && c.Points != null && c.Points.Length > 0 && string.Equals(c.Use, use, StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        /// <summary>Evaluate a curve of the given use at x; 0 when the attenuation has no such curve.</summary>
        public static float EvalCurve(PaAttenuation att, string use, float x)
        {
            var c = FindCurve(att, use);
            return c == null ? 0f : PaCurves.Evaluate(c.Points, x);
        }

        /// <summary>Azimuth of the emitter in listener space, radians in (-pi, pi]. Right is
        /// cross(top, front), which is +X for the Unity identity pose (front +Z, top +Y).
        /// An emitter on the listener gives 0.</summary>
        public static float Azimuth(PaListenerPose listener, PaVec3 emitter)
        {
            PaVec3 front = Normalize(listener.Front, DefaultFront);
            PaVec3 top = Normalize(listener.Top, DefaultTop);
            PaVec3 right = Normalize(Cross(top, front), new PaVec3(1f, 0f, 0f));
            PaVec3 rel = Sub(emitter, listener.Position);
            float x = Dot(rel, right), z = Dot(rel, front);
            if (Math.Abs(x) < 1e-6f && Math.Abs(z) < 1e-6f) return 0f;
            return (float)Math.Atan2(x, z);
        }

        /// <summary>Stereo pan from azimuth and spread: sin(azimuth) * (1 - spread/100), in -1..1.</summary>
        public static float Pan(float azimuth, float spread)
        {
            float s = spread < 0f ? 0f : (spread > 100f ? 100f : spread);
            float p = (float)Math.Sin(azimuth) * (1f - s / 100f);
            return p < -1f ? -1f : (p > 1f ? 1f : p);
        }

        /// <summary>Full 3D evaluation for an emitter. A null attenuation gives no distance terms.</summary>
        public static PaSpatialResult Evaluate(PaListenerPose listener, PaVec3 emitter, float scaling, PaAttenuation att)
        {
            var r = new PaSpatialResult();
            r.Distance = Distance(emitter, listener.Position);
            r.CurveX = ScaledDistance(r.Distance, scaling);
            r.VolumeDb = EvalCurve(att, UseVolumeDry, r.CurveX);
            r.Lpf = EvalCurve(att, UseLpf, r.CurveX);
            r.Spread = EvalCurve(att, UseSpread, r.CurveX);
            r.Azimuth = Azimuth(listener, emitter);
            r.Pan = Pan(r.Azimuth, r.Spread);
            return r;
        }
    }
}
