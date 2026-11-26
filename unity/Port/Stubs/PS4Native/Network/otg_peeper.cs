// Managed stand-in for the otg-peeper native plugin, a UPnP probe used only
// to report the router model and manufacturer in telemetry.
//
// Names, signatures and enum values follow the PS4 binding. No UPnP handle is
// ever created, so the probe job exits at once and telemetry sees a failure
// result code.

using System;
using System.Text;

namespace Wish.Core
{
    public class otg_peeper
    {
        public enum ReturnCode
        {
            kReturn_Success = 0,
            kReturn_NotInitialized = 1,
            kReturn_AlreadyInitialized = 2,
            kReturn_InitializeFailed = 3,
            kReturn_InvalidParameter = 4,
            kReturn_OutOfMemory = 5,
            kReturn_Error = 6
        }

        public enum LogLevel
        {
            kLogTrace = 0,
            kLogDebug = 1,
            kLogInfo = 2,
            kLogWarn = 3,
            kLogError = 4,
            kLogFatal = 5,
            kLogOff = 6
        }

        public static IntPtr CreateUPnP()
        {
            return IntPtr.Zero;
        }

        public static ReturnCode UPnPInitialize(IntPtr upnp, LogLevel logLevel)
        {
            return ReturnCode.kReturn_InitializeFailed;
        }

        public static ReturnCode UPnPUpdate(IntPtr upnp)
        {
            return ReturnCode.kReturn_NotInitialized;
        }

        public static bool UPnPIsReady(IntPtr upnp)
        {
            return false;
        }

        public static ReturnCode UPnPGetRouterModel(IntPtr upnp, StringBuilder pModel, uint bufferSize, ref uint pModelSize)
        {
            pModelSize = 0u;
            return ReturnCode.kReturn_NotInitialized;
        }

        public static ReturnCode UPnPGetRouterManufacturer(IntPtr upnp, StringBuilder pManufacturer, uint bufferSize, ref uint pManufacturerSize)
        {
            pManufacturerSize = 0u;
            return ReturnCode.kReturn_NotInitialized;
        }

        public static void DestroyUPnP(IntPtr upnp)
        {
        }
    }
}
