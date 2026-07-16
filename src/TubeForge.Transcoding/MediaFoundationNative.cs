using System.Runtime.InteropServices;

namespace TubeForge.Transcoding;

internal static class MediaFoundationNative
{
    internal const uint SourceReaderAllStreams = 0xfffffffe;
    internal const uint SourceReaderFirstAudioStream = 0xfffffffd;
    internal const uint SourceReaderEndOfStream = 0x2;
    internal const uint SourceReaderCurrentMediaTypeChanged = 0x20;
    internal const uint SourceReaderStreamTick = 0x100;
    internal const uint MfVersion = 0x00020070;
    internal const uint CoinitMultithreaded = 0;
    internal const int RpcEChangedMode = unchecked((int)0x80010106);

    internal static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    internal static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    internal static readonly Guid MfMtAudioChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    internal static readonly Guid MfMtAudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    internal static readonly Guid MfMtAudioAverageBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    internal static readonly Guid MfMtAudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    internal static readonly Guid MfMtAudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    internal static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfAudioFormatMp3 = new("00000055-0000-0010-8000-00aa00389b71");

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern void CoUninitialize();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(uint version, uint flags = 0);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string url,
        IMFAttributes? attributes,
        out IMFSourceReader sourceReader);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string outputUrl,
        IntPtr byteStream,
        IMFAttributes? attributes,
        out IMFSinkWriter sinkWriter);
}

[ComImport]
[Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int GetItem(ref Guid key, IntPtr value);
    [PreserveSig] int GetItemType(ref Guid key, out int type);
    [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
    [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
    [PreserveSig] int GetDouble(ref Guid key, out double value);
    [PreserveSig] int GetGUID(ref Guid key, out Guid value);
    [PreserveSig] int GetStringLength(ref Guid key, out uint length);
    [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);
    [PreserveSig] int GetAllocatedString(ref Guid key, IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
    [PreserveSig] int GetBlob(ref Guid key, IntPtr value, uint size, IntPtr blobSize);
    [PreserveSig] int GetAllocatedBlob(ref Guid key, IntPtr value, out uint size);
    [PreserveSig] int GetUnknown(ref Guid key, ref Guid interfaceId, IntPtr value);
    [PreserveSig] int SetItem(ref Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(ref Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(ref Guid key, uint value);
    [PreserveSig] int SetUINT64(ref Guid key, ulong value);
    [PreserveSig] int SetDouble(ref Guid key, double value);
    [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
    [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(ref Guid key, IntPtr value, uint size);
    [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes? destination);
}

[ComImport]
[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
}

[ComImport]
[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
}

[ComImport]
[Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig] int GetStreamSelection(uint streamIndex, out int selected);
    [PreserveSig] int SetStreamSelection(uint streamIndex, int selected);
    [PreserveSig] int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType mediaType);
    [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType mediaType);
    [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType mediaType);
    [PreserveSig] int SetCurrentPosition(ref Guid timeFormat, IntPtr position);
    [PreserveSig]
    int ReadSample(
        uint streamIndex,
        uint controlFlags,
        out uint actualStreamIndex,
        out uint streamFlags,
        out long timestamp,
        out IMFSample? sample);
    [PreserveSig] int Flush(uint streamIndex);
    [PreserveSig] int GetServiceForStream(uint streamIndex, ref Guid service, ref Guid interfaceId, IntPtr value);
    [PreserveSig] int GetPresentationAttribute(uint streamIndex, ref Guid attribute, IntPtr value);
}

[ComImport]
[Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriter
{
    [PreserveSig] int AddStream(IMFMediaType targetMediaType, out uint streamIndex);
    [PreserveSig] int SetInputMediaType(uint streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
    [PreserveSig] int BeginWriting();
    [PreserveSig] int WriteSample(uint streamIndex, IMFSample sample);
    [PreserveSig] int SendStreamTick(uint streamIndex, long timestamp);
    [PreserveSig] int PlaceMarker(uint streamIndex, IntPtr context);
    [PreserveSig] int NotifyEndOfSegment(uint streamIndex);
    [PreserveSig] int Flush(uint streamIndex);
    [PreserveSig] int FinalizeWriting();
    [PreserveSig] int GetServiceForStream(uint streamIndex, ref Guid service, ref Guid interfaceId, IntPtr value);
    [PreserveSig] int GetStatistics(uint streamIndex, IntPtr statistics);
}
