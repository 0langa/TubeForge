using System.Buffers;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;

namespace TubeForge.Media.Ebml;

public sealed record WebMMuxReceipt(string DestinationPath, long BytesWritten);

public static class WebMTrackMuxer
{
    private const uint InfoId = 0x1549A966;
    private const uint TimestampScaleId = 0x2AD7B1;
    private const uint TracksId = 0x1654AE6B;
    private const uint TrackEntryId = 0xAE;
    private const uint TrackNumberId = 0xD7;
    private const uint TrackUidId = 0x73C5;
    private const uint TrackTypeId = 0x83;
    private const uint CodecId = 0x86;
    private const uint ClusterId = 0x1F43B675;
    private const uint TimecodeId = 0xE7;
    private const uint SimpleBlockId = 0xA3;
    private const uint BlockGroupId = 0xA0;
    private const uint BlockId = 0xA1;
    private const uint EncryptedBlockId = 0xAF;
    private const uint CuesId = 0x1C53BB6B;
    private const uint CuePointId = 0xBB;
    private const uint CueTimeId = 0xB3;
    private const uint CueTrackPositionsId = 0xB7;
    private const uint CueTrackId = 0xF7;
    private const uint CueClusterPositionId = 0xF1;
    private const int MaximumMetadataBytes = 64 * 1024 * 1024;
    private const int CopyBufferBytes = 128 * 1024;

