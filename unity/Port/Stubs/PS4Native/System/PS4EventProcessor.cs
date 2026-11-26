// Host stand-in for the PS4 system-event plugin ("PS4EventProcessor").
//
// On PS4 a native plugin queued system-service events (resume, invitations,
// share menu and so on) for the game to drain. A desktop host raises none of
// them, so the queue is always empty. The component keeps its scene GUID and
// the public names and signatures of the original API.

using System;
using UnityEngine;

public class PS4EventProcessor : MonoBehaviour
{
    public enum SceSystemServiceEventType
    {
        SCE_SYSTEM_SERVICE_EVENT_INVALID = -1,
        SCE_SYSTEM_SERVICE_EVENT_ON_RESUME = 0x10000000,
        SCE_SYSTEM_SERVICE_EVENT_GAME_LIVE_STREAMING_STATUS_UPDATE = 0x10000001,
        SCE_SYSTEM_SERVICE_EVENT_SESSION_INVITATION = 0x10000002,
        SCE_SYSTEM_SERVICE_EVENT_ENTITLEMENT_UPDATE = 0x10000003,
        SCE_SYSTEM_SERVICE_EVENT_GAME_CUSTOM_DATA = 0x10000004,
        SCE_SYSTEM_SERVICE_EVENT_DISPLAY_SAFE_AREA_UPDATE = 0x10000005,
        SCE_SYSTEM_SERVICE_EVENT_URL_OPEN = 0x10000006,
        SCE_SYSTEM_SERVICE_EVENT_LAUNCH_APP = 0x10000007,
        SCE_SYSTEM_SERVICE_EVENT_APP_LAUNCH_LINK = 0x10000008,
        SCE_SYSTEM_SERVICE_EVENT_ADDCONTENT_INSTALL = 0x10000009,
        SCE_SYSTEM_SERVICE_EVENT_OPEN_SHARE_MENU = 0x30000000
    }

    // Event record size: a 4-byte type followed by an 8 KB payload.
    private const int EventRecordSize = 4 + 8192;

    // Returned by a pop on an empty queue.
    private const int ErrorNoEvent = -1;

    public static byte[] m_ManagedMemoryPool;

    public static SceSystemServiceEventType m_PS4SystemEventType = SceSystemServiceEventType.SCE_SYSTEM_SERVICE_EVENT_INVALID;

    // No host events: nothing is written and the type stays invalid.
    public static int PopEventFromStack(byte[] data)
    {
        if (data != null)
        {
            int n = Math.Min(4, data.Length);
            for (int i = 0; i < n; i++)
            {
                data[i] = 0xFF;
            }
        }
        return ErrorNoEvent;
    }

    public static int PopEventFromStack()
    {
        if (m_ManagedMemoryPool == null)
        {
            m_ManagedMemoryPool = new byte[GetSizeOfType()];
        }
        int result = PopEventFromStack(m_ManagedMemoryPool);
        m_PS4SystemEventType = SceSystemServiceEventType.SCE_SYSTEM_SERVICE_EVENT_INVALID;
        return result;
    }

    public static int NumberOfEvents(ref int numOfEvents)
    {
        numOfEvents = 0;
        return 0;
    }

    public static int GetSizeOfType()
    {
        return EventRecordSize;
    }

    private void Start()
    {
        m_ManagedMemoryPool = new byte[GetSizeOfType()];
    }
}
