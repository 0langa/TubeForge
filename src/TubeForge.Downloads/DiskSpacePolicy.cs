using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Downloads;

public static class DiskSpacePolicy
{
    public const long MinimumReserveBytes = 64L * 1024 * 1024;

    public static long? CalculateRequiredAdditionalBytes(
        long? expectedSourceBytes,
        long existingSourceBytes,
        bool requiresMuxing,
        int additionalOutputCopies = 0)
    {
        if (expectedSourceBytes is null)
        {
            return null;
        }

        if (expectedSourceBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSourceBytes));
        }

        if (existingSourceBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(existingSourceBytes));
        }

        if (additionalOutputCopies is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(additionalOutputCopies));
        }

        try
        {
            var remainingSource = Math.Max(0, expectedSourceBytes.Value - existingSourceBytes);
            var outputCopies = (requiresMuxing ? 1 : 0) + additionalOutputCopies;
            var muxOutput = checked(expectedSourceBytes.Value * outputCopies);
            var reserve = Math.Max(MinimumReserveBytes, expectedSourceBytes.Value / 20);
            return checked(remainingSource + muxOutput + reserve);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    public static Result<DiskSpaceForecast> Check(
        string destinationPath,
        long? expectedSourceBytes,
        long existingSourceBytes,
        bool requiresMuxing,
        int additionalOutputCopies = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var required = CalculateRequiredAdditionalBytes(
            expectedSourceBytes,
            existingSourceBytes,
            requiresMuxing,
            additionalOutputCopies);
        if (required is null)
        {
            return Evaluate(0, availableBytes: null);
        }

        try
        {
            var fullPath = Path.GetFullPath(destinationPath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return Evaluate(required.Value, availableBytes: null);
            }

            var available = new DriveInfo(root).AvailableFreeSpace;
            return Evaluate(required.Value, available);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return Evaluate(required.Value, availableBytes: null);
        }
    }

    public static Result<DiskSpaceForecast> Evaluate(long requiredAdditionalBytes, long? availableBytes)
    {
        if (requiredAdditionalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredAdditionalBytes));
        }

        if (availableBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableBytes));
        }

        if (availableBytes is not null && availableBytes.Value < requiredAdditionalBytes)
        {
            return Result<DiskSpaceForecast>.Failure(new TubeForgeError(
                "Download.InsufficientDiskSpace",
                "The destination does not have enough free space for this download.",
                $"Required additional bytes: {requiredAdditionalBytes}; available bytes: {availableBytes.Value}."));
        }

        return Result<DiskSpaceForecast>.Success(new DiskSpaceForecast
        {
            RequiredAdditionalBytes = requiredAdditionalBytes,
            AvailableBytes = availableBytes
        });
    }
}
