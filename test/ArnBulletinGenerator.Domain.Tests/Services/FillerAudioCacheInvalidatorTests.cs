using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class FillerAudioCacheInvalidatorTests
{
    private readonly IFillerAudioCacheService _cacheService = Substitute.For<IFillerAudioCacheService>();

    public FillerAudioCacheInvalidatorTests()
    {
        _cacheService.InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    // ── Text change triggers invalidation ────────────────────────────────

    [Fact]
    public async Task TextChanged_StatementIsInvalidated()
    {
        var old = new[] { new FillerStatement("s1", "Old text") };
        var @new = new[] { new FillerStatement("s1", "New text") };

        await FillerAudioCacheInvalidator.InvalidateChangedAsync(
            _cacheService, old, @new, CancellationToken.None);

        await _cacheService.Received(1).InvalidateAsync("s1", Arg.Any<CancellationToken>());
    }

    // ── Unchanged statement is NOT invalidated ───────────────────────────

    [Fact]
    public async Task UnchangedStatement_IsNotInvalidated()
    {
        var old = new[] { new FillerStatement("s1", "Same text") };
        var @new = new[] { new FillerStatement("s1", "Same text") };

        await FillerAudioCacheInvalidator.InvalidateChangedAsync(
            _cacheService, old, @new, CancellationToken.None);

        await _cacheService.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Removed statement triggers invalidation ──────────────────────────

    [Fact]
    public async Task RemovedStatement_IsInvalidated()
    {
        var old = new[] { new FillerStatement("s1", "Kept"), new FillerStatement("s2", "Removed") };
        var @new = new[] { new FillerStatement("s1", "Kept") };

        await FillerAudioCacheInvalidator.InvalidateChangedAsync(
            _cacheService, old, @new, CancellationToken.None);

        await _cacheService.Received(1).InvalidateAsync("s2", Arg.Any<CancellationToken>());
        await _cacheService.DidNotReceive().InvalidateAsync("s1", Arg.Any<CancellationToken>());
    }

    // ── Enabled-only change does NOT trigger invalidation ────────────────

    [Fact]
    public async Task EnabledOnlyChange_IsNotInvalidated_TextUntouched()
    {
        var old = new[] { new FillerStatement("s1", "Some text", Enabled: true) };
        var @new = new[] { new FillerStatement("s1", "Some text", Enabled: false) };

        await FillerAudioCacheInvalidator.InvalidateChangedAsync(
            _cacheService, old, @new, CancellationToken.None);

        await _cacheService.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
