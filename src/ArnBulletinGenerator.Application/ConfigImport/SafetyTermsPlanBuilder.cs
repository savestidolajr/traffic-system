using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for the SafetyTerms config section.</summary>
internal static class SafetyTermsPlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<ConfigSafetyTermDto> dtos, string actor, List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyList<SafetyTerm>? existingTerms = null)
    {
        var existingResult = existingTerms is not null
            ? Result<IReadOnlyList<SafetyTerm>>.Success(existingTerms)
            : await ctx.SafetyTerms.ListAllAsync(ct);
        if (existingResult.IsFailure)
            return Result.Failure($"Failed to list safety terms: {existingResult.Error}");
        var existing = existingResult.Value;

        // Two entries sharing an explicit id would both plan against the existing library and the
        // second apply would upsert over the first — reject at plan time.
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            // Stable, bundle-derived identity for every row this builder reports, error or not:
            // whitespace-only ids must not win over the term (`??` let them), because the approve
            // gate compares report keys across two plans.
            var key = string.IsNullOrWhiteSpace(dto.Id)
                ? (string.IsNullOrWhiteSpace(dto.Term) ? "(unnamed)" : dto.Term.Trim())
                : dto.Id.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Id) && !seenIds.Add(dto.Id.Trim()))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term,
                    "Each safety term id may appear only once."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Term))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term, "Term is required."));
                continue;
            }

            if (dto.Term.Length > ConfigValidationLimits.MaxTermLength)
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term,
                    $"Term must be at most {ConfigValidationLimits.MaxTermLength} characters."));
                continue;
            }

            if (!Enum.TryParse<SafetyTermKind>(dto.Kind, true, out var kind))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term, "Kind must be Banned or Allowed."));
                continue;
            }

            if (!Enum.TryParse<SafetyScope>(dto.Scope, true, out var scope))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term, "Scope must be Global or Market."));
                continue;
            }

            string scopeId = SafetyTerm.NormalizeScopeId(scope, dto.ScopeId);
            if (scope != SafetyScope.Global && string.IsNullOrWhiteSpace(scopeId))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term,
                    "ScopeId (market) is required for Market scope."));
                continue;
            }

            if (scope != SafetyScope.Global && !StorageKey.IsValid(scopeId))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term, "ScopeId is not a valid identifier."));
                continue;
            }

            var byId = string.IsNullOrWhiteSpace(dto.Id)
                ? null
                : existing.FirstOrDefault(t => string.Equals(t.Id, dto.Id, StringComparison.OrdinalIgnoreCase));
            var match = byId ?? existing.FirstOrDefault(t =>
                t.Kind == kind && t.Scope == scope
                && string.Equals(t.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Term, dto.Term.Trim(), StringComparison.OrdinalIgnoreCase));

            if (byId is not null && (byId.Kind != kind || byId.Scope != scope
                                     || !string.Equals(byId.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(ErrorItem(ConfigSections.SafetyTerms, key, dto.Term,
                    "Changing a term's kind/scope is not supported by import — delete and recreate it instead."));
                continue;
            }

            var now = ctx.TimeProvider.GetUtcNow().UtcDateTime;
            if (match is null)
            {
                var term = SafetyTerm.Create(kind, scope, scopeId, dto.Term, actor, now, dto.Note, dto.Enabled);
                // The report key MUST be derived from the bundle, never from the entity the plan
                // just minted. `ConfigImportReportDto.IsEquivalentTo` compares the
                // (section, key, action) multiset, and the approve gate re-plans before applying:
                // a freshly minted GUID differs on every plan, so a bundle creating a safety term
                // was ALWAYS refused with "was Create, now absent; was absent, now Create" — no
                // real drift, and unfixable by re-submitting. That made adding a banned word by
                // config import impossible wherever RequireSecondApprover is on, which is prod
                // (deployment/main.prod.bicepparam). Every sibling builder already keys creates
                // off the bundle (dto.Id, or the market); this is the same rule, falling back to
                // the term because SafetyTerms is the one section whose entity id is server-minted.
                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.SafetyTerms, key, dto.Term.Trim(),
                        ConfigImportActions.Create),
                    ct => ctx.SafetyTerms.UpsertAsync(term, ct)));
                continue;
            }

            bool changed =
                !string.Equals(match.Term, dto.Term.Trim(), StringComparison.Ordinal)
                || match.Enabled != dto.Enabled
                || !DescriptionsEqual(match.Note.Match(n => (string?)n, () => null), dto.Note);

            if (!changed)
            {
                items.Add(UnchangedItem(ConfigSections.SafetyTerms, match.Id, match.Term));
                continue;
            }

            var updated = new SafetyTerm(match.Id, kind, scope, scopeId, dto.Term.Trim(), actor, now, now,
                dto.Enabled, Option.OfWhitespace(dto.Note));
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.SafetyTerms, match.Id, dto.Term.Trim(),
                    ConfigImportActions.Update),
                ct => ctx.SafetyTerms.UpsertAsync(updated, ct)));
        }

        return Result.Success();
    }
}
