using System;
using System.Runtime.InteropServices;

namespace Snip;

// Minimal Media Foundation interop for writing H.264 MP4 through the sink writer.
// COM interop needs every vtable slot declared in order, so unused methods are placeholders.
static class MF
{
    public const int Version = 0x20070;

    public static readonly Guid MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid VideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid VideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public const int H264ProfileHigh = 100;
    public static readonly Guid READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public const int VideoInterlace_Progressive = 2;

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateAttributes(out IMFAttributes attributes, int initialSize);
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateSample(out IMFSample sample);
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCopyImage(IntPtr dest, int destStride, IntPtr src, int srcStride, int widthInBytes, int lines);
    [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes attributes, out IMFSinkWriter writer);

    public static long Pack(int high, int low) => ((long)high << 32) | (uint)low;
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
    void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
    void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
    void DeleteAllItems();
    void SetUINT32([In] ref Guid key, int value);
    void SetUINT64([In] ref Guid key, long value);
    void SetDouble();
    void SetGUID([In] ref Guid key, [In] ref Guid value);
    void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
    void GetItemByIndex(); void CopyAllItems();
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
    void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
    void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
    void DeleteAllItems();
    void SetUINT32([In] ref Guid key, int value);
    void SetUINT64([In] ref Guid key, long value);
    void SetDouble();
    void SetGUID([In] ref Guid key, [In] ref Guid value);
    void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
    void GetItemByIndex(); void CopyAllItems();
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
    void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
    void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
    void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID(); void SetString();
    void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex();
    void CopyAllItems();
    void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime();
    void SetSampleTime(long time);
    void GetSampleDuration();
    void SetSampleDuration(long duration);
    void GetBufferCount(); void GetBufferByIndex(); void ConvertToContiguousBuffer();
    void AddBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer
{
    void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
    void Unlock();
    void GetCurrentLength(out int length);
    void SetCurrentLength(int length);
    void GetMaxLength(out int length);
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSinkWriter
{
    void AddStream(IMFMediaType targetMediaType, out int streamIndex);
    void SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes encodingParameters);
    void BeginWriting();
    void WriteSample(int streamIndex, IMFSample sample);
    void SendStreamTick(int streamIndex, long timestamp);
    void PlaceMarker(int streamIndex, IntPtr context);
    void NotifyEndOfSegment(int streamIndex);
    void Flush(int streamIndex);
    void Finalize_();
}

// Writes top-down 32bpp BGRX frames to an H.264 MP4. Call Complete to finish the file, then
// Dispose, which never throws, so the error that ended a recording is the one reported.
sealed class Mp4Writer : IDisposable
{
    readonly IMFSinkWriter writer;
    readonly int stream;
    readonly int width, height;
    bool disposed;

    public Mp4Writer(string path, int width, int height, int fps)
    {
        this.width = width;
        this.height = height;
        MF.MFStartup(MF.Version, 0);
        IMFAttributes attributes = null;
        IMFMediaType output = null, input = null;
        try
        {
            MF.MFCreateAttributes(out attributes, 1);
            var hw = MF.READWRITE_ENABLE_HARDWARE_TRANSFORMS;
            attributes.SetUINT32(ref hw, 1);
            MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out writer);

            MF.MFCreateMediaType(out output);
            SetVideoType(output, MF.VideoFormat_H264, fps);
            var bitrate = MF.MT_AVG_BITRATE;
            output.SetUINT32(ref bitrate, Math.Max(4_000_000, width * height * fps / 8));
            var profile = MF.MT_MPEG2_PROFILE;
            output.SetUINT32(ref profile, MF.H264ProfileHigh);
            writer.AddStream(output, out stream);

            MF.MFCreateMediaType(out input);
            SetVideoType(input, MF.VideoFormat_RGB32, fps);
            var stride = MF.MT_DEFAULT_STRIDE;
            input.SetUINT32(ref stride, width * 4);
            writer.SetInputMediaType(stream, input, null);
            writer.BeginWriting();
        }
        catch
        {
            Release(writer);
            MF.MFShutdown();
            throw;
        }
        finally
        {
            Release(attributes);
            Release(output);
            Release(input);
        }
    }

    void SetVideoType(IMFMediaType type, Guid subtype, int fps)
    {
        Guid major = MF.MT_MAJOR_TYPE, video = MF.MediaType_Video, sub = MF.MT_SUBTYPE;
        Guid interlace = MF.MT_INTERLACE_MODE, size = MF.MT_FRAME_SIZE, rate = MF.MT_FRAME_RATE, aspect = MF.MT_PIXEL_ASPECT_RATIO;
        type.SetGUID(ref major, ref video);
        type.SetGUID(ref sub, ref subtype);
        type.SetUINT32(ref interlace, MF.VideoInterlace_Progressive);
        type.SetUINT64(ref size, MF.Pack(width, height));
        type.SetUINT64(ref rate, MF.Pack(fps, 1));
        type.SetUINT64(ref aspect, MF.Pack(1, 1));
    }

    // time and duration are in 100ns units.
    public void Write(IntPtr pixels, int stride, long time, long duration)
    {
        int length = width * 4 * height;
        IMFMediaBuffer buffer = null;
        IMFSample sample = null;
        try
        {
            MF.MFCreateMemoryBuffer(length, out buffer);
            buffer.Lock(out var dest, out _, out _);
            try { MF.MFCopyImage(dest, width * 4, pixels, stride, width * 4, height); }
            finally { buffer.Unlock(); }
            buffer.SetCurrentLength(length);

            MF.MFCreateSample(out sample);
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            writer.WriteSample(stream, sample);
        }
        finally
        {
            Release(sample);
            Release(buffer);
        }
    }

    public void Complete() => writer.Finalize_();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Release(writer);
        try { MF.MFShutdown(); } catch (COMException) { }
    }

    static void Release(object com)
    {
        if (com != null) Marshal.ReleaseComObject(com);
    }
}
