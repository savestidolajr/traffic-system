using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.ContentSafety;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.ContentSafety;

public class ConfigurableContentSafetyFilterTests
{
    private static ConfigurableContentSafetyFilter CreateFilter(
        List<string>? bannedWords = null,
        Dictionary<string, string>? substitutions = null,
        List<string>? allowedPhrases = null,
        IReadOnlyList<SafetyTerm>? repoTerms = null,
        bool repoFails = false)
    {
        var options = new ContentSafetyOptions
        {
            BannedWords = bannedWords ?? [],
            Substitutions = substitutions ?? new(),
            AllowedPhrases = allowedPhrases ?? []
        };
        return new ConfigurableContentSafetyFilter(
            Options.Create(options),
            new FakeSafetyTermRepository(repoTerms, repoFails),
            NullLogger<ConfigurableContentSafetyFilter>.Instance);
    }

    private static SafetyTerm Banned(string term, SafetyScope scope = SafetyScope.Global, string? scopeId = null)
        => SafetyTerm.Create(SafetyTermKind.Banned, scope, scopeId, term, "test", DateTime.UtcNow);

    private static SafetyTerm Allowed(string term, SafetyScope scope = SafetyScope.Global, string? scopeId = null)
        => SafetyTerm.Create(SafetyTermKind.Allowed, scope, scopeId, term, "test", DateTime.UtcNow);

    // ── Config baseline (unchanged behaviour) ────────────────────────

    [Fact]
    public async Task CheckAsync_NoRules_ReturnsAllowed()
    {
        var filter = CreateFilter();
        var result = await filter.CheckAsync("Good morning, here is your traffic update.", null);
        result.IsAllowed.ShouldBeTrue();
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckAsync_BannedWord_ReturnsBlocked()
    {
        var filter = CreateFilter(bannedWords: ["crash"]);
        var result = await filter.CheckAsync("There was a crash on the M1.", null);
        result.IsAllowed.ShouldBeFalse();
        result.Violations.ShouldContain(v => v.Contains("crash"));
    }

    [Fact]
    public async Task CheckAsync_BannedWord_CaseInsensitive()
    {
        var filter = CreateFilter(bannedWords: ["crash"]);
        var result = await filter.CheckAsync("A CRASH occurred near the bridge.", null);
        result.IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAsync_BannedWord_ExactMatch_NotPartial()
    {
        var filter = CreateFilter(bannedWords: ["crash"]);
        var result = await filter.CheckAsync("The crashbar was installed.", null);
        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_Substitution_ReturnsWarning()
    {
        var filter = CreateFilter(substitutions: new() { ["accident"] = "incident" });
        var result = await filter.CheckAsync("There was an accident on the freeway.", null);
        result.IsAllowed.ShouldBeTrue();
        result.Warnings.ShouldContain(w => w.Contains("accident") && w.Contains("incident"));
    }

    [Fact]
    public async Task CheckAsync_NoMatch_EmptyWarnings()
    {
        var filter = CreateFilter(
            bannedWords: ["crash"],
            substitutions: new() { ["accident"] = "incident" });
        var result = await filter.CheckAsync("Traffic is flowing smoothly.", null);
        result.IsAllowed.ShouldBeTrue();
        result.Violations.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    // ── Repository-backed terms (union global ∪ market) ───────────────

    [Fact]
    public async Task CheckAsync_RepositoryBannedTerm_Blocks_EvenWithEmptyConfig()
    {
        var filter = CreateFilter(repoTerms: [Banned("gridlock")]);
        var result = await filter.CheckAsync("There's total gridlock on the M1.", null);
        result.IsAllowed.ShouldBeFalse();
        result.Violations.ShouldContain(v => v.Contains("gridlock"));
    }

    [Fact]
    public async Task CheckAsync_MarketScopedTerm_BlocksOnlyThatMarket()
    {
        var filter = CreateFilter(repoTerms: [Banned("chaos", SafetyScope.Market, "syd-1")]);

        (await filter.CheckAsync("Absolute chaos at the Harbour Bridge.", "syd-1"))
            .IsAllowed.ShouldBeFalse();
        // A different market only sees Global terms — the syd-1 term must not apply.
        (await filter.CheckAsync("Absolute chaos at the Harbour Bridge.", "mel-1"))
            .IsAllowed.ShouldBeTrue();
        // A global/ad-hoc bulletin (no market) likewise must not be blocked by a market term.
        (await filter.CheckAsync("Absolute chaos at the Harbour Bridge.", null))
            .IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_GlobalTerm_AppliesToEveryMarket()
    {
        var filter = CreateFilter(repoTerms: [Banned("gridlock")]);
        (await filter.CheckAsync("Total gridlock everywhere.", "mel-1")).IsAllowed.ShouldBeFalse();
        (await filter.CheckAsync("Total gridlock everywhere.", "syd-1")).IsAllowed.ShouldBeFalse();
    }

    // ── Allowed exceptions (place-name false positives) ───────────────

    [Fact]
    public async Task CheckAsync_AllowedPhrase_MasksPlaceName_ButGenuineUseStillBlocks()
    {
        var filter = CreateFilter(bannedWords: ["gun"], allowedPhrases: ["Gun Club Road"]);

        // The road name is masked before the banned-word scan → not a false positive.
        (await filter.CheckAsync("Roadworks on Gun Club Road have lanes closed.", null))
            .IsAllowed.ShouldBeTrue();
        // A genuine use of the banned word is still blocked.
        (await filter.CheckAsync("Police are responding to a man with a gun.", null))
            .IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAsync_RepositoryAllowedException_MasksFalsePositive()
    {
        var filter = CreateFilter(
            repoTerms: [Banned("gun"), Allowed("Gun Club Road")]);
        (await filter.CheckAsync("Heavy traffic near Gun Club Road this morning.", null))
            .IsAllowed.ShouldBeTrue();
    }

    // ── Fail-safe: store outage degrades to the config baseline ───────

    [Fact]
    public async Task CheckAsync_StoreUnavailable_EnforcesConfigBaseline_NotFailOpen()
    {
        var filter = CreateFilter(bannedWords: ["crash"], repoFails: true);
        var result = await filter.CheckAsync("There was a crash on the M1.", null);
        result.IsAllowed.ShouldBeFalse();
        result.Warnings.ShouldContain(w => w.Contains("store unavailable", StringComparison.OrdinalIgnoreCase));
    }

    // ── Constructor warnings ─────────────────────────────────────────

    [Fact]
    public void Constructor_NoBannedWords_LogsWarning()
    {
        var recorder = new RecordingLogger<ConfigurableContentSafetyFilter>();
        _ = new ConfigurableContentSafetyFilter(
            Options.Create(new ContentSafetyOptions()),
            new FakeSafetyTermRepository(),
            recorder);
        recorder.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("BannedWords", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Constructor_WithBannedWords_DoesNotWarnAboutEmptyBaseline()
    {
        var recorder = new RecordingLogger<ConfigurableContentSafetyFilter>();
        _ = new ConfigurableContentSafetyFilter(
            Options.Create(new ContentSafetyOptions { BannedWords = ["crash"] }),
            new FakeSafetyTermRepository(),
            recorder);
        recorder.Entries.ShouldNotContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("BannedWords", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CheckAsync_CancelledToken_DoesNotThrow()
    {
        var filter = CreateFilter(bannedWords: ["crash"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await filter.CheckAsync("There was a crash on the M1.", null, cts.Token);
        result.IsAllowed.ShouldBeFalse();
        result.Violations.ShouldNotBeEmpty();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
