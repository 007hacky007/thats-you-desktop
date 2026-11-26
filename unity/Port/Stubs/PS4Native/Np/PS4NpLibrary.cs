// Host stand-in for the PS4 "PS4NpLibrary" native plugin (PSN account,
// availability, PlayStation Plus and parental control queries).
//
// The host has no PSN account. The local user is reported as signed out of
// PSN: the availability check fails with the NP "signed out" error, no
// PlayStation Plus entitlement exists, no sign-in state change is ever
// notified, and account country and language are unknown. The local user's
// online ID is a fixed generic name so code that labels the local player
// still gets a non-empty string. It is never derived from the host machine
// or its user account.
//
// Type, constant and member names match the PS4 plugin so the game compiles
// unchanged. Pointer-based overloads write plain results into the caller's
// buffer when one is given.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class PS4NpLibrary
{
    public enum SceNpState
    {
        SCE_NP_STATE_UNKNOWN = 0,
        SCE_NP_STATE_SIGNED_OUT = 1,
        SCE_NP_STATE_SIGNED_IN = 2
    }

    public struct SceNpNotifyPlusFeatureParameter
    {
        public int size;

        public int userId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
        public string padding;

        private ulong features;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string reserved;

        internal void SetFeatures(ulong value)
        {
            features = value;
        }
    }

    public struct SceNpContentRestriction
    {
        public long size;

        public byte defaultAgeRestriction;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 3)]
        public string padding;

        public int ageRestrictionCount;

        public IntPtr ageRestriction;
    }

    public struct SceNpCountryCode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 3)]
        public string data;

        public byte padding;
    }

    public struct SceNpAgeRestriction
    {
        public SceNpCountryCode countryCode;

        public byte age;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 3)]
        public string padding;
    }

    public struct SceNpParentalControlInfo
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool contentRestriction;

        [MarshalAs(UnmanagedType.I1)]
        public bool chatRestriction;

        [MarshalAs(UnmanagedType.I1)]
        public bool ugcRestriction;
    }

    public struct SceNpLanguageCode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 6)]
        public string code;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
        public string padding;
    }

    [Serializable]
    public struct CountryAgeRestriction
    {
        public string countryCode;

        public byte age;
    }

    public const int SCE_NP_PLUS_FEATURE_REALTIME_MULTIPLAY = 1;

    public const int SCE_NP_PLUS_FEATURE_ASYNC_MULTIPLAY = 2;

    public const int SCE_NP_COUNTRY_CODE_LENGTH = 2;

    public const int SCE_NP_LANGUAGE_CODE_MAX_LEN = 5;

    // NP "signed out" error (0x80550006). Returned wherever the PS4 library
    // needed a signed-in PSN account.
    private const int ErrorSignedOut = unchecked((int)0x80550006);

    // Returned when a required output buffer is missing.
    private const int ErrorInvalidArgument = unchecked((int)0x80550003);

    // Generic, fixed name for the local user. Deliberately not personal.
    internal const string LocalOnlineId = "Player";

    // ---- availability, sign-in state and Plus ----

    public static int CheckPlus(int features, ref bool authorized)
    {
        authorized = false;
        return 0;
    }

    public static int CheckNpAvailability()
    {
        return ErrorSignedOut;
    }

    public static bool GetStateChangedFlag()
    {
        return false;
    }

    public static void ClearStateChangedFlag()
    {
    }

    public static int GetLastNotifiedState()
    {
        return (int)SceNpState.SCE_NP_STATE_SIGNED_OUT;
    }

    public static int RegisterStateCallback()
    {
        return 0;
    }

    public static int CheckCallback()
    {
        return 0;
    }

    public static int InitializeSceNpNotifyPlusFeatureParameter(IntPtr param, int features)
    {
        if (param == IntPtr.Zero)
        {
            return ErrorInvalidArgument;
        }
        SceNpNotifyPlusFeatureParameter value = default(SceNpNotifyPlusFeatureParameter);
        value.size = Marshal.SizeOf(typeof(SceNpNotifyPlusFeatureParameter));
        value.SetFeatures((ulong)features);
        Marshal.StructureToPtr(value, param, false);
        return 0;
    }

    public static int NotifyPlusFeature(IntPtr param)
    {
        return 0;
    }

    public static IntPtr CreateNotifyPlusParam(int features)
    {
        // The caller owns the block and releases it with Marshal.FreeHGlobal.
        IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SceNpNotifyPlusFeatureParameter)));
        if (InitializeSceNpNotifyPlusFeatureParameter(block, features) != 0)
        {
            Marshal.FreeHGlobal(block);
            return IntPtr.Zero;
        }
        return block;
    }

    // ---- content restriction and parental control ----

    // No PSN content restriction to register: accepted as is.
    public static int SetContentRestriction(IntPtr pRestriction)
    {
        return 0;
    }

    public static int SetContentRestriction(byte defaultAge, List<CountryAgeRestriction> countryAgeRestrictions)
    {
        return 0;
    }

    public static int GetParentalControlInfo(ref byte pAge, IntPtr pInfo)
    {
        pAge = 0;
        if (pInfo != IntPtr.Zero)
        {
            Marshal.StructureToPtr(default(SceNpParentalControlInfo), pInfo, false);
        }
        return ErrorSignedOut;
    }

    public static int GetParentalControlInfo(ref byte pAge, ref SceNpParentalControlInfo info)
    {
        pAge = 0;
        info = default(SceNpParentalControlInfo);
        return ErrorSignedOut;
    }

    // ---- account details ----

    public static int GetAccountCountry(IntPtr pOnlineId, IntPtr pCountryCode)
    {
        return ErrorSignedOut;
    }

    public static int GetAccountLanguage(IntPtr pOnlineId, IntPtr pLangCode)
    {
        return ErrorSignedOut;
    }

    public static string GetAccountCountry(string psnID)
    {
        return string.Empty;
    }

    public static string GetAccountLanguage(string psnID)
    {
        return string.Empty;
    }

    public static int GetInitialUserOnlineId(IntPtr pOnlineId)
    {
        if (pOnlineId == IntPtr.Zero)
        {
            return ErrorInvalidArgument;
        }
        SceNpOnlineId id = default(SceNpOnlineId);
        id.data = LocalOnlineId;
        Marshal.StructureToPtr(id, pOnlineId, false);
        return 0;
    }

    public static string GetInitialUserOnlineId()
    {
        return LocalOnlineId;
    }
}
