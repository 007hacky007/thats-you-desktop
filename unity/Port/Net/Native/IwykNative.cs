// P/Invoke declarations for the iwyknet C ABI (native/iwyknet/include/iwyknet.h, contract 3.2).
// Strings are passed as NUL-terminated UTF-8 byte arrays (no LPUTF8Str), outputs go into
// caller-owned arrays, nothing is ever freed across the boundary. The constants mirror every
// #define IWYK_* of the header; tests/test_port_net_cs.py checks they stay equal.

using System.Runtime.InteropServices;
using System.Text;

namespace PortNet
{
    [StructLayout(LayoutKind.Sequential)]
    public struct IwykEvent
    {
        public int Kind;
        public int Session;
        public int TransferId;
        public int Code;
        public int Len;
    }

    public static class IwykNative
    {
        public const string Lib = "iwyknet";

        public const int IWYK_ABI_VERSION = 1;

        public const int IWYK_OK = 0;
        public const int IWYK_E_NOT_STARTED = -1;
        public const int IWYK_E_ALREADY_STARTED = -2;
        public const int IWYK_E_ARG = -3;
        public const int IWYK_E_NO_SESSION = -4;
        public const int IWYK_E_SMALLBUF = -5;
        public const int IWYK_E_TOO_BIG = -6;
        public const int IWYK_E_BIND = -7;
        public const int IWYK_E_CERT = -8;
        public const int IWYK_E_NO_IP = -9;
        public const int IWYK_E_FULL = -10;
        public const int IWYK_E_INTERNAL = -11;

        public const int IWYK_EV_OPENED = 1;
        public const int IWYK_EV_TEXT = 2;
        public const int IWYK_EV_BINARY = 3;
        public const int IWYK_EV_CLOSED = 4;
        public const int IWYK_EV_LOG = 5;

        public const int IWYK_CLOSE_HOST = 1;
        public const int IWYK_CLOSE_REMOTE = 2;
        public const int IWYK_CLOSE_TRANSPORT = 3;
        public const int IWYK_CLOSE_PROTOCOL = 4;
        public const int IWYK_CLOSE_STOPPED = 5;

        public const int IWYK_LOG_DEBUG = 0;
        public const int IWYK_LOG_INFO = 1;
        public const int IWYK_LOG_WARN = 2;
        public const int IWYK_LOG_ERROR = 3;
        public const int IWYK_LOG_OFF = 4;

        public const int IWYK_MAX_TEXT = 65536;
        public const int IWYK_MAX_BINARY = 131072;
        public const int IWYK_CHUNK = 16384;
        public const int IWYK_MAX_SESSIONS = 16;
        public const int IWYK_QR_MAX_MODULES = 31329;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_abi_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_version([Out] byte[] outBuf, int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_set_log_level(int level);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_start(int udpPort, byte[] lanIp, byte[] certPath);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_stop();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_endpoint([Out] byte[] outBuf, int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_join_url(byte[] baseUrl, [Out] byte[] outBuf, int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_qr(byte[] text, [Out] byte[] modules, int cap, out int side);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_poll(out IwykEvent ev, [Out] byte[] buf, int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_send_text(int session, byte[] utf8, int len);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_send_binary(int session, int transferId, byte[] data, int len);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_close(int session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_buffered(int session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int iwyk_last_error([Out] byte[] outBuf, int cap);

        // NUL-terminated UTF-8, or null for a null string.
        public static byte[] Utf8z(string s)
        {
            if (s == null)
                return null;
            int n = Encoding.UTF8.GetByteCount(s);
            var b = new byte[n + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, b, 0);
            return b;
        }
    }
}
