namespace TubeForge.Downloads.Queue;

public sealed class DownloadQueueDispatcher
{
    public const int MinimumConcurrency = 1;
    public const int MaximumAllowedConcurrency = 4;

    private readonly HashSet<Guid> _activeItems = [];
    private readonly object _sync = new();
    private int _maximumConcurrency;

    public DownloadQueueDispatcher(int maximumConcurrency = 2)
    {
        MaximumConcurrency = maximumConcurrency;
    }

    public int MaximumConcurrency
    {
        get
        {
            lock (_sync)
            {
                return _maximumConcurrency;
            }
        }
        set
        {
            if (value is < MinimumConcurrency or > MaximumAllowedConcurrency)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Concurrency must be between {MinimumConcurrency} and {MaximumAllowedConcurrency}.");
            }

            lock (_sync)
            {
                _maximumConcurrency = value;
            }
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_sync)
            {
                return _activeItems.Count;
            }
        }
    }

    public bool TryStart(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("Queue item ID cannot be empty.", nameof(itemId));
        }

        lock (_sync)
        {
            if (_activeItems.Contains(itemId) || _activeItems.Count >= _maximumConcurrency)
            {
                return false;
            }

            return _activeItems.Add(itemId);
        }
    }

    public bool Complete(Guid itemId)
    {
        lock (_sync)
        {
            return _activeItems.Remove(itemId);
        }
    }

    public bool IsActive(Guid itemId)
    {
        lock (_sync)
        {
            return _activeItems.Contains(itemId);
        }
    }
}
