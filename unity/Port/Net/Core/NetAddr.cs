// Synthetic per-session address. The game identifies peers by (IPv4 as uint, port offset); every
// WebRTC session gets a fresh 198.18.x.y (benchmark range, never a real LAN peer) with port
// offset 0. The uint holds the four address bytes read little-endian, as the game stores them.

using System;

namespace PortNet
{
    public struct NetAddr : IEquatable<NetAddr>
    {
        public uint Ip;
        public int PortOffset;

        public NetAddr(uint ip, int portOffset)
        {
            Ip = ip;
            PortOffset = portOffset;
        }

        public bool IsValid => Ip != 0 && PortOffset >= 0;

        public static uint IpFromBytes(byte a, byte b, byte c, byte d)
        {
            return (uint)(a | (b << 8) | (c << 16) | (d << 24));
        }

        public string IpString => (Ip & 0xFF) + "." + ((Ip >> 8) & 0xFF) + "." + ((Ip >> 16) & 0xFF) + "." + (Ip >> 24);

        public bool Equals(NetAddr other)
        {
            return Ip == other.Ip && PortOffset == other.PortOffset;
        }

        public override bool Equals(object obj)
        {
            return obj is NetAddr other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Ip.GetHashCode() ^ PortOffset.GetHashCode();
        }

        public static bool operator ==(NetAddr a, NetAddr b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(NetAddr a, NetAddr b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return IpString + ":+" + PortOffset;
        }
    }

    // Hands out 198.18.0.1, 198.18.0.2, ... 198.18.255.255, 198.19.0.0 ... 198.19.255.255, then
    // wraps to 198.18.0.1, skipping addresses still in use.
    public sealed class NetAddrAllocator
    {
        public const int MaxCounter = 0x1FFFF;

        int m_last;

        public NetAddrAllocator(int last = 0)
        {
            m_last = last;
        }

        public static NetAddr FromCounter(int counter)
        {
            return new NetAddr(NetAddr.IpFromBytes(198, (byte)(18 + (counter >> 16)), (byte)(counter >> 8), (byte)counter), 0);
        }

        public NetAddr Allocate(Func<NetAddr, bool> inUse)
        {
            for (int tries = 0; tries < MaxCounter; tries++)
            {
                m_last = m_last >= MaxCounter ? 1 : m_last + 1;
                NetAddr a = FromCounter(m_last);
                if (inUse == null || !inUse(a))
                    return a;
            }
            throw new InvalidOperationException("no free synthetic address");
        }
    }
}
