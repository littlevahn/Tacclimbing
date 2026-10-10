namespace Tacc.Api.Authentication;

/// <summary>Per-worker, bounded fixed-window throttling; no shared or persistent state.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int AttemptsPerMinute = 5;
    public const int MaximumSources = 1024;
    public const int MaximumConcurrentChecks = 2;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly object gate = new();
    private readonly Dictionary<string, (DateTimeOffset Start, int Attempts)> sources = new(StringComparer.Ordinal);
    private int activeChecks;

    public IDisposable? TryAcquire(string source, out int retryAfter)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var key in sources.Where(entry => now - entry.Value.Start >= Window)
                         .Select(entry => entry.Key).ToArray()) sources.Remove(key);
            retryAfter = 1;
            if (!sources.TryGetValue(source, out var entry))
            {
                if (sources.Count >= MaximumSources) { retryAfter = 60; return null; }
                entry = (now, 0);
            }
            if (entry.Attempts >= AttemptsPerMinute)
            {
                retryAfter = Math.Max(1, (int)Math.Ceiling((entry.Start + Window - now).TotalSeconds));
                return null;
            }
            sources[source] = (entry.Start, entry.Attempts + 1);
            if (activeChecks >= MaximumConcurrentChecks) return null;
            activeChecks++;
            return new Lease(this);
        }
    }

    private sealed class Lease(LoginThrottle owner) : IDisposable
    {
        private LoginThrottle? owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref owner, null);
            if (current is not null) lock (current.gate) current.activeChecks--;
        }
    }
}
