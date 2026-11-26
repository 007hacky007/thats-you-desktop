using System;
using System.Collections.Generic;

namespace PortAudio
{
    /// <summary>RMS and peak of one window of the mix, in dBFS (full scale = 1.0).</summary>
    public struct PaLevel
    {
        public int Index;            // window number from 0
        public double StartSec;      // window start in stream time
        public int Frames;
        public float RmsDb, PeakDb;
    }

    /// <summary>
    /// Splits interleaved float samples into fixed windows (default one second) and reports RMS
    /// and peak per window over all channels. Silence (0) is <see cref="SilentDb"/>. Pure, not thread safe.
    /// </summary>
    public sealed class PaLevelMeter
    {
        public const float SilentDb = -200f;

        readonly int m_Channels;
        readonly int m_WindowFrames;
        readonly double m_Rate;
        double m_SumSquares;
        float m_Peak;
        int m_Frames;
        int m_PartialSamples;
        int m_Index;

        public PaLevelMeter(int sampleRate, int channels, double windowSeconds = 1.0)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException("sampleRate");
            if (channels <= 0) throw new ArgumentOutOfRangeException("channels");
            if (!(windowSeconds > 0)) throw new ArgumentOutOfRangeException("windowSeconds");
            m_Rate = sampleRate;
            m_Channels = channels;
            m_WindowFrames = Math.Max(1, (int)Math.Round(sampleRate * windowSeconds));
        }

        public int WindowFrames { get { return m_WindowFrames; } }

        public static float ToDb(double linear)
        {
            if (!(linear > 0)) return SilentDb;
            double db = 20.0 * Math.Log10(linear);
            return db < SilentDb ? SilentDb : (float)db;
        }

        /// <summary>Adds samples; every completed window is appended to <paramref name="done"/>.</summary>
        public void Feed(float[] samples, int offset, int count, List<PaLevel> done)
        {
            for (int i = 0; i < count; i++)
            {
                float v = samples[offset + i];
                if (float.IsNaN(v)) v = 0f;
                float a = v < 0 ? -v : v;
                if (a > m_Peak) m_Peak = a;
                m_SumSquares += (double)v * v;
                if (++m_PartialSamples == m_Channels)
                {
                    m_PartialSamples = 0;
                    if (++m_Frames == m_WindowFrames)
                        Emit(done);
                }
            }
        }

        /// <summary>Emits the current partial window, if it has any frames.</summary>
        public void Finish(List<PaLevel> done)
        {
            if (m_Frames > 0)
                Emit(done);
        }

        void Emit(List<PaLevel> done)
        {
            var l = new PaLevel
            {
                Index = m_Index,
                StartSec = (double)m_Index * m_WindowFrames / m_Rate,
                Frames = m_Frames,
                RmsDb = ToDb(Math.Sqrt(m_SumSquares / ((double)m_Frames * m_Channels))),
                PeakDb = ToDb(m_Peak),
            };
            if (done != null)
                done.Add(l);
            m_Index++;
            m_Frames = 0;
            m_SumSquares = 0;
            m_Peak = 0;
        }
    }
}
