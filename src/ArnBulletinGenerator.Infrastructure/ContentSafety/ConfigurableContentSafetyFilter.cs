using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.ContentSafety;

/// <summary>
///     Deterministic content-safety filter. Enforces the UNION of the engineered config
///     baseline (<c>ContentSafety:BannedWords</c>/<c>AllowedPhrases</c>) and the
///     repository-managed terms for the bulletin's scope (global ∪ market). Allowed
///     exceptions are masked out before the banned-word scan, so a banned substring inside a
///     legitimate place name is not a false positive. Never fails open: if the term store is
///     unavailable the config baseline is still enforced.
/// </summary>
public sealed class ConfigurableContentSafetyFilter : IContentSafetyFilter
{
    private readonly ContentSafetyOptions _options;
    private readonly ISafetyTermRepository _terms;
    private readonly ILogger<ConfigurableContentSafetyFilter> _logger;

    public ConfigurableContentSafetyFilter(
        IOptions<ContentSafetyOptions> options,
        ISafetyTermRepository terms,
        ILogger<ConfigurableContentSafetyFilter> logger)
    {
        _options = options.Value;
        _terms = terms;
        _logger = logger;

        // The config baseline is the immutable floor; repository terms supplement it. An empty
        // baseline relies entirely on the store — surface that so it is a deliberate choice.
        if (_options.BannedWords.Count == 0)
        {
            _logger.LogWarning(
                "Content safety baseline '{Section}:BannedWords' is empty. The deterministic filter relies " +
                "solely on repository-managed terms; configure the baseline floor before the pilot goes live.",
                ContentSafetyOptions.SectionName);
        }
    }

    public async Task<ContentSafetyResult> CheckAsync(
        string scriptText, string? market, CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        var warnings = new List<string>();

        // Substitutions are informational — reported as warnings.
        foreach (var (original, replacement) in _options.Substitutions)
        {
            if (scriptText.Contains(original, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Substitution suggested: '{original}' → '{replacement}'");
        }

        // Union: engineered baseline + repository terms (global ∪ market). A store failure
        // degrades to the baseline floor, never fail-open.
        var banned = new List<string>(_options.BannedWords);
        var allowed = new List<string>(_options.AllowedPhrases);
        var termsResult = await _terms.GetForMarketAsync(market, cancellationToken);
        if (termsResult.IsSuccess)
        {
            foreach (var t in termsResult.Value)
            {
                if (!t.Enabled) continue;
                if (t.Kind == SafetyTermKind.Banned) banned.Add(t.Term);
                else allowed.Add(t.Term);
            }
        }
        else
        {
            _logger.LogWarning(
                "Safety term store unavailable ({Error}); enforcing config baseline only.", termsResult.Error);
            warnings.Add("Safety term store unavailable — config baseline enforced.");
        }

        // Mask allowed exceptions so a banned substring inside a legitimate place name is not flagged.
        string scanText = scriptText;
        foreach (var phrase in allowed.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            scanText = Regex.Replace(
                scanText, Regex.Escape(phrase.Trim()), m => new string(' ', m.Length), RegexOptions.IgnoreCase);
        }

        foreach (var term in banned
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Select(t => t.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pattern = $@"\b{Regex.Escape(term)}\b";
            if (Regex.IsMatch(scanText, pattern, RegexOptions.IgnoreCase))
                violations.Add($"Banned word detected: '{term}'");
        }

        if (violations.Count > 0)
        {
            _logger.LogWarning("Content safety check failed with {Count} violation(s)", violations.Count);
            return ContentSafetyResult.Blocked([.. violations], [.. warnings]);
        }

        return new ContentSafetyResult(true, [], [.. warnings]);
    }
}
