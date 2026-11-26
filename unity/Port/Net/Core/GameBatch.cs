// Parser for the game's batch payload (protocol.md 1.5):
//   start 84 12 40 EE, then 1..N segments (segLen int32 LE, segTransferID int32 LE, data),
//   then end 32 90 as the last two bytes.
// Strict bounds: every segment must fit, the end marker must close the payload exactly, and
// each step advances by at least 9 bytes, so malformed input can never loop.

using System.Collections.Generic;

namespace PortNet
{
    public struct BatchSegment
    {
        public int TransferId;
        public int Offset;
        public int Length;

        public BatchSegment(int transferId, int offset, int length)
        {
            TransferId = transferId;
            Offset = offset;
            Length = length;
        }
    }

    public static class GameBatch
    {
        public static readonly byte[] StartMarker = { 0x84, 0x12, 0x40, 0xEE };
        public static readonly byte[] EndMarker = { 0x32, 0x90 };

        public static bool IsBatch(byte[] data, int size)
        {
            if (data == null || size < StartMarker.Length || size > data.Length)
                return false;
            for (int i = 0; i < StartMarker.Length; i++)
                if (data[i] != StartMarker[i])
                    return false;
            return true;
        }

        // Splits a batch into segments (offsets into data). False, with segments cleared, when
        // the payload is not a well-formed batch with at least one segment.
        public static bool TrySplit(byte[] data, int size, List<BatchSegment> segments)
        {
            segments.Clear();
            if (!IsBatch(data, size))
                return false;
            int p = StartMarker.Length;
            while (true)
            {
                int rem = size - p;
                if (rem == EndMarker.Length && data[p] == EndMarker[0] && data[p + 1] == EndMarker[1])
                {
                    if (segments.Count > 0)
                        return true;
                    break;
                }
                if (rem < 8 + 1 + EndMarker.Length)
                    break;
                int len = ReadInt32(data, p);
                int id = ReadInt32(data, p + 4);
                p += 8;
                if (len < 1 || len > size - p - EndMarker.Length)
                    break;
                segments.Add(new BatchSegment(id, p, len));
                p += len;
            }
            segments.Clear();
            return false;
        }

        public static int ReadInt32(byte[] b, int p)
        {
            return b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
        }

        public static void WriteInt32(byte[] b, int p, int v)
        {
            b[p] = (byte)v;
            b[p + 1] = (byte)(v >> 8);
            b[p + 2] = (byte)(v >> 16);
            b[p + 3] = (byte)(v >> 24);
        }
    }
}
