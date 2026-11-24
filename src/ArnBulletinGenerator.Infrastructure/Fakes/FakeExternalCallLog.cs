using System.Collections.Concurrent;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>Thread-safe, timestamped record of every fake external-provider call made during an ARN_E2E run, for test assertions.</summary>
public sealed class FakeExternalCallLog
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public void Add(string message) => Entries.Enqueue($"{DateTimeOffset.UtcNow:o} {message}");
}
