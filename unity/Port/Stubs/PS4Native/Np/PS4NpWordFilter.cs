// Host stand-in for the PS4 "PS4NpWordFilter" native plugin (the PSN
// profanity filter for user-entered text).
//
// There is no PSN filter service on the host. Contexts and requests are
// handed out as small positive ids, and SanitizeComment returns the text
// unchanged: it copies the zero-terminated input into the output buffer.
//
// Constant and member names match the PS4 plugin.

using System;

public class PS4NpWordFilter
{
    public const int SCE_NP_WORD_FILTER_CENSOR_COMMENT_MAXLEN = 1024;

    public const int SCE_NP_WORD_FILTER_SANITIZE_COMMENT_MAXLEN = 1024;

    // Returned when the buffers are missing.
    private const int ErrorInvalidArgument = unchecked((int)0x80550003);

    private static int s_nextId;

    public static int LoadModule()
    {
        return 0;
    }

    public static int UnloadModule()
    {
        return 0;
    }

    public static int CreateTitleCtx()
    {
        return NextId();
    }

    public static int CreateRequest(int titleCtxId)
    {
        return NextId();
    }

    public static int SanitizeComment(int reqId, byte[] comment, byte[] sanitizedComment, IntPtr option)
    {
        if (comment == null || sanitizedComment == null)
        {
            return ErrorInvalidArgument;
        }
        int limit = Math.Min(comment.Length, sanitizedComment.Length);
        limit = Math.Min(limit, SCE_NP_WORD_FILTER_SANITIZE_COMMENT_MAXLEN);
        int n = 0;
        while (n < limit && comment[n] != 0)
        {
            sanitizedComment[n] = comment[n];
            n++;
        }
        // Terminate (or clear the rest) so the output is a clean C string.
        for (int i = n; i < sanitizedComment.Length; i++)
        {
            sanitizedComment[i] = 0;
        }
        return 0;
    }

    public static int DeleteRequest(int reqId)
    {
        return 0;
    }

    private static int NextId()
    {
        return System.Threading.Interlocked.Increment(ref s_nextId);
    }
}
