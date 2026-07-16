using System.Security.Cryptography;
using System.Text;

namespace TubeForge.YouTube.Player;

internal sealed class PlayerTransformCache(int capacity = 64)
{
    private readonly object gate = new();
    private readonly Dictionary<string, CacheEntry> plans = new(StringComparer.Ordinal);
    private readonly Queue<(string Hash, long Generation)> insertionOrder = new();
    private readonly int capacity = Math.Clamp(capacity, 1, 256);
    private long generation;

    public bool TryGet(string playerScript, out PlayerTransformPlans plan)
    {
        var hash = Hash(playerScript);
        lock (gate)
        {
            if (plans.TryGetValue(hash, out var entry))
            {
                plan = entry.Plans;
                return true;
            }
        }

        plan = null!;
        return false;
    }

    public void Store(string playerScript, PlayerTransformPlans plan)
    {
        ArgumentNullException.ThrowIfNull(playerScript);
        ArgumentNullException.ThrowIfNull(plan);
        var hash = Hash(playerScript);
        lock (gate)
        {
            if (plans.TryGetValue(hash, out var existing))
            {
                plans[hash] = existing with { Plans = plan };
                return;
            }

            var currentGeneration = ++generation;
            plans.Add(hash, new CacheEntry(plan, currentGeneration));
            insertionOrder.Enqueue((hash, currentGeneration));
            while (plans.Count > capacity && insertionOrder.TryDequeue(out var oldest))
            {
                if (plans.TryGetValue(oldest.Hash, out var entry) &&
                    entry.Generation == oldest.Generation)
                {
                    plans.Remove(oldest.Hash);
                }
            }
        }
    }

    public void Remove(string playerScript)
    {
        var hash = Hash(playerScript);
        lock (gate)
        {
            plans.Remove(hash);
        }
    }

    internal static string Hash(string playerScript)
    {
        ArgumentNullException.ThrowIfNull(playerScript);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(playerScript)));
    }

    private sealed record CacheEntry(PlayerTransformPlans Plans, long Generation);
}
