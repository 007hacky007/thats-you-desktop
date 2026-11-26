// Received data waiting to be claimed by the game with TryGetData(transferId, sender).
// FIFO per (address, transfer id); unclaimed entries are abandoned after 2 s of game realtime;
// capped by entry count and total bytes (the oldest entry goes first).

using System;
using System.Collections.Generic;

namespace PortNet
{
    public sealed class ReceiveStore
    {
        struct Entry
        {
            public NetAddr Addr;
            public int TransferId;
            public byte[] Data;
            public float Time;
        }

        public const int Taken = 1;
        public const int NotFound = 0;
        public const int DroppedTooSmall = -1;

        readonly List<Entry> m_entries = new List<Entry>();
        readonly int m_maxEntries;
        readonly long m_maxBytes;

        public ReceiveStore(int maxEntries = NetLimits.ReceiveStoreEntries, long maxBytes = NetLimits.ReceiveStoreBytes)
        {
            m_maxEntries = maxEntries;
            m_maxBytes = maxBytes;
        }

        public int Count => m_entries.Count;
        public long Bytes { get; private set; }

        // Stores data (the array is kept, not copied). Returns how many old entries were dropped
        // to stay within the caps.
        public int Add(NetAddr addr, int transferId, byte[] data, float time)
        {
            int dropped = 0;
            long size = data == null ? 0 : data.Length;
            while (m_entries.Count > 0 && (m_entries.Count >= m_maxEntries || Bytes + size > m_maxBytes))
            {
                RemoveAt(0);
                dropped++;
            }
            m_entries.Add(new Entry { Addr = addr, TransferId = transferId, Data = data ?? new byte[0], Time = time });
            Bytes += size;
            return dropped;
        }

        // Oldest entry for (transferId, addr). Copies it into buffer when it fits (Taken), drops
        // it when it does not (DroppedTooSmall); NotFound otherwise.
        public int TryTake(int transferId, NetAddr addr, byte[] buffer, ref int size)
        {
            for (int i = 0; i < m_entries.Count; i++)
            {
                Entry e = m_entries[i];
                if (e.TransferId != transferId || e.Addr != addr)
                    continue;
                RemoveAt(i);
                if (buffer == null || buffer.Length < e.Data.Length)
                    return DroppedTooSmall;
                Buffer.BlockCopy(e.Data, 0, buffer, 0, e.Data.Length);
                size = e.Data.Length;
                return Taken;
            }
            return NotFound;
        }

        // Removes entries stored before cutoff. Returns how many.
        public int Abandon(float cutoff)
        {
            int n = 0;
            for (int i = m_entries.Count - 1; i >= 0; i--)
            {
                if (m_entries[i].Time < cutoff)
                {
                    RemoveAt(i);
                    n++;
                }
            }
            return n;
        }

        public void Clear()
        {
            m_entries.Clear();
            Bytes = 0;
        }

        void RemoveAt(int i)
        {
            Bytes -= m_entries[i].Data.Length;
            m_entries.RemoveAt(i);
        }
    }
}
