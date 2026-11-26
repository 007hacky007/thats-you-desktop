// Host stand-in for the PS4 hardware JPEG decoder plugin ("PS4JpegDec").
//
// The PS4 build decoded phone portraits and thumbnails through
// libSceJpegDec. There is no such library on the host, so this class keeps
// the public surface the game uses (parameter structs, enums and static
// entry points) and reports that decoding is unavailable. JpegDecode then
// takes its normal error path.
//
// Module load, memory query, create and delete succeed; ParseHeader and
// Decode fail with a negative SCE-style error code and leave a zeroed image
// info behind. A real managed codec belongs to the phone-data plan.
//
// These methods are called from JpegDecode's worker thread, so they must not
// touch the Unity API.

using System;
using System.Runtime.InteropServices;

public class PS4JpegDec
{
    // Field order and types are kept so Marshal.SizeOf / StructureToPtr /
    // PtrToStructure on these structs behave as the caller expects.
    public struct SceJpegDecCreateParam
    {
        public uint thisSize;
        public uint attribute;
        public uint maxImageWidth;
    }

    public struct SceJpegDecDecodeParam
    {
        public IntPtr jpegMemAddr;
        public IntPtr imageMemAddr;
        public IntPtr coefficientMemAddr;
        public uint jpegMemSize;
        public uint imageMemSize;
        public uint coefficientMemSize;
        public ushort decodeMode;
        public ushort downScale;
        public ushort pixelFormat;
        public ushort alphaValue;
        public uint imagePitch;
    }

    public struct SceJpegDecImageInfo
    {
        public uint imageWidth;
        public uint imageHeight;
        public ushort colorSpace;
        public ushort componentNumber;
        public byte componentId_0;
        public byte componentId_1;
        public byte componentId_2;
        public byte componentId_3;
        public byte samplingFactor_0;
        public byte samplingFactor_1;
        public byte samplingFactor_2;
        public byte samplingFactor_3;
        public uint coefficientMemSize;
        public uint suitableCscAttribute;
        public uint outputImageWidth;
        public uint outputImageHeight;
    }

    public struct SceJpegDecParseParam
    {
        public IntPtr jpegMemAddr;
        public uint jpegMemSize;
        public ushort decodeMode;
        public ushort downScale;
    }

    public enum SceJpegDecAttribute
    {
        SCE_JPEG_DEC_ATTRIBUTE_CSC_STD_SAMPLING = 1,
        SCE_JPEG_DEC_ATTRIBUTE_CSC_FULL_SAMPLING = 2
    }

    public enum SceJpegDecDecodeMode
    {
        SCE_JPEG_DEC_DECODE_MODE_NORMAL = 0,
        SCE_JPEG_DEC_DECODE_MODE_MJPEG = 1
    }

    public enum SceJpegDecColorSpace
    {
        SCE_JPEG_DEC_COLOR_SPACE_UNKNOWN = 0,
        SCE_JPEG_DEC_COLOR_SPACE_YCC = 1,
        SCE_JPEG_DEC_COLOR_SPACE_GRAYSCALE = 2
    }

    public enum SceJpegDecPixelFormat
    {
        SCE_JPEG_DEC_PIXEL_FORMAT_R8G8B8A8 = 0,
        SCE_JPEG_DEC_PIXEL_FORMAT_B8G8R8A8 = 1,
        SCE_JPEG_DEC_PIXEL_FORMAT_Y8U8Y8V8 = 10,
        SCE_JPEG_DEC_PIXEL_FORMAT_Y8 = 11
    }

    // Negative result returned by ParseHeader and Decode: the decoder is not
    // available on this platform. Any negative value sends JpegDecode down its
    // error path.
    private const int ErrorNotAvailable = unchecked((int)0x80650101);

    // Size the caller allocates as decoder work memory. Kept small; the
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

    public static int QueryMemorySize(IntPtr param)
    {
        return WorkMemorySize;
    }

    public static int Create(IntPtr param, IntPtr memoryAddress, uint memorySize, ref IntPtr handle)
    {
        handle = DummyHandle;
        return 0;
    }

    public static int Delete(IntPtr handle)
    {
        return 0;
    }

    public static int ParseHeader(IntPtr param, IntPtr imageInfo, byte[] jpegMemAddr, uint jpegMemSize)
    {
        // The caller reads the image info back even when parsing fails, so
        // hand it zeros rather than uninitialised memory.
        ClearImageInfo(imageInfo);
        return ErrorNotAvailable;
    }

    public static int Decode(IntPtr handle, IntPtr param, IntPtr imageInfo, byte[] jpegMemAddr, uint jpegMemSize, byte[] imageMemAddr, uint imageMemSize)
    {
        ClearImageInfo(imageInfo);
        return ErrorNotAvailable;
    }

    private static void ClearImageInfo(IntPtr imageInfo)
    {
        if (imageInfo != IntPtr.Zero)
        {
            Marshal.StructureToPtr(default(SceJpegDecImageInfo), imageInfo, false);
        }
    }
}
