using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class QueueItemArtifactsTests
{
    private const string Guid32 = "0123456789abcdef0123456789abcdef";

    [Test]
    public static void DeletesEveryArtifactTheItemOwnsAndNothingElse()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "Video [abc].mp4");

        string[] owned =
        [
            "Video [abc].mp4.part",
            "Video [abc].mp4.part.json",
            "Video [abc].mp4.part.segments.json",
            "Video [abc].mp4.video-track.mp4",
            "Video [abc].mp4.audio-track.m4a.part",
            "Video [abc].mp4.audio-track.m4a.part.segments.json",
            "Video [abc].mp4.source.webm",
            "Video [abc].mp4.source.webm.part.json",
            "Video [abc].mp4.chapter-source.mp4",
            "Video [abc].mp4.caption-source.mp4",
            "Video [abc].mp4.trim-source.mp4",
            "Video [abc].mp4.caption-source.srt",
            "Video [abc].mp4.caption-2-source.srt.part",
            "Video [abc].mp4.hls-source",
            "Video [abc].mp4.hls-source.hls.json",
            "Video [abc].mp4.hls-source.hls.json.bak",
            $"Video [abc].mp4.{Guid32}.processing.mp4",
            $"Video [abc].mp4.trim-source.mp4.{Guid32}.processing.mp4",
            $"Video [abc].mp4.{Guid32}.transcoding.mp3",
            $"Video [abc].mp4.{Guid32}.chapters.ffmetadata",
        ];
        string[] kept =
        [
            "Video [abc].mp4",                  // the output itself
            "Video [abc] (1).mp4.part",         // another item's download
            "Video [abc].mp4.notes.txt",        // the user's own file
            "Video [abc].mp4.part.bak",         // not a name TubeForge writes
            "Video [abc].mp4.jpg",
            "Other.mp4.part",
            $"Video [abc].mp4.{Guid32}.processing",
            $"{Guid32}.probe.ffmetadata",       // cannot be tied to one item
        ];
        foreach (var name in owned.Concat(kept))
        {
            File.WriteAllText(Path.Combine(directory.Path, name), name);
        }

        var parts = Directory.CreateDirectory(Path.Combine(directory.Path, "Video [abc].mp4.hls-source.hls.parts"));
        File.WriteAllText(Path.Combine(parts.FullName, "00000000000000000001.bin"), "segment");
        File.WriteAllText(Path.Combine(parts.FullName, "00000000000000000002.bin.new"), "segment");
        var chapters = Directory.CreateDirectory(Path.Combine(directory.Path, "Video [abc] - chapters"));
        File.WriteAllText(Path.Combine(chapters.FullName, "01 Intro.mp4"), "chapter");

        var cleanup = QueueItemArtifacts.Delete(destination);

        Assert.Equal(owned.Length + 1, cleanup.Deleted);
        Assert.Equal(0, cleanup.Failed.Count);
        foreach (var name in owned)
        {
            Assert.False(File.Exists(Path.Combine(directory.Path, name)), $"{name} should have been deleted.");
        }

        Assert.False(Directory.Exists(parts.FullName), "the live-capture segment folder belongs to the item");
        foreach (var name in kept)
        {
            Assert.True(File.Exists(Path.Combine(directory.Path, name)), $"{name} must be left alone.");
        }

        Assert.True(File.Exists(Path.Combine(chapters.FullName, "01 Intro.mp4")), "split chapters are finished output");
    }

    [Test]
    public static void DeletesTheChainedLeftoversOfCombinedSteps()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "Clip.mp4");

        // Names the app builds when steps combine: captions or chapters plus a trim,
        // a video conversion that fetches separate tracks, and FFmpeg scratch files
        // next to an intermediate.
        string[] owned =
        [
            "Clip.mp4.caption-source.mp4.part",
            "Clip.mp4.caption-source.mp4.trim-source.mp4.part.segments.json",
            "Clip.mp4.chapter-source.mp4.video-track.webm.part",
            "Clip.mp4.chapter-source.mp4.audio-track.webm",
            "Clip.mp4.source.webm.video-track.webm.part.json",
            "Clip.mp4.caption-source.mp4.source.webm.audio-track.m4a.part",
            $"Clip.mp4.caption-source.mp4.{Guid32}.transcoding.mp4",
        ];
        string[] kept =
        [
            "Clip.mp4",
            "Clip.mp4.source.webm.notes.txt",           // ends in a name the app never writes
            "Clip.mp4.caption-source.mp4.final.mp4",
            "Clip.mp4.part.video-track.webm",           // a partial suffix only ever comes last
            "Clip.mp4.video-track.webm.part.part",
        ];
        foreach (var name in owned.Concat(kept))
        {
            File.WriteAllText(Path.Combine(directory.Path, name), name);
        }

        var cleanup = QueueItemArtifacts.Delete(destination);

        Assert.Equal(owned.Length, cleanup.Deleted);
        Assert.Equal(0, cleanup.Failed.Count);
        foreach (var name in owned)
        {
            Assert.False(File.Exists(Path.Combine(directory.Path, name)), $"{name} should have been deleted.");
        }

        foreach (var name in kept)
        {
            Assert.True(File.Exists(Path.Combine(directory.Path, name)), $"{name} must be left alone.");
        }
    }

    [Test]
    public static void LeavesASegmentFolderHoldingAnythingUnexpected()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "Live.mp4");
        var parts = Directory.CreateDirectory(Path.Combine(directory.Path, "Live.mp4.hls-source.hls.parts"));
        File.WriteAllText(Path.Combine(parts.FullName, "00000000000000000001.bin"), "segment");
        File.WriteAllText(Path.Combine(parts.FullName, "keep-me.txt"), "not ours");
        Directory.CreateDirectory(Path.Combine(parts.FullName, "nested"));

        var found = QueueItemArtifacts.Find(destination);
        var cleanup = QueueItemArtifacts.Delete(destination);

        Assert.Equal(0, found.Count);
        Assert.Equal(0, cleanup.Deleted);
        Assert.True(File.Exists(Path.Combine(parts.FullName, "keep-me.txt")));
        Assert.True(File.Exists(Path.Combine(parts.FullName, "00000000000000000001.bin")));
    }

    [Test]
    public static void MissingFolderOrInvalidDestinationFindsNothing()
    {
        using var directory = new TestDirectory();

        Assert.Equal(0, QueueItemArtifacts.Find(Path.Combine(directory.Path, "gone", "Video.mp4")).Count);
        Assert.Equal(0, QueueItemArtifacts.Find(string.Empty).Count);
        Assert.Equal(0, QueueItemArtifacts.Delete(Path.Combine(directory.Path, "gone", "Video.mp4")).Deleted);
    }

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

        public TestDirectory()
        {
            Path = System.IO.Path.Combine(SafeRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(SafeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean a test directory outside the safe root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