    public static async Task<Result<WebMMuxReceipt>> MuxAsync(
        string videoPath,
        string audioPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            var videoFullPath = Path.GetFullPath(videoPath);
            var audioFullPath = Path.GetFullPath(audioPath);
            var destinationFullPath = Path.GetFullPath(destinationPath);
            ValidatePaths(videoFullPath, audioFullPath, destinationFullPath);

            var video = LoadInput(videoFullPath, expectedTrackType: 1);
            var audio = LoadInput(audioFullPath, expectedTrackType: 2);
            if (video.TimestampScale != audio.TimestampScale)
            {
                throw MuxFailure(
                    "Media.IncompatibleTracks",
                    "The WebM video and audio use different timestamp scales.");
            }

            var audioTrackNumber = ChooseTrackNumber(video.Track.Number);
            ValidateBlockTrackWidth(audio.Clusters, audioTrackNumber);
            var audioTrackUid = ChooseTrackUid(video.Track.Uid, audio.Track.Uid, audio.Track.UidWidth);
            var audioEntry = PatchTrackEntry(audio.Track, audioTrackNumber, audioTrackUid);
            var tracks = EbmlWriter.Element(TracksId, video.Track.EntryBytes, audioEntry);
            var segmentHeader = EbmlWriter.UnknownSizeSegmentHeader();

            var clusters = video.Clusters
                .Select((cluster, index) => new OutputCluster(video, cluster, IsAudio: false, index))
                .Concat(audio.Clusters.Select((cluster, index) => new OutputCluster(audio, cluster, IsAudio: true, index)))
                .OrderBy(cluster => cluster.Cluster.Timecode)
                .ThenBy(cluster => cluster.IsAudio)
                .ThenBy(cluster => cluster.SourceIndex)
                .ToArray();

            var segmentPosition = checked((long)video.InfoBytes.Length + tracks.Length);
            var cueLocations = new List<CueLocation>();
            foreach (var cluster in clusters)
            {
                if (!cluster.IsAudio)
                {
                    cueLocations.Add(new CueLocation(cluster.Cluster.Timecode, checked((ulong)segmentPosition)));
                }

                segmentPosition = checked(segmentPosition + cluster.Cluster.Header.TotalSize);
            }

            var cues = BuildCues(video.Track.Number, cueLocations);
            var directory = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw MuxFailure("Download.InvalidDestination", "The WebM output directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            temporaryPath = destinationFullPath + "." + Guid.NewGuid().ToString("N") + ".muxing";
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             CopyBufferBytes,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var videoStream = new FileStream(
                             video.Path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             CopyBufferBytes,
                             FileOptions.Asynchronous | FileOptions.RandomAccess))
            await using (var audioStream = new FileStream(
                             audio.Path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             CopyBufferBytes,
                             FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                await output.WriteAsync(video.HeaderBytes, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(segmentHeader, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(video.InfoBytes, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(tracks, cancellationToken).ConfigureAwait(false);
                foreach (var cluster in clusters)
                {
                    await CopyClusterAsync(
                            cluster.IsAudio ? audioStream : videoStream,
                            cluster.Cluster,
                            output,
                            cluster.IsAudio ? audioTrackNumber : null,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await output.WriteAsync(cues, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var containerValidation = MediaContainerValidator.Validate(temporaryPath, MediaContainer.WebM);
            if (!containerValidation.IsSuccess)
            {
                throw new WebMMuxException(containerValidation.Error!);
            }

            ValidateOutput(temporaryPath);
            File.Move(temporaryPath, destinationFullPath, overwrite: false);
            temporaryPath = null;
            var length = new FileInfo(destinationFullPath).Length;
            return Result<WebMMuxReceipt>.Success(new WebMMuxReceipt(destinationFullPath, length));
        }
        catch (OperationCanceledException)
        {
            return Failure("Operation.Cancelled", "WebM muxing was cancelled.");
        }
        catch (WebMMuxException exception)
        {
            return Result<WebMMuxReceipt>.Failure(exception.Error);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure("Media.MuxWriteFailed", "TubeForge cannot access a WebM input or output file.", exception);
        }
        catch (IOException exception)
        {
            return Failure("Media.MuxWriteFailed", "TubeForge could not read or write the WebM files.", exception);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or OverflowException)
        {
            return Failure("Media.InvalidEbml", "The WebM structure could not be processed safely.", exception);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static void ValidatePaths(string videoPath, string audioPath, string destinationPath)
    {
        if (!File.Exists(videoPath) || !File.Exists(audioPath))
        {
            throw MuxFailure("Media.InputMissing", "A selected WebM track file is missing.");
        }

        if (videoPath.Equals(audioPath, StringComparison.OrdinalIgnoreCase) ||
            destinationPath.Equals(videoPath, StringComparison.OrdinalIgnoreCase) ||
            destinationPath.Equals(audioPath, StringComparison.OrdinalIgnoreCase))
        {
            throw MuxFailure("Media.InvalidMuxPath", "Video, audio, and WebM output paths must be distinct.");
        }

        if (File.Exists(destinationPath))
        {
            throw MuxFailure("Download.DestinationExists", "The selected WebM output already exists.");
        }
    }

    private static WebMInput LoadInput(string path, ulong expectedTrackType)
    {
        var documentResult = EbmlReader.ReadDocument(path);
        if (!documentResult.IsSuccess)
        {
            throw new WebMMuxException(documentResult.Error!);
        }

        var document = documentResult.Value;
        var infoHeader = RequireSingle(document.SegmentChildren, InfoId, "Info");
        var tracksHeader = RequireSingle(document.SegmentChildren, TracksId, "Tracks");
        var clusterHeaders = document.SegmentChildren.Where(element => element.Id == ClusterId).ToArray();
        if (clusterHeaders.Length == 0)
        {
            throw MuxFailure("Media.InvalidEbml", "The WebM input contains no clusters.");
        }

        using var stream = EbmlReader.Open(path);
        var headerBytes = EbmlReader.ReadBytes(stream, document.Header, MaximumMetadataBytes);
        var infoBytes = EbmlReader.ReadBytes(stream, infoHeader, MaximumMetadataBytes);
        var tracksBytes = EbmlReader.ReadBytes(stream, tracksHeader, MaximumMetadataBytes);
        var track = ReadSingleTrack(tracksBytes);
        if (track.Type != expectedTrackType ||
            expectedTrackType == 1 && track.Codec is not ("V_VP8" or "V_VP9" or "V_AV1") ||
            expectedTrackType == 2 && track.Codec is not ("A_VORBIS" or "A_OPUS"))
        {
            throw MuxFailure(
                "Media.IncompatibleTracks",
                expectedTrackType == 1
                    ? "The selected WebM input is not a supported video-only track."
                    : "The selected WebM input is not a supported audio-only track.");
        }

        var timestampScale = ReadTimestampScale(infoBytes);
        var clusters = clusterHeaders.Select(header => ReadCluster(stream, header, track.Number)).ToArray();
        return new WebMInput(path, headerBytes, infoBytes, timestampScale, track, clusters);
    }

    private static WebMTrack ReadSingleTrack(byte[] tracksBytes)
    {
        var tracks = MemoryElement.ParseSingle(tracksBytes);
        var entries = tracks.Children(tracksBytes).Where(element => element.Id == TrackEntryId).ToArray();
        if (entries.Length != 1)
        {
            throw MuxFailure("Media.InvalidEbml", "Each adaptive WebM input must contain exactly one TrackEntry.");
        }

        var entryBytes = entries[0].CopyBytes(tracksBytes);
        var entry = MemoryElement.ParseSingle(entryBytes);
        var children = entry.Children(entryBytes);
        var numberElement = RequireSingle(children, TrackNumberId, "TrackNumber");
        var uidElement = RequireSingle(children, TrackUidId, "TrackUID");
        var typeElement = RequireSingle(children, TrackTypeId, "TrackType");
        var codecElement = RequireSingle(children, CodecId, "CodecID");
        var number = numberElement.ReadUnsigned(entryBytes);
        var uid = uidElement.ReadUnsigned(entryBytes);
        var type = typeElement.ReadUnsigned(entryBytes);
        var codec = Encoding.ASCII.GetString(codecElement.Content(entryBytes));
        if (number == 0 || number > 126 || uid == 0 || codec.Length == 0)
        {
            throw MuxFailure("Media.InvalidEbml", "The WebM track identity or codec is invalid.");
        }

        return new WebMTrack(
            number,
            uid,
            checked((int)uidElement.DataSize),
            type,
            codec,
            entryBytes,
            numberElement,
            uidElement);
    }

    private static ulong ReadTimestampScale(byte[] infoBytes)
    {
        var info = MemoryElement.ParseSingle(infoBytes);
        var values = info.Children(infoBytes).Where(element => element.Id == TimestampScaleId).ToArray();
        if (values.Length > 1)
        {
            throw MuxFailure("Media.InvalidEbml", "The WebM Info contains duplicate TimestampScale elements.");
        }

        var scale = values.Length == 0 ? 1_000_000UL : values[0].ReadUnsigned(infoBytes);
        return scale == 0
            ? throw MuxFailure("Media.InvalidEbml", "The WebM TimestampScale is zero.")
            : scale;
    }

    private static ClusterInfo ReadCluster(
        FileStream stream,
        EbmlElementHeader header,
        ulong expectedTrackNumber)
    {
        var children = EbmlReader.ReadChildren(stream, header);
        var timecodeElement = RequireSingle(children, TimecodeId, "Cluster Timecode");
        var timecode = EbmlReader.ReadUnsigned(stream, timecodeElement);
        var patches = new List<BlockPatch>();
        foreach (var child in children)
        {
            if (child.Id == SimpleBlockId)
            {
                patches.Add(ReadBlockTrack(stream, child, expectedTrackNumber));
            }
            else if (child.Id == BlockGroupId)
            {
                var blocks = EbmlReader.ReadChildren(stream, child).Where(element => element.Id == BlockId).ToArray();
                if (blocks.Length != 1)
                {
                    throw MuxFailure("Media.InvalidEbml", "A WebM BlockGroup must contain exactly one Block.");
                }

                patches.Add(ReadBlockTrack(stream, blocks[0], expectedTrackNumber));
            }
            else if (child.Id == EncryptedBlockId)
            {
                throw MuxFailure("Media.IncompatibleTracks", "Encrypted WebM blocks are not supported.");
            }
        }

        return new ClusterInfo(header, timecode, patches);
    }

    private static BlockPatch ReadBlockTrack(
        FileStream stream,
        EbmlElementHeader block,
        ulong expectedTrackNumber)
    {
        if (block.DataSize < 4)
        {
            throw MuxFailure("Media.InvalidEbml", "A WebM block is truncated.");
        }

        Span<byte> bytes = stackalloc byte[8];
        stream.Position = block.DataOffset;
        stream.ReadExactly(bytes[..1]);
        var width = EbmlReader.VintWidth(bytes[0], 8);
        if (width == 0 || width > block.DataSize)
        {
            throw MuxFailure("Media.InvalidEbml", "A WebM block TrackNumber is invalid.");
        }

        if (width > 1)
        {
            stream.ReadExactly(bytes.Slice(1, width - 1));
        }

        var marker = 1 << (8 - width);
        ulong number = (ulong)(bytes[0] & (marker - 1));
        for (var index = 1; index < width; index++)
        {
            number = (number << 8) | bytes[index];
        }

        if (number != expectedTrackNumber)
        {
            throw MuxFailure("Media.InvalidEbml", "A WebM block references an unexpected track number.");
        }

        return new BlockPatch(block.DataOffset, width);
    }

    private static byte[] PatchTrackEntry(WebMTrack track, ulong number, ulong uid)
    {
        var output = (byte[])track.EntryBytes.Clone();
        EbmlWriter.WriteUnsigned(
            output.AsSpan(checked((int)track.NumberElement.DataOffset), checked((int)track.NumberElement.DataSize)),
            number);
        EbmlWriter.WriteUnsigned(
            output.AsSpan(checked((int)track.UidElement.DataOffset), checked((int)track.UidElement.DataSize)),
            uid);
        return output;
    }

    private static ulong ChooseTrackNumber(ulong videoTrackNumber)
    {
        for (ulong candidate = 1; candidate <= 126; candidate++)
        {
            if (candidate != videoTrackNumber)
            {
                return candidate;
            }
        }

        throw MuxFailure("Media.IncompatibleTracks", "No compatible WebM audio track number is available.");
    }

    private static ulong ChooseTrackUid(ulong videoUid, ulong audioUid, int width)
    {
        var maximum = width == 8 ? ulong.MaxValue : (1UL << (width * 8)) - 1;
        if (audioUid != videoUid && audioUid <= maximum)
        {
            return audioUid;
        }

        for (ulong candidate = 1; candidate <= maximum; candidate++)
        {
            if (candidate != videoUid)
            {
                return candidate;
            }
        }

        throw MuxFailure("Media.IncompatibleTracks", "No unique WebM audio TrackUID fits the source metadata.");
    }

    private static void ValidateBlockTrackWidth(IEnumerable<ClusterInfo> clusters, ulong trackNumber)
    {
        foreach (var patch in clusters.SelectMany(cluster => cluster.BlockPatches))
        {
            _ = EbmlWriter.EncodeVintValue(trackNumber, patch.Width);
        }
    }

    private static byte[] BuildCues(ulong videoTrackNumber, IEnumerable<CueLocation> locations)
    {
        var cuePoints = locations.Select(location => EbmlWriter.Element(
                CuePointId,
                EbmlWriter.UnsignedElement(CueTimeId, location.Timecode),
                EbmlWriter.Element(
                    CueTrackPositionsId,
                    EbmlWriter.UnsignedElement(CueTrackId, videoTrackNumber),
                    EbmlWriter.UnsignedElement(CueClusterPositionId, location.SegmentPosition))))
            .ToArray();
        var cues = EbmlWriter.Element(CuesId, cuePoints);
        return cues.Length <= MaximumMetadataBytes
            ? cues
            : throw MuxFailure("Media.MetadataLimit", "The generated WebM cue table exceeds the safety limit.");
    }

    private static async Task CopyClusterAsync(
        FileStream input,
        ClusterInfo cluster,
        FileStream output,
        ulong? replacementTrackNumber,
        CancellationToken cancellationToken)
    {
        var cursor = cluster.Header.Offset;
        foreach (var patch in cluster.BlockPatches.OrderBy(value => value.Offset))
        {
            await CopyRangeAsync(input, cursor, patch.Offset - cursor, output, cancellationToken)
                .ConfigureAwait(false);
            input.Position = patch.Offset + patch.Width;
            if (replacementTrackNumber is null)
            {
                input.Position = patch.Offset;
                await CopyRangeAsync(input, patch.Offset, patch.Width, output, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await output.WriteAsync(
                        EbmlWriter.EncodeVintValue(replacementTrackNumber.Value, patch.Width),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            cursor = patch.Offset + patch.Width;
        }

        await CopyRangeAsync(input, cursor, cluster.Header.EndOffset - cursor, output, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CopyRangeAsync(
        FileStream input,
        long offset,
        long length,
        FileStream output,
        CancellationToken cancellationToken)
    {
        if (length < 0)
        {
            throw MuxFailure("Media.InvalidEbml", "A WebM copy range is negative.");
        }

        input.Position = offset;
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                var requested = (int)Math.Min(remaining, buffer.Length);
                var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw MuxFailure("Media.InvalidEbml", "A WebM element ended during muxing.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateOutput(string path)
    {
        var document = EbmlReader.ReadDocument(path);
        if (!document.IsSuccess)
        {
            throw new WebMMuxException(document.Error!);
        }

        var children = document.Value.SegmentChildren;
        if (children.Count(element => element.Id == InfoId) != 1 ||
            children.Count(element => element.Id == TracksId) != 1 ||
            children.Count(element => element.Id == ClusterId) == 0 ||
            children.Count(element => element.Id == CuesId) != 1)
        {
            throw MuxFailure("Media.InvalidMuxOutput", "The muxed WebM is missing required segment elements.");
        }

        using var stream = EbmlReader.Open(path);
        var tracksHeader = children.Single(element => element.Id == TracksId);
        var tracksBytes = EbmlReader.ReadBytes(stream, tracksHeader, MaximumMetadataBytes);
        var tracks = MemoryElement.ParseSingle(tracksBytes).Children(tracksBytes)
            .Where(element => element.Id == TrackEntryId)
            .Select(element => ReadTrackIdentity(element.CopyBytes(tracksBytes)))
            .ToArray();
        if (tracks.Length != 2 || tracks[0].Type != 1 || tracks[1].Type != 2 || tracks[0].Number == tracks[1].Number)
        {
            throw MuxFailure("Media.InvalidMuxOutput", "The muxed WebM track table is invalid.");
        }

        ulong? previousTimecode = null;
        foreach (var cluster in children.Where(element => element.Id == ClusterId))
        {
            var timecode = EbmlReader.ReadUnsigned(
                stream,
                RequireSingle(EbmlReader.ReadChildren(stream, cluster), TimecodeId, "Cluster Timecode"));
            if (previousTimecode > timecode)
            {
                throw MuxFailure("Media.InvalidMuxOutput", "The muxed WebM clusters are not time-ordered.");
            }

            previousTimecode = timecode;
        }
    }

    private static (ulong Number, ulong Type) ReadTrackIdentity(byte[] entryBytes)
    {
        var entry = MemoryElement.ParseSingle(entryBytes);
        var children = entry.Children(entryBytes);
        return (
            RequireSingle(children, TrackNumberId, "TrackNumber").ReadUnsigned(entryBytes),
            RequireSingle(children, TrackTypeId, "TrackType").ReadUnsigned(entryBytes));
    }

    private static T RequireSingle<T>(IEnumerable<T> values, Func<T, bool> predicate, string label)
    {
        var matches = values.Where(predicate).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw MuxFailure("Media.InvalidEbml", $"The WebM must contain exactly one {label} element here.");
    }

    private static EbmlElementHeader RequireSingle(
        IEnumerable<EbmlElementHeader> values,
        uint id,
        string label) =>
        RequireSingle(values, element => element.Id == id, label);

    private static MemoryElement RequireSingle(
        IEnumerable<MemoryElement> values,
        uint id,
        string label) =>
        RequireSingle(values, element => element.Id == id, label);

    private static WebMMuxException MuxFailure(string code, string message) =>
        new(new TubeForgeError(code, message));

    private static Result<WebMMuxReceipt> Failure(string code, string message, Exception? exception = null) =>
        Result<WebMMuxReceipt>.Failure(new TubeForgeError(code, message, exception?.GetType().Name));

    private sealed record WebMInput(
        string Path,
        byte[] HeaderBytes,
        byte[] InfoBytes,
        ulong TimestampScale,
        WebMTrack Track,
        IReadOnlyList<ClusterInfo> Clusters);

    private sealed record WebMTrack(
        ulong Number,
        ulong Uid,
        int UidWidth,
        ulong Type,
        string Codec,
        byte[] EntryBytes,
        MemoryElement NumberElement,
        MemoryElement UidElement);

    private sealed record ClusterInfo(
        EbmlElementHeader Header,
        ulong Timecode,
        IReadOnlyList<BlockPatch> BlockPatches);

    private sealed record OutputCluster(
        WebMInput Source,
        ClusterInfo Cluster,
        bool IsAudio,
        int SourceIndex);

    private readonly record struct BlockPatch(long Offset, int Width);

    private readonly record struct CueLocation(ulong Timecode, ulong SegmentPosition);

    private sealed class WebMMuxException(TubeForgeError error) : Exception(error.Message)
    {
        public TubeForgeError Error { get; } = error;
    }

    private readonly record struct MemoryElement(
        uint Id,
        int Offset,
        int HeaderSize,
        int DataSize)
    {
        public long DataOffset => Offset + HeaderSize;

        public long EndOffset => DataOffset + DataSize;

        public static MemoryElement ParseSingle(byte[] bytes)
        {
            var elements = ParseRange(bytes, 0, bytes.Length);
            return elements.Count == 1 && elements[0].EndOffset == bytes.Length
                ? elements[0]
                : throw MuxFailure("Media.InvalidEbml", "An in-memory WebM element is malformed.");
        }

        public IReadOnlyList<MemoryElement> Children(byte[] source) =>
            ParseRange(source, checked((int)DataOffset), DataSize);

        public ReadOnlySpan<byte> Content(byte[] source) => source.AsSpan(checked((int)DataOffset), DataSize);

        public byte[] CopyBytes(byte[] source) => source.AsSpan(Offset, checked((int)(EndOffset - Offset))).ToArray();

        public ulong ReadUnsigned(byte[] source)
        {
            if (DataSize is < 1 or > 8)
            {
                throw MuxFailure("Media.InvalidEbml", $"WebM integer 0x{Id:X} has an invalid width.");
            }

            ulong value = 0;
            foreach (var valueByte in Content(source))
            {
                value = (value << 8) | valueByte;
            }

            return value;
        }

        private static IReadOnlyList<MemoryElement> ParseRange(byte[] bytes, int offset, int length)
        {
            var elements = new List<MemoryElement>();
            var end = checked(offset + length);
            var cursor = offset;
            while (cursor < end)
            {
                if (end - cursor < 2)
                {
                    throw MuxFailure("Media.InvalidEbml", "An in-memory WebM element header is truncated.");
                }

                var idWidth = EbmlReader.VintWidth(bytes[cursor], 4);
                if (idWidth == 0 || cursor + idWidth >= end)
                {
                    throw MuxFailure("Media.InvalidEbml", "An in-memory WebM element ID is invalid.");
                }

                uint id = 0;
                for (var index = 0; index < idWidth; index++)
                {
                    id = (id << 8) | bytes[cursor + index];
                }

                var sizeOffset = cursor + idWidth;
                var sizeWidth = EbmlReader.VintWidth(bytes[sizeOffset], 8);
                if (sizeWidth == 0 || sizeOffset + sizeWidth > end)
                {
                    throw MuxFailure("Media.InvalidEbml", "An in-memory WebM element size is invalid.");
                }

                var marker = 1 << (8 - sizeWidth);
                ulong size = (ulong)(bytes[sizeOffset] & (marker - 1));
                for (var index = 1; index < sizeWidth; index++)
                {
                    size = (size << 8) | bytes[sizeOffset + index];
                }

                var unknown = (1UL << (7 * sizeWidth)) - 1;
                var headerSize = idWidth + sizeWidth;
                if (size == unknown || size > int.MaxValue || size > (ulong)(end - cursor - headerSize))
                {
                    throw MuxFailure("Media.InvalidEbml", $"In-memory element 0x{id:X} has an invalid size.");
                }

                elements.Add(new MemoryElement(id, cursor, headerSize, (int)size));
                cursor = checked(cursor + headerSize + (int)size);
            }

            return elements;
        }
    }
}
