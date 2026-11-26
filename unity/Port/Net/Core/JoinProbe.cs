// The in-memory join datagram used to run the game's own join policy (contract 4.4), and the
// decoding of the game's raw replies to it. Layouts are the original wire format
// (protocol.md 3.1): request = join magic, nonce byte, uidLen int32 LE, UID UTF-8;
// response = response magic + nonce; refusal = refusal magic + reason.

using System.Text;

namespace PortNet
{
    public static class JoinProbe
    {
        public static readonly byte[] JoinMagic = { 0xC1, 0x7C, 0xDC, 0x01, 0x43, 0xCF, 0x93 };
        public static readonly byte[] ResponseMagic = { 0x6A, 0xE4, 0x41, 0x2E, 0xB9, 0x41, 0xC1 };
        public static readonly byte[] RefuseMagic = { 0x98, 0xAB, 0x64, 0x59, 0x44, 0x4E, 0x2D };

        public const byte Nonce = 0;
        public const int HeaderSize = 12;   // magic 7 + nonce 1 + uidLen 4

        public static byte[] BuildRequest(string uid)
        {
            byte[] u = Encoding.UTF8.GetBytes(uid ?? "");
            var d = new byte[HeaderSize + u.Length];
            System.Buffer.BlockCopy(JoinMagic, 0, d, 0, JoinMagic.Length);
            d[7] = Nonce;
            GameBatch.WriteInt32(d, 8, u.Length);
            System.Buffer.BlockCopy(u, 0, d, HeaderSize, u.Length);
            return d;
        }

        // Joined for a response, Refused(reason) for a refusal; false for anything else.
        public static bool TryDecodeReply(byte[] data, int size, out JoinDecision decision)
        {
            decision = JoinDecision.Pending;
            if (data == null || size != 8 || data.Length < 8)
                return false;
            if (StartsWith(data, ResponseMagic))
            {
                decision = JoinDecision.Joined;
                return true;
            }
            if (StartsWith(data, RefuseMagic))
            {
                decision = JoinDecision.Refused(data[7]);
                return true;
            }
            return false;
        }

        static bool StartsWith(byte[] d, byte[] magic)
        {
            for (int i = 0; i < magic.Length; i++)
                if (d[i] != magic[i])
                    return false;
            return true;
        }
    }
}
