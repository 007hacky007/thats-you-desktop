using System;
using System.IO;

namespace PortAudio
{
    /// <summary>
    /// PCM16 WAV writer for interleaved float samples. The RIFF and data sizes in the header are
    /// patched (and the stream flushed) after every second of audio, so a file left behind by a
    /// killed process is still a valid WAV holding at least everything up to the last whole second.
    /// Not thread safe: one writer thread owns it.
    /// </summary>
    public sealed class PaWavWriter : IDisposable
    {
        public const int HeaderBytes = 44;
        const uint MaxDataBytes = 0xFFFFFFFFu - 36u - 1u;

        readonly Stream m_Stream;
        readonly bool m_OwnsStream;
        readonly int m_Rate, m_Channels;
        readonly long m_PatchEverySamples;
        byte[] m_Buffer = new byte[8192];
        long m_DataBytes;
        long m_SamplesSincePatch;
        long m_Clipped;
        bool m_Closed;

        public int SampleRate { get { return m_Rate; } }
        public int Channels { get { return m_Channels; } }
        public long DataBytes { get { return m_DataBytes; } }
        public long Frames { get { return m_DataBytes / (2 * m_Channels); } }
        public double Seconds { get { return (double)Frames / m_Rate; } }
        /// <summary>Samples outside [-1, 1] that were clamped.</summary>
        public long Clipped { get { return m_Clipped; } }
        /// <summary>Data bytes the header currently declares.</summary>
        public long PatchedDataBytes { get; private set; }

        public PaWavWriter(Stream stream, int sampleRate, int channels, bool ownsStream = true)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (!stream.CanSeek || !stream.CanWrite) throw new ArgumentException("stream must be seekable and writable");
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException("sampleRate");
            if (channels <= 0 || channels > 32) throw new ArgumentOutOfRangeException("channels");
            m_Stream = stream;
            m_OwnsStream = ownsStream;
            m_Rate = sampleRate;
            m_Channels = channels;
            m_PatchEverySamples = (long)sampleRate * channels;
            m_Stream.SetLength(0);
            WriteHeader(0);
            m_Stream.Flush();
        }

        /// <summary>Creates the file (and its directory); other processes may read it while it is written.</summary>
        public static PaWavWriter Create(string path, int sampleRate, int channels)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            return new PaWavWriter(new FileStream(full, FileMode.Create, FileAccess.ReadWrite, FileShare.Read), sampleRate, channels);
        }

        /// <summary>Appends interleaved samples; count should be a multiple of Channels.</summary>
        public void Write(float[] samples, int offset, int count)
        {
            if (m_Closed) throw new ObjectDisposedException("PaWavWriter");
            if (samples == null) throw new ArgumentNullException("samples");
            if (offset < 0 || count < 0 || offset + count > samples.Length) throw new ArgumentOutOfRangeException("count");
            while (count > 0)
            {
                long room = MaxDataBytes - m_DataBytes;
                if (room < 2)
                    return;                                           // 4 GB WAV limit reached: drop the rest
                long untilPatch = m_PatchEverySamples - m_SamplesSincePatch;
                int n = (int)Math.Min(Math.Min(count, m_Buffer.Length / 2), Math.Min(untilPatch, room / 2));
                for (int i = 0; i < n; i++)
                {
                    float f = samples[offset + i];
                    int v;
                    if (float.IsNaN(f)) v = 0;
                    else if (f >= 1f) { v = 32767; if (f > 1f) m_Clipped++; }
                    else if (f <= -1f) { v = -32768; if (f < -1f) m_Clipped++; }
                    else v = (int)Math.Round(f * 32767f);
                    m_Buffer[2 * i] = (byte)v;
                    m_Buffer[2 * i + 1] = (byte)(v >> 8);
                }
                m_Stream.Write(m_Buffer, 0, 2 * n);
                m_DataBytes += 2 * n;
                m_SamplesSincePatch += n;
                offset += n;
                count -= n;
                if (m_SamplesSincePatch >= m_PatchEverySamples)
                {
                    m_SamplesSincePatch = 0;
                    PatchHeader();
                }
            }
        }

        /// <summary>Writes the current sizes into the header and flushes.</summary>
        public void PatchHeader()
        {
            if (m_Closed) return;
            // the declared size covers whole frames only
            long declared = m_DataBytes - m_DataBytes % (2 * m_Channels);
            long end = m_Stream.Position;
            m_Stream.Position = 4;
            WriteUInt((uint)(36 + declared));
            m_Stream.Position = 40;
            WriteUInt((uint)declared);
            m_Stream.Position = end;
            m_Stream.Flush();
            PatchedDataBytes = declared;
        }

        /// <summary>Final header patch; drops a trailing partial frame. Safe to call twice.</summary>
        public void Close()
        {
            if (m_Closed) return;
            long partial = m_DataBytes % (2 * m_Channels);
            if (partial != 0)
            {
                m_DataBytes -= partial;
                m_Stream.SetLength(HeaderBytes + m_DataBytes);
                m_Stream.Position = HeaderBytes + m_DataBytes;
            }
            PatchHeader();
            m_Closed = true;
            if (m_OwnsStream)
                m_Stream.Dispose();
        }

        public void Dispose()
        {
            Close();
        }

        void WriteHeader(uint dataBytes)
        {
            int blockAlign = 2 * m_Channels;
            WriteAscii("RIFF");
            WriteUInt(36 + dataBytes);
            WriteAscii("WAVE");
            WriteAscii("fmt ");
            WriteUInt(16);
            WriteUShort(1);                                           // PCM
            WriteUShort((ushort)m_Channels);
            WriteUInt((uint)m_Rate);
            WriteUInt((uint)(m_Rate * blockAlign));
            WriteUShort((ushort)blockAlign);
            WriteUShort(16);
            WriteAscii("data");
            WriteUInt(dataBytes);
        }

        void WriteAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
                m_Stream.WriteByte((byte)s[i]);
        }

        void WriteUInt(uint v)
        {
            m_Stream.WriteByte((byte)v);
            m_Stream.WriteByte((byte)(v >> 8));
            m_Stream.WriteByte((byte)(v >> 16));
            m_Stream.WriteByte((byte)(v >> 24));
        }

        void WriteUShort(ushort v)
        {
            m_Stream.WriteByte((byte)v);
            m_Stream.WriteByte((byte)(v >> 8));
        }
    }
}
