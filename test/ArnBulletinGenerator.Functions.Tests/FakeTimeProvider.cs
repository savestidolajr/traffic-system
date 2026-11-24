namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Deterministic <see cref="TimeProvider" /> for unit tests. Supports a fixed
///     <see cref="DateTimeOffset" /> via the parameterised constructor, or a mutable
///     clock via the parameterless constructor and <see cref="SetUtcNow" />.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider() : this(DateTimeOffset.UtcNow) { }

    public FakeTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public void SetUtcNow(DateTimeOffset value) => _now = value;

    public override DateTimeOffset GetUtcNow() => _now;
}
