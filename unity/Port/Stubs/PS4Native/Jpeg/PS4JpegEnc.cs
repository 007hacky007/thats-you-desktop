// Host stand-in for the PS4 hardware JPEG encoder plugin ("PS4JpegEnc").
//
// The PS4 build encoded player thumbnails through libSceJpegEnc. There is no
// such library on the host, so this class keeps the public surface the game
// uses (enums, parameter structs and static entry points) and reports that
// encoding is unavailable. JpegEncode then takes its normal error path.
//
// Module load, create and delete succeed so the caller's setup and cleanup
// run as usual; Encode fails with a negative SCE-style error code. A real
// managed codec belongs to the phone-data plan.
//
// These methods are called from JpegEncode's worker thread, so they must not
// touch the Unity API.

using System;
using System.Runtime.InteropServices;

public class PS4JpegEnc
{
    public enum SceJpegEncEncodeMode
    {
        SCE_JPEG_ENC_ENCODE_MODE_NORMAL = 0,
        SCE_JPEG_ENC_ENCODE_MODE_MJPEG = 1
    }

    public enum SceJpegEncColorSpace
    {
        SCE_JPEG_ENC_COLOR_SPACE_YCC = 1,
        SCE_JPEG_ENC_COLOR_SPACE_GRAYSCALE = 2
    }

    public enum SceJpegEncSamplingType
    {
        SCE_JPEG_ENC_SAMPLING_TYPE_FULL = 0,
        SCE_JPEG_ENC_SAMPLING_TYPE_422 = 1,
        SCE_JPEG_ENC_SAMPLING_TYPE_420 = 2
    }

    public enum SceJpegEncPixelFormat
    {
        SCE_JPEG_ENC_PIXEL_FORMAT_R8G8B8A8 = 0,
        SCE_JPEG_ENC_PIXEL_FORMAT_B8G8R8A8 = 1,
        SCE_JPEG_ENC_PIXEL_FORMAT_Y8U8Y8V8 = 10,
        SCE_JPEG_ENC_PIXEL_FORMAT_Y8 = 11
    }

    // Field order and types are kept so Marshal.SizeOf / StructureToPtr on
    // these structs behave as the caller expects.
    public struct SceJpegEncCreateParam
    {
        public uint thisSize;
        public uint attribute;
    }

    public struct SceJpegEncEncodeParam
    {
        public IntPtr imageMemAddr;
        public IntPtr jpegMemAddr;
        public uint imageMemSize;
        public uint jpegMemSize;
        public uint imageWidth;
        public uint imageHeight;
        public uint imagePitch;
        public ushort pixelFormat;
        public ushort encodeMode;
        public ushort colorSpace;
        public byte samplingType;
        public byte compressionRatio;
        public int restartInterval;
    }

    public struct SceJpegEncOutputInfo
    {
        public uint dataSize;
        public uint processedHeight;
    }

    // Negative result returned by Encode: the encoder is not available on
    // this platform. Any negative value sends JpegEncode down its error path.
    private const int ErrorNotAvailable = unchecked((int)0x80650001);

    // Size the caller allocates as encoder work memory. Kept small; the
    // stand-in never touches it.
    private const int WorkMemorySize = 64;

    // Placeholder handle so the caller sees a non-null handle and calls
    // Delete during its cleanup.
    private static readonly IntPtr DummyHandle = new IntPtr(1);

    public static int LoadModule()
    {
        return 0;
    }

    public static int UnloadModule()
    {
        return 0;
    }

    public static int QueryMemorySize()
    {
        return WorkMemorySize;
    }

    public static int Create(IntPtr memoryAddress, uint memorySize, ref IntPtr handle)
    {
        handle = DummyHandle;
        return 0;
    }

    public static int Delete(IntPtr handle)
    {
        return 0;
    }

    public static int Encode(IntPtr handle, IntPtr param, IntPtr outputInfo, byte[] imageMemAddr, byte[] jpegMemAddr, uint imageMemSize, uint jpegMemSize)
    {
        // Leave a defined (empty) result behind for anyone reading it.
        if (outputInfo != IntPtr.Zero)
        {
            Marshal.StructureToPtr(default(SceJpegEncOutputInfo), outputInfo, false);
        }
        return ErrorNotAvailable;
    }
}
