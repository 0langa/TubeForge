using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;

namespace TubeForge.Media.IsoBmff;

public sealed record Mp4MuxReceipt(string DestinationPath, long BytesWritten);

public static class Mp4TrackMuxer
{
    private const int MaximumMetadataBytes = 64 * 1024 * 1024;
    private const int CopyBufferBytes = 128 * 1024;
    private static readonly HashSet<string> TrackContainers = ["trak", "mdia", "minf", "stbl"];
    private static readonly HashSet<string> FragmentContainers = ["moof", "traf"];

    public static async Task<Result<Mp4MuxReceipt>> MuxAsync(
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

            var video = LoadInput(videoFullPath, "vide");
            var audio = LoadInput(audioFullPath, "soun");
            var videoTiming = ReadMovieTiming(video.MovieHeader);
            var audioTiming = ReadMovieTiming(audio.MovieHeader);
            var videoTrackTiming = ReadTrackTiming(video.Track);
            var audioTrackTiming = ReadTrackTiming(audio.Track);
            if (videoTrackTiming.TrackId == uint.MaxValue)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "The video track identifier cannot be remapped safely.");
            }

            var audioTrackId = videoTrackTiming.TrackId + 1;
            var nextTrackId = audioTrackId == uint.MaxValue ? uint.MaxValue : audioTrackId + 1;
            var audioMovieDuration = ScaleDuration(
                audioTiming.Duration,
                audioTiming.Timescale,
                videoTiming.Timescale);
            var audioTrackDuration = ScaleDuration(
                audioTrackTiming.Duration,
                audioTiming.Timescale,
                videoTiming.Timescale);
            var movieDuration = Math.Max(videoTiming.Duration, audioMovieDuration);
            if (video.IsFragmented != audio.IsFragmented)
            {
                throw MuxFailure(
                    "Media.IncompatibleTracks",
                    "Regular and fragmented MP4 tracks cannot be combined in one output.");
            }

            byte[] movie;
            IReadOnlyList<OutputFragment> outputFragments = [];
            if (video.IsFragmented)
            {
                var videoTrack = RewriteTrack(video.Track, _ => 0, null, null);
                var audioTrack = RewriteTrack(audio.Track, _ => 0, audioTrackId, audioTrackDuration);
                movie = BuildFragmentedMovie(
                    video,
                    audio,
                    videoTrack,
                    audioTrack,
                    movieDuration,
                    nextTrackId,
                    audioTrackId);
                outputFragments = PlanFragments(
                    video,
                    audio,
                    checked(video.FileType.Length + movie.Length),
                    videoTrackTiming.TrackId,
                    audioTrackId);
            }
            else
            {
                var placeholderVideoTrack = RewriteTrack(video.Track, _ => 0, null, null);
                var placeholderAudioTrack = RewriteTrack(audio.Track, _ => 0, audioTrackId, audioTrackDuration);
                var placeholderMovie = BuildMovie(
                    video,
                    placeholderVideoTrack,
                    placeholderAudioTrack,
                    movieDuration,
                    nextTrackId);

                var videoMappings = CreateOutputMappings(
                    video.MediaData,
                    checked(video.FileType.Length + placeholderMovie.Length));
                var afterVideo = videoMappings.Count == 0
                    ? checked(video.FileType.Length + placeholderMovie.Length)
                    : videoMappings[^1].OutputEnd;
                var audioMappings = CreateOutputMappings(audio.MediaData, afterVideo);

                var rewrittenVideoTrack = RewriteTrack(
                    video.Track,
                    offset => TranslateChunkOffset(offset, videoMappings),
                    null,
                    null);
                var rewrittenAudioTrack = RewriteTrack(
                    audio.Track,
                    offset => TranslateChunkOffset(offset, audioMappings),
                    audioTrackId,
                    audioTrackDuration);
                movie = BuildMovie(
                    video,
                    rewrittenVideoTrack,
                    rewrittenAudioTrack,
                    movieDuration,
                    nextTrackId);
                if (movie.Length != placeholderMovie.Length)
                {
                    throw MuxFailure("Media.MuxInvariant", "The rewritten MP4 metadata changed size unexpectedly.");
                }
            }

            var directory = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw MuxFailure("Download.InvalidDestination", "The output directory is invalid.");
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
            {
                await output.WriteAsync(video.FileType, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(movie, cancellationToken).ConfigureAwait(false);
                if (video.IsFragmented)
                {
                    await CopyFragmentsAsync(outputFragments, output, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await CopyMediaDataAsync(videoFullPath, video.MediaData, output, cancellationToken)
                        .ConfigureAwait(false);
                    await CopyMediaDataAsync(audioFullPath, audio.MediaData, output, cancellationToken)
                        .ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var containerValidation = MediaContainerValidator.Validate(temporaryPath, MediaContainer.Mp4);
            if (!containerValidation.IsSuccess)
            {
                throw new Mp4MuxException(containerValidation.Error!);
            }

            ValidateMuxedOutput(temporaryPath);
            File.Move(temporaryPath, destinationFullPath, overwrite: false);
            temporaryPath = null;
            var length = new FileInfo(destinationFullPath).Length;
            return Result<Mp4MuxReceipt>.Success(new Mp4MuxReceipt(destinationFullPath, length));
        }
        catch (OperationCanceledException)
        {
            return Failure("Operation.Cancelled", "MP4 muxing was cancelled.");
        }
        catch (Mp4MuxException exception)
        {
            return Result<Mp4MuxReceipt>.Failure(exception.Error);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure("Media.MuxWriteFailed", "TubeForge cannot access an MP4 input or output file.", exception);
        }
        catch (IOException exception)
        {
            return Failure("Media.MuxWriteFailed", "TubeForge could not read or write the MP4 files.", exception);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or OverflowException)
        {
            return Failure("Media.InvalidIsoBmff", "The MP4 structure could not be processed safely.", exception);
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
            throw MuxFailure("Media.InputMissing", "A selected MP4 track file is missing.");
        }

        if (videoPath.Equals(audioPath, StringComparison.OrdinalIgnoreCase) ||
            destinationPath.Equals(videoPath, StringComparison.OrdinalIgnoreCase) ||
            destinationPath.Equals(audioPath, StringComparison.OrdinalIgnoreCase))
        {
            throw MuxFailure("Media.InvalidMuxPath", "Video, audio, and output paths must be distinct.");
        }

        if (File.Exists(destinationPath))
        {
            throw MuxFailure("Download.DestinationExists", "The selected MP4 output already exists.");
        }
    }

    private static Mp4Input LoadInput(string path, string expectedHandler)
    {
        var topLevelResult = IsoBmffReader.ReadTopLevel(path);
        if (!topLevelResult.IsSuccess)
        {
            throw new Mp4MuxException(topLevelResult.Error!);
        }

        var topLevel = topLevelResult.Value;
        if (topLevel.Count(box => box.Type == "moov") != 1)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The MP4 must contain exactly one movie box.");
        }

        var isFragmented = topLevel.Any(box => box.Type == "moof");
        var fileTypeHeader = topLevel.FirstOrDefault(box => box.Type == "ftyp");
        var movieHeader = topLevel.Single(box => box.Type == "moov");
        var mediaData = topLevel.Where(box => box.Type == "mdat").ToArray();
        if (fileTypeHeader.Size == 0 || mediaData.Length == 0 || movieHeader.Size > MaximumMetadataBytes)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The MP4 is missing bounded file, movie, or media-data boxes.");
        }

        var fileType = ReadBox(path, fileTypeHeader);
        var movie = ReadBox(path, movieHeader);
        var movieBox = MemoryBox.ParseSingle(movie);
        var children = movieBox.Children(movie);
        var movieHeaderBox = RequireSingle(children, "mvhd");
        var tracks = children.Where(box => box.Type == "trak").ToArray();
        var movieExtensions = children.Where(box => box.Type == "mvex").ToArray();
        if (tracks.Length != 1 || isFragmented != (movieExtensions.Length == 1))
        {
            throw MuxFailure(
                "Media.InvalidIsoBmff",
                "Each adaptive MP4 input must contain one track and a matching regular or fragmented movie layout.");
        }

        var handler = ReadHandlerType(movie, tracks[0]);
        if (!handler.Equals(expectedHandler, StringComparison.Ordinal))
        {
            throw MuxFailure(
                "Media.IncompatibleTracks",
                expectedHandler == "vide"
                    ? "The selected video input does not contain a video track."
                    : "The selected audio input does not contain an audio track.");
        }

        var trackTiming = ReadTrackTiming(tracks[0]);
        var fragments = isFragmented
            ? ReadFragments(path, topLevel, trackTiming.TrackId)
            : [];
        return new Mp4Input(
            path,
            new FileInfo(path).Length,
            fileType,
            movie,
            movieBox,
            movieHeaderBox,
            tracks[0],
            mediaData,
            isFragmented,
            isFragmented ? movieExtensions[0] : null,
            fragments,
            ReadMediaTimescale(tracks[0]));
    }

    private static byte[] BuildMovie(
        Mp4Input video,
        byte[] videoTrack,
        byte[] audioTrack,
        ulong duration,
        uint nextTrackId)
    {
        var children = video.MovieBox.Children(video.Movie);
        using var payload = new MemoryStream(video.Movie.Length + audioTrack.Length);
        foreach (var child in children)
        {
            if (child.Type == "mvhd")
            {
                payload.Write(PatchMovieHeader(child.CopyBytes(video.Movie), duration, nextTrackId));
            }
            else if (child.Type == "trak")
            {
                payload.Write(videoTrack);
                payload.Write(audioTrack);
            }
            else
            {
                payload.Write(child.CopyBytes(video.Movie));
            }
        }

        return MakeBox("moov", payload.ToArray());
    }

    private static byte[] BuildFragmentedMovie(
        Mp4Input video,
        Mp4Input audio,
        byte[] videoTrack,
        byte[] audioTrack,
        ulong duration,
        uint nextTrackId,
        uint audioTrackId)
    {
        if (video.MovieExtension is not MemoryBox videoExtension ||
            audio.MovieExtension is not MemoryBox audioExtension)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A fragmented MP4 is missing its movie extension.");
        }

        var mergedExtension = BuildMergedMovieExtension(
            video.Movie,
            videoExtension,
            audio.Movie,
            audioExtension,
            audioTrackId);
        var children = video.MovieBox.Children(video.Movie);
        using var payload = new MemoryStream(video.Movie.Length + audioTrack.Length + mergedExtension.Length);
        foreach (var child in children)
        {
            if (child.Type == "mvhd")
            {
                payload.Write(PatchMovieHeader(child.CopyBytes(video.Movie), duration, nextTrackId));
            }
            else if (child.Type == "trak")
            {
                payload.Write(videoTrack);
                payload.Write(audioTrack);
            }
            else if (child.Type == "mvex")
            {
                payload.Write(mergedExtension);
            }
            else
            {
                payload.Write(child.CopyBytes(video.Movie));
            }
        }

        return MakeBox("moov", payload.ToArray());
    }

    private static byte[] BuildMergedMovieExtension(
        byte[] videoMovie,
        MemoryBox videoExtension,
        byte[] audioMovie,
        MemoryBox audioExtension,
        uint audioTrackId)
    {
        var videoChildren = videoExtension.Children(videoMovie);
        var audioTrackExtends = audioExtension.Children(audioMovie)
            .Where(box => box.Type == "trex")
            .ToArray();
        if (videoChildren.Count(box => box.Type == "trex") != 1 || audioTrackExtends.Length != 1)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "Each fragmented MP4 track must contain one TrackExtends box.");
        }

        using var payload = new MemoryStream(videoExtension.Size + audioTrackExtends[0].Size);
        foreach (var child in videoChildren)
        {
            payload.Write(child.CopyBytes(videoMovie));
        }

        payload.Write(PatchTrackExtends(audioTrackExtends[0].CopyBytes(audioMovie), audioTrackId));
        return MakeBox("mvex", payload.ToArray());
    }

    private static byte[] PatchTrackExtends(byte[] boxBytes, uint trackId)
    {
        var box = MemoryBox.ParseSingle(boxBytes);
        if (box.Type != "trex" || box.Content(boxBytes).Length < 24)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A TrackExtends box is truncated.");
        }

        var output = (byte[])boxBytes.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(box.HeaderSize + 4, 4), trackId);
        return output;
    }

    private static IReadOnlyList<InputFragment> ReadFragments(
        string path,
        IReadOnlyList<IsoBmffBoxHeader> topLevel,
        uint expectedTrackId)
    {
        var fragments = new List<InputFragment>();
        for (var index = 0; index < topLevel.Count; index++)
        {
            if (topLevel[index].Type != "moof")
            {
                continue;
            }

            if (index + 1 >= topLevel.Count || topLevel[index + 1].Type != "mdat")
            {
                throw MuxFailure("Media.InvalidIsoBmff", "Each MovieFragment must be followed by one MediaData box.");
            }

            var moofHeader = topLevel[index];
            var mdatHeader = topLevel[index + 1];
            var moofBytes = ReadBox(path, moofHeader);
            var moof = MemoryBox.ParseSingle(moofBytes);
            var children = moof.Children(moofBytes);
            _ = RequireSingle(children, "mfhd");
            var trackFragment = RequireSingle(children, "traf");
            var trackChildren = trackFragment.Children(moofBytes);
            var trackHeader = RequireSingle(trackChildren, "tfhd");
            var trackHeaderContent = trackHeader.Content(moofBytes);
            if (trackHeaderContent.Length < 8 ||
                BinaryPrimitives.ReadUInt32BigEndian(trackHeaderContent.Slice(4, 4)) != expectedTrackId)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A MovieFragment references an unexpected track.");
            }

            var decodeTime = ReadDecodeTime(RequireSingle(trackChildren, "tfdt"), moofBytes);
            ValidateFragmentDataOffsets(moofHeader, mdatHeader, trackHeader, trackChildren, moofBytes);
            fragments.Add(new InputFragment(moofHeader, mdatHeader, moofBytes, decodeTime));
            index++;
        }

        return fragments.Count > 0
            ? fragments
            : throw MuxFailure("Media.InvalidIsoBmff", "The fragmented MP4 contains no complete fragments.");
    }

    private static ulong ReadDecodeTime(MemoryBox decodeTime, byte[] source)
    {
        var content = decodeTime.Content(source);
        return content.Length switch
        {
            >= 8 when content[0] == 0 => BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4)),
            >= 12 when content[0] == 1 => BinaryPrimitives.ReadUInt64BigEndian(content.Slice(4, 8)),
            _ => throw MuxFailure("Media.InvalidIsoBmff", "A TrackFragmentDecodeTime box is invalid.")
        };
    }

    private static void ValidateFragmentDataOffsets(
        IsoBmffBoxHeader moof,
        IsoBmffBoxHeader mdat,
        MemoryBox trackHeader,
        IReadOnlyList<MemoryBox> trackChildren,
        byte[] source)
    {
        var headerContent = trackHeader.Content(source);
        var flags = ReadFullBoxFlags(headerContent);
        ulong baseOffset;
        if ((flags & 0x000001) != 0)
        {
            if (headerContent.Length < 16)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A TrackFragmentHeader base offset is truncated.");
            }

            baseOffset = BinaryPrimitives.ReadUInt64BigEndian(headerContent.Slice(8, 8));
        }
        else
        {
            baseOffset = (ulong)moof.Offset;
        }

        foreach (var run in trackChildren.Where(box => box.Type == "trun"))
        {
            var content = run.Content(source);
            var runFlags = ReadFullBoxFlags(content);
            if (content.Length < 8)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A TrackRun box is truncated.");
            }

            if ((runFlags & 0x000001) == 0)
            {
                continue;
            }

            if (content.Length < 12)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A TrackRun data offset is truncated.");
            }

            var relativeOffset = BinaryPrimitives.ReadInt32BigEndian(content.Slice(8, 4));
            var absoluteOffset = checked((long)baseOffset + relativeOffset);
            if (absoluteOffset < mdat.DataOffset || absoluteOffset >= mdat.EndOffset)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A TrackRun points outside its MediaData box.");
            }
        }
    }

    private static int ReadFullBoxFlags(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A FullBox header is truncated.");
        }

        return (content[1] << 16) | (content[2] << 8) | content[3];
    }

    private static IReadOnlyList<OutputFragment> PlanFragments(
        Mp4Input video,
        Mp4Input audio,
        long startingOutputOffset,
        uint videoTrackId,
        uint audioTrackId)
    {
        var candidates = video.Fragments
            .Select((fragment, index) => new FragmentCandidate(
                video.Path,
                fragment,
                video.MediaTimescale,
                videoTrackId,
                IsAudio: false,
                index))
            .Concat(audio.Fragments.Select((fragment, index) => new FragmentCandidate(
                audio.Path,
                fragment,
                audio.MediaTimescale,
                audioTrackId,
                IsAudio: true,
                index)))
            .OrderBy(candidate => candidate, FragmentCandidateComparer.Instance)
            .ToArray();

        var output = new List<OutputFragment>(candidates.Length);
        var offset = startingOutputOffset;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            output.Add(new OutputFragment(
                candidate.Path,
                candidate.Fragment,
                candidate.OutputTrackId,
                checked((uint)index + 1),
                offset));
            offset = checked(offset + candidate.Fragment.Moof.Size + candidate.Fragment.Mdat.Size);
        }

        return output;
    }

    private static async Task CopyFragmentsAsync(
        IReadOnlyList<OutputFragment> fragments,
        FileStream output,
        CancellationToken cancellationToken)
    {
        var streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var fragment in fragments)
            {
                if (!streams.TryGetValue(fragment.Path, out var input))
                {
                    input = new FileStream(
                        fragment.Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        CopyBufferBytes,
                        FileOptions.Asynchronous | FileOptions.RandomAccess);
                    streams.Add(fragment.Path, input);
                }

                var moof = PatchFragment(fragment);
                await output.WriteAsync(moof, cancellationToken).ConfigureAwait(false);
                await CopyRangeAsync(
                        input,
                        fragment.Fragment.Mdat.Offset,
                        fragment.Fragment.Mdat.Size,
                        output,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var stream in streams.Values)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static byte[] PatchFragment(OutputFragment fragment)
    {
        var output = (byte[])fragment.Fragment.MoofBytes.Clone();
        var moof = MemoryBox.ParseSingle(output);
        var children = moof.Children(output);
        var movieFragmentHeader = RequireSingle(children, "mfhd");
        var movieHeaderContentOffset = movieFragmentHeader.Offset + movieFragmentHeader.HeaderSize;
        if (movieFragmentHeader.Content(output).Length < 8)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A MovieFragmentHeader is truncated.");
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            output.AsSpan(movieHeaderContentOffset + 4, 4),
            fragment.SequenceNumber);
        var trackFragment = RequireSingle(children, "traf");
        var trackHeader = RequireSingle(trackFragment.Children(output), "tfhd");
        var trackContentOffset = trackHeader.Offset + trackHeader.HeaderSize;
        var trackContent = trackHeader.Content(output);
        var flags = ReadFullBoxFlags(trackContent);
        if (trackContent.Length < 8)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A TrackFragmentHeader is truncated.");
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            output.AsSpan(trackContentOffset + 4, 4),
            fragment.OutputTrackId);
        if ((flags & 0x000001) != 0)
        {
            if (trackContent.Length < 16)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A TrackFragmentHeader base offset is truncated.");
            }

            var sourceBase = BinaryPrimitives.ReadUInt64BigEndian(trackContent.Slice(8, 8));
            var sourceStart = (ulong)fragment.Fragment.Moof.Offset;
            var sourceEnd = checked((ulong)fragment.Fragment.Mdat.EndOffset);
            if (sourceBase < sourceStart || sourceBase >= sourceEnd)
            {
                throw MuxFailure("Media.InvalidIsoBmff", "A fragment base offset points outside its fragment.");
            }

            var targetBase = checked((ulong)fragment.OutputOffset + (sourceBase - sourceStart));
            BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(trackContentOffset + 8, 8), targetBase);
        }

        return output;
    }

    private static async Task CopyRangeAsync(
        FileStream input,
        long offset,
        long length,
        FileStream output,
        CancellationToken cancellationToken)
    {
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
                    throw MuxFailure("Media.InvalidIsoBmff", "A fragmented MP4 box ended unexpectedly.");
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

    private static byte[] RewriteTrack(
        MemoryBox track,
        Func<ulong, ulong> translateOffset,
        uint? replacementTrackId,
        ulong? replacementDuration) =>
        RewriteBox(track.Bytes, track, translateOffset, replacementTrackId, replacementDuration);

    private static byte[] RewriteBox(
        byte[] source,
        MemoryBox box,
        Func<ulong, ulong> translateOffset,
        uint? replacementTrackId,
        ulong? replacementDuration)
    {
        if (box.Type is "stco" or "co64")
        {
            return RewriteChunkOffsets(source, box, translateOffset);
        }

        if (box.Type == "tkhd" && replacementTrackId is not null && replacementDuration is not null)
        {
            return PatchTrackHeader(box.CopyBytes(source), replacementTrackId.Value, replacementDuration.Value);
        }

        if (!TrackContainers.Contains(box.Type))
        {
            return box.CopyBytes(source);
        }

        using var payload = new MemoryStream(box.Size);
        foreach (var child in box.Children(source))
        {
            payload.Write(RewriteBox(source, child, translateOffset, replacementTrackId, replacementDuration));
        }

        return MakeBox(box.Type, payload.ToArray());
    }

    private static byte[] RewriteChunkOffsets(
        byte[] source,
        MemoryBox box,
        Func<ulong, ulong> translateOffset)
    {
        var content = box.Content(source);
        if (content.Length < 8)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A chunk-offset table is truncated.");
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4));
        var inputWidth = box.Type == "stco" ? 4 : 8;
        var required = checked(8L + ((long)count * inputWidth));
        if (required != content.Length || count > int.MaxValue / 8)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "A chunk-offset table has an invalid entry count.");
        }

        var payload = new byte[checked(8 + ((int)count * 8))];
        content[..4].CopyTo(payload);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), count);
        for (var index = 0; index < count; index++)
        {
            var sourceOffset = inputWidth == 4
                ? BinaryPrimitives.ReadUInt32BigEndian(content.Slice(8 + ((int)index * 4), 4))
                : BinaryPrimitives.ReadUInt64BigEndian(content.Slice(8 + ((int)index * 8), 8));
            BinaryPrimitives.WriteUInt64BigEndian(
                payload.AsSpan(8 + ((int)index * 8), 8),
                translateOffset(sourceOffset));
        }

        return MakeBox("co64", payload);
    }

    private static IReadOnlyList<OffsetMapping> CreateOutputMappings(
        IReadOnlyList<IsoBmffBoxHeader> mediaData,
        long startingOutputOffset)
    {
        var mappings = new List<OffsetMapping>(mediaData.Count);
        var outputOffset = startingOutputOffset;
        foreach (var box in mediaData)
        {
            var outputHeaderSize = MdatHeaderSize(box.DataSize);
            var outputDataOffset = checked(outputOffset + outputHeaderSize);
            var outputEnd = checked(outputDataOffset + box.DataSize);
            mappings.Add(new OffsetMapping(
                box.DataOffset,
                box.DataSize,
                outputDataOffset,
                outputEnd));
            outputOffset = outputEnd;
        }

        return mappings;
    }

    private static ulong TranslateChunkOffset(ulong sourceOffset, IReadOnlyList<OffsetMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            var sourceStart = (ulong)mapping.SourceDataOffset;
            var sourceEnd = checked(sourceStart + (ulong)mapping.DataLength);
            if (sourceOffset >= sourceStart && sourceOffset < sourceEnd)
            {
                return checked((ulong)mapping.OutputDataOffset + (sourceOffset - sourceStart));
            }
        }

        throw MuxFailure("Media.InvalidChunkOffset", "An MP4 chunk offset points outside every media-data box.");
    }

    private static async Task CopyMediaDataAsync(
        string sourcePath,
        IReadOnlyList<IsoBmffBoxHeader> mediaData,
        FileStream output,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            foreach (var box in mediaData)
            {
                await WriteMdatHeaderAsync(output, box.DataSize, cancellationToken).ConfigureAwait(false);
                input.Position = box.DataOffset;
                var remaining = box.DataSize;
                while (remaining > 0)
                {
                    var requested = (int)Math.Min(remaining, buffer.Length);
                    var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw MuxFailure("Media.InvalidIsoBmff", "An MP4 media-data box ended unexpectedly.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    remaining -= read;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task WriteMdatHeaderAsync(
        Stream output,
        long dataSize,
        CancellationToken cancellationToken)
    {
        var headerSize = MdatHeaderSize(dataSize);
        var header = new byte[headerSize];
        if (headerSize == 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)(dataSize + 8)));
            "mdat"u8.CopyTo(header.AsSpan(4));
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, 1);
            "mdat"u8.CopyTo(header.AsSpan(4));
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), checked((ulong)dataSize + 16));
        }

        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
    }

    private static int MdatHeaderSize(long dataSize) => dataSize <= uint.MaxValue - 8L ? 8 : 16;

    private static void ValidateMuxedOutput(string path)
    {
        var parsed = IsoBmffReader.ReadTopLevel(path);
        if (!parsed.IsSuccess)
        {
            throw new Mp4MuxException(parsed.Error!);
        }

        var boxes = parsed.Value;
        if (boxes.Count(box => box.Type == "moov") != 1 || boxes.Count(box => box.Type == "mdat") < 2)
        {
            throw MuxFailure("Media.InvalidMuxOutput", "The muxed MP4 is missing movie or track media data.");
        }

        var movie = ReadBox(path, boxes.Single(box => box.Type == "moov"));
        var movieBox = MemoryBox.ParseSingle(movie);
        var tracks = movieBox.Children(movie).Where(box => box.Type == "trak").ToArray();
        if (tracks.Length != 2 || ReadHandlerType(movie, tracks[0]) != "vide" ||
            ReadHandlerType(movie, tracks[1]) != "soun")
        {
            throw MuxFailure("Media.InvalidMuxOutput", "The muxed MP4 does not contain one video and one audio track.");
        }

        var ids = tracks.Select(track => ReadTrackTiming(track).TrackId).ToArray();
        if (ids[0] == ids[1])
        {
            throw MuxFailure("Media.InvalidMuxOutput", "The muxed MP4 track identifiers are not unique.");
        }
    }

    private static MovieTiming ReadMovieTiming(MemoryBox movieHeader)
    {
        var content = movieHeader.Content(movieHeader.Bytes);
        if (content.Length < 20)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The movie header is truncated.");
        }

        return content[0] switch
        {
            0 when content.Length >= 100 => new MovieTiming(
                BinaryPrimitives.ReadUInt32BigEndian(content.Slice(12, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4))),
            1 when content.Length >= 112 => new MovieTiming(
                BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4)),
                BinaryPrimitives.ReadUInt64BigEndian(content.Slice(24, 8))),
            _ => throw MuxFailure("Media.InvalidIsoBmff", "The movie header version or length is unsupported.")
        };
    }

    private static TrackTiming ReadTrackTiming(MemoryBox track)
    {
        var trackHeader = RequireSingle(track.Children(track.Bytes), "tkhd");
        var content = trackHeader.Content(track.Bytes);
        if (content.Length < 24)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The track header is truncated.");
        }

        return content[0] switch
        {
            0 when content.Length >= 84 => new TrackTiming(
                BinaryPrimitives.ReadUInt32BigEndian(content.Slice(12, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4))),
            1 when content.Length >= 96 => new TrackTiming(
                BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4)),
                BinaryPrimitives.ReadUInt64BigEndian(content.Slice(28, 8))),
            _ => throw MuxFailure("Media.InvalidIsoBmff", "The track header version or length is unsupported.")
        };
    }

    private static uint ReadMediaTimescale(MemoryBox track)
    {
        var media = RequireSingle(track.Children(track.Bytes), "mdia");
        var mediaHeader = RequireSingle(media.Children(track.Bytes), "mdhd");
        var content = mediaHeader.Content(track.Bytes);
        var timescale = content.Length switch
        {
            >= 24 when content[0] == 0 => BinaryPrimitives.ReadUInt32BigEndian(content.Slice(12, 4)),
            >= 36 when content[0] == 1 => BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4)),
            _ => throw MuxFailure("Media.InvalidIsoBmff", "The media header version or length is unsupported.")
        };
        return timescale == 0
            ? throw MuxFailure("Media.InvalidIsoBmff", "An MP4 media timescale is zero.")
            : timescale;
    }

    private static byte[] PatchMovieHeader(byte[] boxBytes, ulong duration, uint nextTrackId)
    {
        var box = MemoryBox.ParseSingle(boxBytes);
        var output = (byte[])boxBytes.Clone();
        var contentOffset = box.HeaderSize;
        if (output[contentOffset] == 0)
        {
            if (duration > uint.MaxValue)
            {
                throw MuxFailure("Media.UnsupportedDuration", "The combined MP4 duration requires a version-1 movie header.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(contentOffset + 16, 4), (uint)duration);
        }
        else if (output[contentOffset] == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(contentOffset + 24, 8), duration);
        }
        else
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The movie header version is unsupported.");
        }

        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(output.Length - 4), nextTrackId);
        return output;
    }

    private static byte[] PatchTrackHeader(byte[] boxBytes, uint trackId, ulong duration)
    {
        var box = MemoryBox.ParseSingle(boxBytes);
        var output = (byte[])boxBytes.Clone();
        var contentOffset = box.HeaderSize;
        if (output[contentOffset] == 0)
        {
            if (duration > uint.MaxValue)
            {
                throw MuxFailure("Media.UnsupportedDuration", "The audio duration requires a version-1 track header.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(contentOffset + 12, 4), trackId);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(contentOffset + 20, 4), (uint)duration);
        }
        else if (output[contentOffset] == 1)
        {
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(contentOffset + 20, 4), trackId);
            BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(contentOffset + 28, 8), duration);
        }
        else
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The track header version is unsupported.");
        }

        return output;
    }

    private static ulong ScaleDuration(ulong duration, uint sourceTimescale, uint targetTimescale)
    {
        if (sourceTimescale == 0 || targetTimescale == 0)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "An MP4 timescale is zero.");
        }

        return checked((ulong)Math.Round(
            ((decimal)duration * targetTimescale) / sourceTimescale,
            MidpointRounding.AwayFromZero));
    }

    private static string ReadHandlerType(byte[] source, MemoryBox track)
    {
        var media = RequireSingle(track.Children(source), "mdia");
        var handler = RequireSingle(media.Children(source), "hdlr");
        var content = handler.Content(source);
        if (content.Length < 12)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "The MP4 handler box is truncated.");
        }

        return Encoding.ASCII.GetString(content.Slice(8, 4));
    }

    private static MemoryBox RequireSingle(IEnumerable<MemoryBox> boxes, string type)
    {
        var matches = boxes.Where(box => box.Type == type).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw MuxFailure("Media.InvalidIsoBmff", $"The MP4 must contain exactly one '{type}' box here.");
    }

    private static byte[] ReadBox(string path, IsoBmffBoxHeader header)
    {
        if (header.Size > MaximumMetadataBytes || header.Size > int.MaxValue)
        {
            throw MuxFailure("Media.InvalidIsoBmff", "An MP4 metadata box exceeds the safety limit.");
        }

        var bytes = new byte[(int)header.Size];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = header.Offset;
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] MakeBox(string type, ReadOnlySpan<byte> payload)
    {
        if (type.Length != 4)
        {
            throw new ArgumentException("An ISO BMFF box type must contain four characters.", nameof(type));
        }

        var length = checked(payload.Length + 8);
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)length));
        Encoding.ASCII.GetBytes(type, bytes.AsSpan(4, 4));
        payload.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static Mp4MuxException MuxFailure(string code, string detail) =>
        new(new TubeForgeError(code, detail));

    private static Result<Mp4MuxReceipt> Failure(string code, string message, Exception? exception = null) =>
        Result<Mp4MuxReceipt>.Failure(new TubeForgeError(code, message, exception?.GetType().Name));

    private sealed record Mp4Input(
        string Path,
        long Length,
        byte[] FileType,
        byte[] Movie,
        MemoryBox MovieBox,
        MemoryBox MovieHeader,
        MemoryBox Track,
        IReadOnlyList<IsoBmffBoxHeader> MediaData,
        bool IsFragmented,
        MemoryBox? MovieExtension,
        IReadOnlyList<InputFragment> Fragments,
        uint MediaTimescale);

    private readonly record struct MovieTiming(uint Timescale, ulong Duration);

    private readonly record struct TrackTiming(uint TrackId, ulong Duration);

    private readonly record struct OffsetMapping(
        long SourceDataOffset,
        long DataLength,
        long OutputDataOffset,
        long OutputEnd);

    private sealed record InputFragment(
        IsoBmffBoxHeader Moof,
        IsoBmffBoxHeader Mdat,
        byte[] MoofBytes,
        ulong DecodeTime);

    private sealed record FragmentCandidate(
        string Path,
        InputFragment Fragment,
        uint Timescale,
        uint OutputTrackId,
        bool IsAudio,
        int SourceIndex);

    private sealed record OutputFragment(
        string Path,
        InputFragment Fragment,
        uint OutputTrackId,
        uint SequenceNumber,
        long OutputOffset);

    private sealed class FragmentCandidateComparer : IComparer<FragmentCandidate>
    {
        public static FragmentCandidateComparer Instance { get; } = new();

        public int Compare(FragmentCandidate? left, FragmentCandidate? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var leftScaled = new BigInteger(left.Fragment.DecodeTime) * right.Timescale;
            var rightScaled = new BigInteger(right.Fragment.DecodeTime) * left.Timescale;
            var timing = leftScaled.CompareTo(rightScaled);
            if (timing != 0)
            {
                return timing;
            }

            var trackOrder = left.IsAudio.CompareTo(right.IsAudio);
            return trackOrder != 0 ? trackOrder : left.SourceIndex.CompareTo(right.SourceIndex);
        }
    }

    private sealed class Mp4MuxException(TubeForgeError error) : Exception(error.Message)
    {
        public TubeForgeError Error { get; } = error;
    }

    private readonly record struct MemoryBox(string Type, int Offset, int Size, int HeaderSize, byte[] Bytes)
    {
        public static MemoryBox ParseSingle(byte[] bytes)
        {
            var boxes = ParseRange(bytes, 0, bytes.Length);
            return boxes.Count == 1 && boxes[0].Size == bytes.Length
                ? boxes[0]
                : throw MuxFailure("Media.InvalidIsoBmff", "An in-memory MP4 box is malformed.");
        }

        public IReadOnlyList<MemoryBox> Children(byte[] source) =>
            ParseRange(source, Offset + HeaderSize, Size - HeaderSize);

        public byte[] CopyBytes(byte[] source) => source.AsSpan(Offset, Size).ToArray();

        public ReadOnlySpan<byte> Content(byte[] source) =>
            source.AsSpan(Offset + HeaderSize, Size - HeaderSize);

        private static IReadOnlyList<MemoryBox> ParseRange(byte[] bytes, int offset, int length)
        {
            var boxes = new List<MemoryBox>();
            var end = checked(offset + length);
            var cursor = offset;
            while (cursor < end)
            {
                if (end - cursor < 8)
                {
                    throw MuxFailure("Media.InvalidIsoBmff", "A nested MP4 box header is truncated.");
                }

                var shortSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor, 4));
                var type = Encoding.ASCII.GetString(bytes, cursor + 4, 4);
                var headerSize = 8;
                ulong declaredSize = shortSize;
                if (shortSize == 1)
                {
                    if (end - cursor < 16)
                    {
                        throw MuxFailure("Media.InvalidIsoBmff", "A nested extended MP4 box header is truncated.");
                    }

                    declaredSize = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(cursor + 8, 8));
                    headerSize = 16;
                }

                if (type == "uuid")
                {
                    headerSize += 16;
                }

                var size = shortSize == 0 ? (ulong)(end - cursor) : declaredSize;
                if (size < (ulong)headerSize || size > int.MaxValue || size > (ulong)(end - cursor))
                {
                    throw MuxFailure("Media.InvalidIsoBmff", $"Nested box '{type}' has an invalid size.");
                }

                boxes.Add(new MemoryBox(type, cursor, (int)size, headerSize, bytes));
                cursor = checked(cursor + (int)size);
            }

            return boxes;
        }
    }
}
