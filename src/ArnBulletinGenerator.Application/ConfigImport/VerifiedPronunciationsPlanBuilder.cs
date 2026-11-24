using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Models;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans the per-market replace-list VerifiedPronunciations config section.</summary>
internal static class VerifiedPronunciationsPlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<ConfigVerifiedPronunciationsDto> dtos, string actor,
        List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyDictionary<string, IReadOnlyList<VerifiedPronunciation>>? existingByMarket = null)
    {
        var seenMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var market = dto.Market?.Trim() ?? string.Empty;
            if (!StorageKey.IsValid(market))
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    "Market is not a valid identifier."));
                continue;
            }

            if (!seenMarkets.Add(market))
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    "Each market may appear only once."));
                continue;
            }

            var incoming = dto.Words;
            if (incoming is null)
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    "Words must be supplied as an array."));
                continue;
            }

            if (incoming.Count > ConfigValidationLimits.MaxVerifiedPronunciations)
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    $"At most {ConfigValidationLimits.MaxVerifiedPronunciations} verified pronunciations per market (found {incoming.Count})."));
                continue;
            }

            if (incoming.Any(w => w is null || string.IsNullOrWhiteSpace(w.Word)))
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    "Every verified pronunciation needs a word."));
                continue;
            }

            if (incoming.Any(w => w!.Word.Trim().Length > ConfigValidationLimits.MaxVerifiedPronunciationLength))
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    $"Words must be at most {ConfigValidationLimits.MaxVerifiedPronunciationLength} characters."));
                continue;
            }

            if (incoming.Any(w => w!.Note is { Length: > ConfigValidationLimits.MaxVerifiedPronunciationNoteLength }))
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    $"Notes must be at most {ConfigValidationLimits.MaxVerifiedPronunciationNoteLength} characters."));
                continue;
            }

            var duplicates = incoming
                .GroupBy(w => VerifiedPronunciation.NormalizeWord(w!.Word.Trim()), StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, market,
                    $"Each word may appear once. Duplicated: {string.Join(", ", duplicates)}."));
                continue;
            }

            var words = new List<VerifiedPronunciation>(incoming.Count);
            var parseError = false;
            foreach (var dtoWord in incoming)
            {
                var word = dtoWord!;
                DateTime verifiedAt = default;
                if (!string.IsNullOrWhiteSpace(word.VerifiedAt) &&
                    !DateTime.TryParse(word.VerifiedAt, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out verifiedAt))
                {
                    items.Add(ErrorItem(ConfigSections.VerifiedPronunciations, market, word.Word,
                        "VerifiedAt must be a valid ISO date-time."));
                    parseError = true;
                    break;
                }

                words.Add(new VerifiedPronunciation(
                    word.Word.Trim(),
                    string.IsNullOrWhiteSpace(word.Note) ? null : word.Note.Trim(),
                    word.Enabled,
                    string.IsNullOrWhiteSpace(word.VerifiedBy) ? null : word.VerifiedBy.Trim(),
                    verifiedAt));
            }

            if (parseError)
                continue;

            var existing = existingByMarket is not null && existingByMarket.TryGetValue(market, out var snapshotWords)
                ? Result<IReadOnlyList<VerifiedPronunciation>>.Success(snapshotWords)
                : await ctx.VerifiedPronunciations.GetForMarketAsync(market, ct);
            if (existing.IsFailure)
                return Result.Failure($"Failed to read verified pronunciations for '{market}': {existing.Error}");

            if (SetEquals(existing.Value.Select(WordKey), words.Select(WordKey)))
            {
                items.Add(UnchangedItem(ConfigSections.VerifiedPronunciations, market,
                    $"{market} ({words.Count} words)"));
                continue;
            }

            var action = existing.Value.Count == 0 ? ConfigImportActions.Create : ConfigImportActions.Update;
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.VerifiedPronunciations, market,
                    $"{market} ({words.Count} words)", action),
                cancellationToken => ctx.VerifiedPronunciations.SetForMarketAsync(
                    market, words, actor, cancellationToken)));
        }

        return Result.Success();

        static string WordKey(VerifiedPronunciation word) =>
            $"{word.Word.Trim()}|{word.Note?.Trim() ?? ""}|{word.Enabled}|{word.VerifiedBy?.Trim() ?? ""}|{(word.VerifiedAtUtc == default ? "" : word.VerifiedAtUtc.ToUniversalTime().Ticks)}";
    }
}
