// INetTransport over the iwyknet native library. One host thread (the Unity main thread) calls
// it. The library is loaded lazily on the first call; a missing library or entry point turns
// into IWYK_E_INTERNAL with LastError "iwyknet native library not found".

using System;
using System.Text;

namespace PortNet
{
    public sealed class NativeTransport : INetTransport
    {
        public const string NotFound = "iwyknet native library not found";
        const int PollBufferSize = IwykNative.IWYK_MAX_BINARY;   // always enough per the ABI

        byte[] m_pollBuf = new byte[PollBufferSize];
        readonly byte[] m_strBuf = new byte[4096];
        bool m_unavailable;
        bool m_started;
        string m_lastError;

        public string LastError => m_lastError;

        public bool Available
        {
            get
            {
                if (m_unavailable)
                    return false;
                try
                {
                    return IwykNative.iwyk_abi_version() == IwykNative.IWYK_ABI_VERSION;
                }
                catch (Exception e) when (IsMissing(e))
                {
                    MarkMissing();
                    return false;
                }
            }
        }

        static bool IsMissing(Exception e)
        {
            return e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException;
        }

        void MarkMissing()
        {
            m_unavailable = true;
            m_started = false;
            m_lastError = NotFound;
        }

        public int Start(int udpPort, string lanIp, string certPath)
        {
            if (m_unavailable)
                return IwykNative.IWYK_E_INTERNAL;
            try
            {
                int abi = IwykNative.iwyk_abi_version();
                if (abi != IwykNative.IWYK_ABI_VERSION)
                {
                    m_lastError = "iwyknet ABI " + abi + " does not match the expected " + IwykNative.IWYK_ABI_VERSION;
                    return IwykNative.IWYK_E_INTERNAL;
                }
                int rc = IwykNative.iwyk_start(udpPort, IwykNative.Utf8z(lanIp ?? ""), IwykNative.Utf8z(certPath ?? ""));
                m_started = rc == IwykNative.IWYK_OK;
                if (!m_started)
                    m_lastError = ReadLastError() ?? ("iwyk_start failed (" + rc + ")");
                return rc;
            }
            catch (Exception e) when (IsMissing(e))
            {
                MarkMissing();
                return IwykNative.IWYK_E_INTERNAL;
            }
        }

        public void Stop()
        {
            if (m_unavailable)
                return;
            try
            {
                IwykNative.iwyk_stop();
            }
            catch (Exception e) when (IsMissing(e))
            {
                MarkMissing();
            }
            m_started = false;
        }

        public void SetLogLevel(int level)
        {
            if (m_unavailable)
                return;
            try
            {
                IwykNative.iwyk_set_log_level(level);
            }
            catch (Exception e) when (IsMissing(e))
            {
                MarkMissing();
            }
        }

        public bool Poll(out NetEvent ev)
        {
            ev = default;
            if (!m_started)
                return false;
            int rc = IwykNative.iwyk_poll(out IwykEvent e, m_pollBuf, m_pollBuf.Length);
            if (rc == IwykNative.IWYK_E_SMALLBUF && e.Len > m_pollBuf.Length)
            {
                m_pollBuf = new byte[e.Len];
                rc = IwykNative.iwyk_poll(out e, m_pollBuf, m_pollBuf.Length);
            }
            if (rc != 1)
                return false;
            int len = Math.Max(0, Math.Min(e.Len, m_pollBuf.Length));
            var data = new byte[len];
            Buffer.BlockCopy(m_pollBuf, 0, data, 0, len);
            ev = new NetEvent { Kind = (NetEventKind)e.Kind, Session = e.Session, TransferId = e.TransferId, Code = e.Code, Data = data };
            return true;
        }

        public int SendText(int session, byte[] utf8, int len)
        {
            return m_started ? IwykNative.iwyk_send_text(session, utf8, len) : IwykNative.IWYK_E_NOT_STARTED;
        }

        public int SendBinary(int session, int transferId, byte[] data, int len)
        {
            return m_started ? IwykNative.iwyk_send_binary(session, transferId, data, len) : IwykNative.IWYK_E_NOT_STARTED;
        }

        public int Close(int session)
        {
            return m_started ? IwykNative.iwyk_close(session) : IwykNative.IWYK_E_NOT_STARTED;
        }

        public int Buffered(int session)
        {
            return m_started ? IwykNative.iwyk_buffered(session) : IwykNative.IWYK_E_NOT_STARTED;
        }

        public string Endpoint
        {
            get
            {
                if (!m_started)
                    return null;
                int n = IwykNative.iwyk_endpoint(m_strBuf, m_strBuf.Length);
                return n > 0 ? Encoding.UTF8.GetString(m_strBuf, 0, n) : null;
            }
        }

        public string JoinUrl(string baseUrl)
        {
            if (!m_started || baseUrl == null)
                return null;
            int n = IwykNative.iwyk_join_url(IwykNative.Utf8z(baseUrl), m_strBuf, m_strBuf.Length);
            if (n <= 0)
            {
                m_lastError = ReadLastError();
                return null;
            }
            return Encoding.UTF8.GetString(m_strBuf, 0, n);
        }

        public bool Qr(string text, out byte[] modules, out int side)
        {
            modules = null;
            side = 0;
            if (m_unavailable || text == null)
                return false;
            try
            {
                var buf = new byte[IwykNative.IWYK_QR_MAX_MODULES];
                int rc = IwykNative.iwyk_qr(IwykNative.Utf8z(text), buf, buf.Length, out side);
                if (rc < 0 || side <= 0 || side * side > buf.Length)
                {
                    side = 0;
                    return false;
                }
                modules = new byte[side * side];
                Buffer.BlockCopy(buf, 0, modules, 0, modules.Length);
                return true;
            }
            catch (Exception e) when (IsMissing(e))
            {
                MarkMissing();
                return false;
            }
        }

        public string Version
        {
            get
            {
                if (m_unavailable)
                    return null;
                try
                {
                    int n = IwykNative.iwyk_version(m_strBuf, m_strBuf.Length);
                    return n > 0 ? Encoding.UTF8.GetString(m_strBuf, 0, n) : null;
                }
                catch (Exception e) when (IsMissing(e))
                {
                    MarkMissing();
                    return null;
                }
            }
        }

        string ReadLastError()
        {
            int n = IwykNative.iwyk_last_error(m_strBuf, m_strBuf.Length);
            return n > 0 ? Encoding.UTF8.GetString(m_strBuf, 0, n) : null;
        }
    }
}
