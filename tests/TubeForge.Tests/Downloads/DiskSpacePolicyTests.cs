using TubeForge.Downloads;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DiskSpacePolicyTests
{
    [Test]
    public static void ForecastsDirectAndAdaptivePeakAdditionalSpace()
    {
        const long sourceBytes = 1_000_000_000;
        const long existingBytes = 250_000_000;

        var direct = DiskSpacePolicy.CalculateRequiredAdditionalBytes(sourceBytes, existingBytes, requiresMuxing: false);
        var adaptive = DiskSpacePolicy.CalculateRequiredAdditionalBytes(sourceBytes, existingBytes, requiresMuxing: true);
        var split = DiskSpacePolicy.CalculateRequiredAdditionalBytes(
            sourceBytes,
            existingBytes,
            requiresMuxing: true,
            additionalOutputCopies: 1);

        Assert.Equal(750_000_000 + DiskSpacePolicy.MinimumReserveBytes, direct);
        Assert.Equal(1_750_000_000 + DiskSpacePolicy.MinimumReserveBytes, adaptive);
        Assert.Equal(2_750_000_000 + DiskSpacePolicy.MinimumReserveBytes, split);
    }

    [Test]
    public static void HandlesUnknownLengthsCompletedSourcesAndOverflowSafely()
    {
        Assert.True(DiskSpacePolicy.CalculateRequiredAdditionalBytes(null, 0, false) is null);
        Assert.Equal(
            DiskSpacePolicy.MinimumReserveBytes,
            DiskSpacePolicy.CalculateRequiredAdditionalBytes(1024, 1024, false));
        Assert.Equal(
            long.MaxValue,
            DiskSpacePolicy.CalculateRequiredAdditionalBytes(long.MaxValue, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiskSpacePolicy.CalculateRequiredAdditionalBytes(0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiskSpacePolicy.CalculateRequiredAdditionalBytes(1, -1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiskSpacePolicy.CalculateRequiredAdditionalBytes(1, 0, false, 5));
    }

    [Test]
    public static void ChecksCurrentDestinationWithoutExposingItsPathInResult()
    {
        var destination = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", "forecast.mp4");
        var result = DiskSpacePolicy.Check(destination, 1024, 0, requiresMuxing: false);

        if (result.IsSuccess)
        {
            Assert.Equal(1024 + DiskSpacePolicy.MinimumReserveBytes, result.Value.RequiredAdditionalBytes);
            Assert.True(result.Value.AvailableBytes is > 0);
        }
        else
        {
            Assert.Equal("Download.InsufficientDiskSpace", result.Error?.Code);
            Assert.False(result.Error!.TechnicalDetail?.Contains(destination, StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Test]
    public static void ReturnsTypedRetryableFailureWhenAvailableSpaceIsTooLow()
    {
        var insufficient = DiskSpacePolicy.Evaluate(500, 499);
        var sufficient = DiskSpacePolicy.Evaluate(500, 500);
        var unknown = DiskSpacePolicy.Evaluate(500, null);

        Assert.False(insufficient.IsSuccess);
        Assert.Equal("Download.InsufficientDiskSpace", insufficient.Error?.Code);
        Assert.True(sufficient.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.False(unknown.Value.IsKnown);
    }
}
