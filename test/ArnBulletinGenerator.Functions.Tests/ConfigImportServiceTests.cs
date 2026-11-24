using NSubstitute;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ArnBulletinGenerator.Application.ConfigImport;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Infrastructure;
using ArnBulletinGenerator.Functions.ConfigTransfer;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class ConfigImportServiceTests
{

    /// <summary>
    ///     A playbook service with an empty library. An unconfigured NSubstitute proxy returns a
    ///     null Task, which the import path dereferences: the stub has to answer, not merely exist.
    /// </summary>
    private static IBulletinPlaybookService EmptyPlaybooks()
    {
        var service = Substitute.For<IBulletinPlaybookService>();
        service.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<BulletinPlaybook>>.Success([]));
        return service;
    }
    private static readonly DateTime Now = new(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
    private static VersionedLibraryListPage<T> Page<T>(
        IReadOnlyList<T> values, Func<T, string> nameSelector, VersionedLibraryListQuery query)
    {
        var ordered = values.OrderBy(nameSelector, StringComparer.OrdinalIgnoreCase).ToList();
        int offset = 0;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(query.Cursor));
            if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out offset) || offset < 0)
                throw new FormatException("Cursor offset is invalid.");
        }

        int pageSize = query.PageSize ?? Math.Max(1, ordered.Count);
        var items = offset >= ordered.Count
            ? []
            : ordered.Skip(offset).Take(pageSize).ToList();
        string? next = offset + items.Count < ordered.Count
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            : null;
        return new VersionedLibraryListPage<T>(items, ordered.Count, next);
    }

    private static IReadOnlyDictionary<string, Option<T>> GetMany<T>(
        IReadOnlyList<T> values, IReadOnlyCollection<string> ids, Func<T, string> idSelector)
        where T : class
    {
        var result = new Dictionary<string, Option<T>>(StringComparer.Ordinal);
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.Ordinal))
        {
            var match = values.FirstOrDefault(value =>
                string.Equals(idSelector(value), id, StringComparison.OrdinalIgnoreCase));
            result[id] = match is null ? Option<T>.None : Option<T>.Some(match);
        }
        return result;
    }


    // ── Fakes ────────────────────────────────────────────────────────

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryScheduleRepository : IScheduleRepository
    {
        private readonly List<MarketSchedule> _schedules = [];
        public IReadOnlyList<MarketSchedule> Schedules => _schedules;

        public Task<Result<IEnumerable<MarketSchedule>>> ListSchedulesAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success(_schedules));

        public Task<Result<MarketSchedule>> GetScheduleAsync(string marketId, CancellationToken ct = default)
        {
            var match = _schedules.FirstOrDefault(s =>
                string.Equals(s.MarketId, marketId, StringComparison.OrdinalIgnoreCase));
            return match is null
                ? Task.FromResult(Result<MarketSchedule>.Failure($"Schedule '{marketId}' not found."))
                : Task.FromResult(Result<MarketSchedule>.Success(match));
        }

        public Task<Result> SaveScheduleAsync(MarketSchedule schedule, CancellationToken ct = default)
        {
            _schedules.RemoveAll(s =>
                string.Equals(s.MarketId, schedule.MarketId, StringComparison.OrdinalIgnoreCase));
            _schedules.Add(schedule);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteScheduleAsync(string marketId, CancellationToken ct = default)
        {
            _schedules.RemoveAll(s =>
                string.Equals(s.MarketId, marketId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(DateTime thresholdUtc, CancellationToken ct = default) =>
            Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success([]));

        public Task<Result> TryStartJobAsync(string marketId, string jobId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> CompleteJobAsync(string marketId, Option<DateTime> nextRunUtc, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public void Add(MarketSchedule schedule) => _schedules.Add(schedule);
    }

    /// <summary>
    ///     Delegates to the REAL Cronos-backed parser so import validation is exercised against
    ///     genuine cron semantics. A stub returning <c>true</c> unconditionally would let an
    ///     unparseable expression through exactly as the unguarded plan builder used to, and a
    ///     stubbed next-run would hide the minimum-interval rule for the same reason: the floor is
    ///     measured by walking real occurrences, so a fixed 30-minute answer makes every cron look
    ///     compliant.
    /// </summary>
    private sealed class FakeCronParser : ICronParser
    {
        private static readonly CronParser Real = new();

        public bool TryParseCron(string expression) => Real.TryParseCron(expression);
        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone) =>
            Real.CalculateNextRun(cronExpression, timezone);
        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone, DateTime baseTimeUtc) =>
            Real.CalculateNextRun(cronExpression, timezone, baseTimeUtc);
    }

    private sealed class InMemoryVoiceProfileRepository : IVoiceProfileRepository
    {
        private readonly List<VoiceProfile> _profiles = [];
        public IReadOnlyList<VoiceProfile> Profiles => _profiles;

        public Task<Result<IReadOnlyList<VoiceProfile>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<VoiceProfile>>.Success(_profiles));
        public Task<Result<VersionedLibraryListPage<VoiceProfile>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<VersionedLibraryListPage<VoiceProfile>>.Success(
                Page(_profiles, profile => profile.Name, query)));

        public Task<Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>.Success(
                GetMany(_profiles, ids, profile => profile.Id)));


        public Task<Result<Option<VoiceProfile>>> GetAsync(string id, CancellationToken ct = default)
        {
            var match = _profiles.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match is null
                ? Result<Option<VoiceProfile>>.Success(Option<VoiceProfile>.None)
                : Result<Option<VoiceProfile>>.Success(Option<VoiceProfile>.Some(match)));
        }

        public Task<Result> SaveAsync(VoiceProfile profile, CancellationToken ct = default)
        {
            _profiles.RemoveAll(p =>
                string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            _profiles.Add(profile);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default)
        {
            _profiles.RemoveAll(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }


        public void Add(VoiceProfile profile) => _profiles.Add(profile);
    }

    private sealed class InMemoryDeliveryStyleRepository : IDeliveryStyleRepository
    {
        private readonly List<DeliveryStyle> _styles = [];
        public IReadOnlyList<DeliveryStyle> Styles => _styles;

        public Task<Result<IReadOnlyList<DeliveryStyle>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<DeliveryStyle>>.Success(_styles));
        public Task<Result<VersionedLibraryListPage<DeliveryStyle>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<VersionedLibraryListPage<DeliveryStyle>>.Success(
                Page(_styles, style => style.Name, query)));

        public Task<Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>.Success(
                GetMany(_styles, ids, style => style.Id)));


        public Task<Result<Option<DeliveryStyle>>> GetAsync(string id, CancellationToken ct = default)
        {
            var match = _styles.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match is null
                ? Result<Option<DeliveryStyle>>.Success(Option<DeliveryStyle>.None)
                : Result<Option<DeliveryStyle>>.Success(Option<DeliveryStyle>.Some(match)));
        }

        public bool FailNextSave { get; set; }

        public Task<Result> SaveAsync(DeliveryStyle style, CancellationToken ct = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                return Task.FromResult(Result.Failure("Simulated storage outage."));
            }

            _styles.RemoveAll(s =>
                string.Equals(s.Id, style.Id, StringComparison.OrdinalIgnoreCase));
            _styles.Add(style);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default)
        {
            _styles.RemoveAll(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public void Add(DeliveryStyle style) => _styles.Add(style);
    }

    private sealed class InMemoryPronunciationDictionaryRepository : IPronunciationDictionaryRepository
    {
        private readonly List<PronunciationDictionary> _dicts = [];
        public IReadOnlyList<PronunciationDictionary> Dictionaries => _dicts;

        public Task<Result<IReadOnlyList<PronunciationDictionary>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<PronunciationDictionary>>.Success(_dicts));
        public Task<Result<VersionedLibraryListPage<PronunciationDictionary>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<VersionedLibraryListPage<PronunciationDictionary>>.Success(
                Page(_dicts, dict => dict.Name, query)));

        public Task<Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>.Success(
                GetMany(_dicts, ids, dict => dict.Id)));


        public Task<Result<Option<PronunciationDictionary>>> GetAsync(string id, CancellationToken ct = default)
        {
            var match = _dicts.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match is null
                ? Result<Option<PronunciationDictionary>>.Success(Option<PronunciationDictionary>.None)
                : Result<Option<PronunciationDictionary>>.Success(Option<PronunciationDictionary>.Some(match)));
        }

        public Task<Result> SaveAsync(PronunciationDictionary dict, CancellationToken ct = default)
        {
            _dicts.RemoveAll(d =>
                string.Equals(d.Id, dict.Id, StringComparison.OrdinalIgnoreCase));
            _dicts.Add(dict);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default)
        {
            _dicts.RemoveAll(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public void Add(PronunciationDictionary dict) => _dicts.Add(dict);
    }

    private sealed class InMemorySafetyTermRepository : ISafetyTermRepository
    {
        private readonly List<SafetyTerm> _terms = [];
        public IReadOnlyList<SafetyTerm> Terms => _terms;

        public Task<Result<IReadOnlyList<SafetyTerm>>> GetForMarketAsync(string? market, CancellationToken ct = default)
        {
            var filtered = string.IsNullOrWhiteSpace(market)
                ? _terms.Where(t => t.Scope == SafetyScope.Global).ToList()
                : _terms.Where(t => t.Scope == SafetyScope.Global
                    || (t.Scope == SafetyScope.Market
                        && string.Equals(t.ScopeId, market.Trim().ToLowerInvariant(),
                            StringComparison.OrdinalIgnoreCase))).ToList();
            return Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Success(filtered));
        }

        public Task<Result<IReadOnlyList<SafetyTerm>>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Success(_terms));

        public Task<Result> UpsertAsync(SafetyTerm term, CancellationToken ct = default)
        {
            _terms.RemoveAll(t => string.Equals(t.Id, term.Id, StringComparison.OrdinalIgnoreCase));
            _terms.Add(term);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(SafetyScope scope, string scopeId, string id, CancellationToken ct = default)
        {
            _terms.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public void Add(SafetyTerm term) => _terms.Add(term);
    }

    private sealed class InMemoryGuidanceRepository : IGuidanceRepository
    {
        private readonly List<GuidanceEntry> _entries = [];
        public IReadOnlyList<GuidanceEntry> Entries => _entries;

        public Task<Result<IReadOnlyList<GuidanceEntry>>> GetForScopesAsync(
            string market, Option<string> station, CancellationToken ct = default)
        {
            var filtered = _entries.Where(e =>
                e.Scope == GuidanceScope.Global
                || (e.Scope == GuidanceScope.Market
                    && string.Equals(e.ScopeId, market.Trim(), StringComparison.OrdinalIgnoreCase))
                || (e.Scope == GuidanceScope.Station && station.HasValue
                    && string.Equals(e.ScopeId, station.Value.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
            return Task.FromResult(Result<IReadOnlyList<GuidanceEntry>>.Success(filtered));
        }

        public Task<Result<IReadOnlyList<GuidanceEntry>>> ListAsync(
            GuidanceScope scope, string scopeId, CancellationToken ct = default)
        {
            var filtered = _entries.Where(e =>
                e.Scope == scope
                && string.Equals(e.ScopeId, scopeId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult(Result<IReadOnlyList<GuidanceEntry>>.Success(filtered));
        }

        public Task<Result> UpsertAsync(GuidanceEntry entry, CancellationToken ct = default)
        {
            _entries.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            _entries.Add(entry);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<bool>> DeleteAsync(GuidanceScope scope, string scopeId, string id, CancellationToken ct = default)
        {
            bool removed = _entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            return Task.FromResult(Result<bool>.Success(removed));
        }

        public void Add(GuidanceEntry entry) => _entries.Add(entry);
    }

    private sealed class InMemoryVerifiedPronunciationRepository : IVerifiedPronunciationRepository
    {
        private readonly Dictionary<string, List<VerifiedPronunciation>> _words = new(StringComparer.OrdinalIgnoreCase);

        public Task<Result<IReadOnlyList<VerifiedPronunciation>>> GetForMarketAsync(
            string market, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<VerifiedPronunciation>>.Success(
                _words.TryGetValue(market, out var words) ? words : []));

        public Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success(
                _words.Keys.ToList() as IReadOnlyList<string>));

        public Task<Result> SetForMarketAsync(
            string market, IReadOnlyList<VerifiedPronunciation> words, string updatedBy,
            CancellationToken ct = default)
        {
            _words[market] = words.ToList();
            return Task.FromResult(Result.Success());
        }

        public void Set(string market, List<VerifiedPronunciation> words) => _words[market] = words;
    }

    private sealed class InMemoryOperatorAssignmentRepository : IOperatorAssignmentRepository
    {
        private readonly List<OperatorAssignment> _assignments = [];
        public IReadOnlyList<OperatorAssignment> Assignments => _assignments;

        public Task<Result<Option<OperatorAssignment>>> GetAsync(string objectId, CancellationToken ct = default) =>
            Task.FromResult(Result<Option<OperatorAssignment>>.Success(
                _assignments.FirstOrDefault(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
                    is { } assignment
                    ? Option<OperatorAssignment>.Some(assignment)
                    : Option<OperatorAssignment>.None));

        public Task<Result<IReadOnlyList<OperatorAssignment>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<OperatorAssignment>>.Success(_assignments));

        public Task<Result> UpsertAsync(OperatorAssignment assignment, CancellationToken ct = default)
        {
            _assignments.RemoveAll(a => string.Equals(a.ObjectId, assignment.ObjectId, StringComparison.OrdinalIgnoreCase));
            _assignments.Add(assignment);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string objectId, CancellationToken ct = default)
        {
            _assignments.RemoveAll(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        // Config import never touches the sign-in alias partition; these satisfy the interface
        // with the same in-memory shape the real repository persists (SWA userId -> Entra oid,
        // first writer wins) so a future import test can assert on it without a rewrite.
        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

        public Task<Result> UpsertDirectoryAliasAsync(
            string swaUserId, string objectId, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(swaUserId) && !string.IsNullOrWhiteSpace(objectId))
                _aliases.TryAdd(swaUserId, objectId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<Option<string>>> GetObjectIdByAliasAsync(
            string swaUserId, CancellationToken ct = default) =>
            Task.FromResult(Result<Option<string>>.Success(
                !string.IsNullOrWhiteSpace(swaUserId) && _aliases.TryGetValue(swaUserId, out string? oid)
                    ? Option<string>.Some(oid)
                    : Option<string>.None));

        public void Add(OperatorAssignment assignment) => _assignments.Add(assignment);
    }

    private sealed class InMemoryVoiceCalibrationRepository : IVoiceCalibrationRepository
    {
        private readonly List<VoiceCalibration> _calibrations = [];

        public Task<Result<Option<VoiceCalibration>>> GetCalibrationAsync(
            string voiceId, VoiceSettings settings, CancellationToken ct = default) =>
            Task.FromResult(Result<Option<VoiceCalibration>>.Success(
                _calibrations.FirstOrDefault(c =>
                    string.Equals(c.VoiceId, voiceId, StringComparison.Ordinal)
                    && c.Settings == settings) is { } calibration
                    ? Option<VoiceCalibration>.Some(calibration)
                    : Option<VoiceCalibration>.None));

        public Task<Result<IReadOnlyList<VoiceCalibration>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<VoiceCalibration>>.Success(_calibrations));

        public Task<Result> SaveCalibrationAsync(VoiceCalibration calibration, CancellationToken ct = default)
        {
            _calibrations.RemoveAll(c => string.Equals(c.ConfigurationKey, calibration.ConfigurationKey, StringComparison.OrdinalIgnoreCase));
            _calibrations.Add(calibration);
            return Task.FromResult(Result.Success());
        }

        public void Add(VoiceCalibration calibration) => _calibrations.Add(calibration);
    }

    private sealed class InMemoryPriorityRoadsRepository : IPriorityRoadsRepository
    {
        private readonly Dictionary<string, List<PriorityRoad>> _roads = new(StringComparer.OrdinalIgnoreCase);

        public Task<Result<IReadOnlyList<PriorityRoad>>> GetRoadsAsync(string market, CancellationToken ct = default)
        {
            _roads.TryGetValue(market, out var list);
            return Task.FromResult(Result<IReadOnlyList<PriorityRoad>>.Success(list ?? []));
        }

        public Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success(
                _roads.Keys.ToList() as IReadOnlyList<string>));

        public Task<Result> SetRoadsAsync(string market, IReadOnlyList<PriorityRoad> roads, string updatedBy, CancellationToken ct = default)
        {
            _roads[market] = roads.ToList();
            return Task.FromResult(Result.Success());
        }

        public void Set(string market, List<PriorityRoad> roads) => _roads[market] = roads;
    }

    private sealed class InMemoryFillerStatementsRepository : IFillerStatementsRepository
    {
        private readonly Dictionary<string, List<FillerStatement>> _statements = new(StringComparer.OrdinalIgnoreCase);

        public Task<Result<IReadOnlyList<FillerStatement>>> GetStatementsAsync(string market, CancellationToken ct = default)
        {
            _statements.TryGetValue(market, out var list);
            return Task.FromResult(Result<IReadOnlyList<FillerStatement>>.Success(list ?? []));
        }

        public Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success(
                _statements.Keys.ToList() as IReadOnlyList<string>));

        public Task<Result> SetStatementsAsync(string market, IReadOnlyList<FillerStatement> statements, string updatedBy, CancellationToken ct = default)
        {
            _statements[market] = statements.ToList();
            return Task.FromResult(Result.Success());
        }

        public void Set(string market, List<FillerStatement> statements) => _statements[market] = statements;
    }

    private sealed class InMemoryFillerAudioCacheService : IFillerAudioCacheService
    {
        public Task<Result<ResolvedFillerAudio>> ResolveAsync(
            FillerStatement statement, string voiceId, VoiceSettings settings, string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([], 0, [])));

        public Task<Result> InvalidateAsync(string fillerStatementId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<ResolvedCartClip>> ResolveCartAsync(
            string scriptText, string voiceId, VoiceSettings settings, string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ResolvedCartClip>.Failure("not supported"));
    }

    private sealed class InMemoryVoiceProfileService : IVoiceProfileService
    {
        private readonly InMemoryVoiceProfileRepository _repo;
        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public InMemoryVoiceProfileService(InMemoryVoiceProfileRepository repo) => _repo = repo;

        public Task<Result<IReadOnlyList<VoiceProfile>>> ListAsync(CancellationToken ct = default) =>
            _repo.ListAsync(ct);
        public Task<Result<VersionedLibraryListPage<VoiceProfile>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            _repo.ListAsync(query, ct);

        public Task<Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            _repo.GetManyAsync(ids, includeHistory, ct);

        public Task<Result<VoiceProfile>> GetAsync(string id, CancellationToken ct = default)
        {
            var result = _repo.GetAsync(id, ct).Result;
            if (result.IsFailure)
                return Task.FromResult(Result<VoiceProfile>.Failure(result.Error));
            return result.Value.HasValue
                ? Task.FromResult(Result<VoiceProfile>.Success(result.Value.Value))
                : Task.FromResult(Result<VoiceProfile>.Failure($"Voice profile '{id}' not found."));
        }

        public Task<Result<VoiceProfile>> ArchiveAsync(string id, CancellationToken ct = default)
        {
            var existing = _repo.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Task.FromResult(Result<VoiceProfile>.Failure($"Voice profile '{id}' not found."));
            existing.Archive(Now);
            return Task.FromResult(Result<VoiceProfile>.Success(existing));
        }

        public Task<Result<VoiceProfile>> UnarchiveAsync(string id, CancellationToken ct = default)
        {
            var existing = _repo.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Task.FromResult(Result<VoiceProfile>.Failure($"Voice profile '{id}' not found."));
            existing.Unarchive(Now);
            return Task.FromResult(Result<VoiceProfile>.Success(existing));
        }

        public Task<Result<VoiceProfile>> CreateAsync(CreateVoiceProfileInput input, string? createdBy, CancellationToken ct = default)
        {
            var result = VoiceProfile.Create(
                input.Name, input.Description, input.BaseVoiceId, input.BaseVoiceName,
                input.Model, input.Settings, input.ChangeNote, createdBy, Now,
                input.PaceBoostWpm, input.Id);
            if (result.IsSuccess)
            {
                CreateCount++;
                _repo.Add(result.Value);
            }
            return Task.FromResult(result);
        }

        public Task<Result<VoiceProfile>> UpdateAsync(string id, UpdateVoiceProfileInput input, string? editedBy, CancellationToken ct = default)
        {
            var existing = _repo.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Task.FromResult(Result<VoiceProfile>.Failure($"Voice profile '{id}' not found."));

            existing.ApplyEdit(input.Name, input.Description, input.Model, input.Settings,
                input.ChangeNote, editedBy, Now, input.PaceBoostWpm);
            UpdateCount++;
            return Task.FromResult(Result<VoiceProfile>.Success(existing));
        }

        public Task<Result<VoiceProfile>> RestoreVersionAsync(string id, int version, string? restoredBy, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class InMemoryDeliveryStyleService : IDeliveryStyleService
    {
        private readonly InMemoryDeliveryStyleRepository _repo;
        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public InMemoryDeliveryStyleService(InMemoryDeliveryStyleRepository repo) => _repo = repo;

        public Task<Result<IReadOnlyList<DeliveryStyle>>> ListAsync(CancellationToken ct = default) =>
            _repo.ListAsync(ct);
        public Task<Result<VersionedLibraryListPage<DeliveryStyle>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            _repo.ListAsync(query, ct);

        public Task<Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            _repo.GetManyAsync(ids, includeHistory, ct);

        public Task<Result<DeliveryStyle>> GetAsync(string id, CancellationToken ct = default)
        {
            var result = _repo.GetAsync(id, ct).Result;
            if (result.IsFailure)
                return Task.FromResult(Result<DeliveryStyle>.Failure(result.Error));
            return result.Value.HasValue
                ? Task.FromResult(Result<DeliveryStyle>.Success(result.Value.Value))
                : Task.FromResult(Result<DeliveryStyle>.Failure($"Delivery style '{id}' not found."));
        }

        public Task<Result<DeliveryStyle>> CreateAsync(CreateDeliveryStyleInput input, string? createdBy, CancellationToken ct = default)
        {
            var result = DeliveryStyle.Create(
                input.Name, input.Description, input.Policy, input.ChangeNote, createdBy, Now, input.Id);
            if (result.IsSuccess)
            {
                CreateCount++;
                _repo.Add(result.Value);
            }
            return Task.FromResult(result);
        }

        public Task<Result<DeliveryStyle>> UpdateAsync(string id, UpdateDeliveryStyleInput input, string? editedBy, CancellationToken ct = default)
        {
            var existing = _repo.Styles.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Task.FromResult(Result<DeliveryStyle>.Failure($"Delivery style '{id}' not found."));

            existing.ApplyEdit(input.Name, input.Description, input.Policy, input.ChangeNote, editedBy, Now);
            UpdateCount++;
            return Task.FromResult(Result<DeliveryStyle>.Success(existing));
        }

        public Task<Result<DeliveryStyle>> RestoreVersionAsync(string id, int version, string? restoredBy, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<DeliveryStyle>> ArchiveAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<DeliveryStyle>> UnarchiveAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class InMemoryBookendRepository : IBookendRepository
    {
        private readonly List<Bookend> _bookends = [];
        public IReadOnlyList<Bookend> Bookends => _bookends;

        public Task<Result<IReadOnlyList<Bookend>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<Bookend>>.Success(_bookends.ToList()));
        public Task<Result<VersionedLibraryListPage<Bookend>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<VersionedLibraryListPage<Bookend>>.Success(
                Page(_bookends, bookend => bookend.Name, query)));

        public Task<Result<IReadOnlyDictionary<string, Option<Bookend>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyDictionary<string, Option<Bookend>>>.Success(
                GetMany(_bookends, ids, bookend => bookend.Id)));


        public Task<Result<Option<Bookend>>> GetAsync(string id, CancellationToken ct = default)
        {
            var match = _bookends.FirstOrDefault(b =>
                string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match is null
                ? Result<Option<Bookend>>.Success(Option<Bookend>.None)
                : Result<Option<Bookend>>.Success(Option<Bookend>.Some(match)));
        }

        public Task<Result> SaveAsync(Bookend bookend, CancellationToken ct = default)
        {
            _bookends.RemoveAll(b =>
                string.Equals(b.Id, bookend.Id, StringComparison.OrdinalIgnoreCase));
            _bookends.Add(bookend);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default)
        {
            _bookends.RemoveAll(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(Result.Success());
        }

        public void Add(Bookend bookend) => _bookends.Add(bookend);
    }

    private sealed class InMemoryBookendService : IBookendService
    {
        private readonly InMemoryBookendRepository _repo;
        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public InMemoryBookendService(InMemoryBookendRepository repo) => _repo = repo;

        public Task<Result<IReadOnlyList<Bookend>>> ListAsync(CancellationToken ct = default) =>
            _repo.ListAsync(ct);

        public Task<Result<VersionedLibraryListPage<Bookend>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            _repo.ListAsync(query, ct);

        public Task<Result<IReadOnlyDictionary<string, Option<Bookend>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            _repo.GetManyAsync(ids, includeHistory, ct);
        public Task<Result<Bookend>> GetAsync(string id, CancellationToken ct = default)
        {
            var result = _repo.GetAsync(id, ct).Result;
            if (result.IsFailure)
                return Task.FromResult(Result<Bookend>.Failure(result.Error));
            return result.Value.HasValue
                ? Task.FromResult(Result<Bookend>.Success(result.Value.Value))
                : Task.FromResult(Result<Bookend>.Failure($"Bookend '{id}' not found."));
        }

        public Task<Result<Bookend>> CreateAsync(CreateBookendInput input, CancellationToken ct = default)
        {
            var result = Bookend.Create(input.Name, input.Description,
                new BookendTemplate(input.IntroText, input.OutroText), input.ChangeNote, input.Actor, Now, input.Id);
            if (result.IsSuccess)
            {
                CreateCount++;
                _repo.Add(result.Value);
            }
            return Task.FromResult(result);
        }

        public Task<Result<Bookend>> UpdateAsync(UpdateBookendInput input, CancellationToken ct = default)
        {
            var existing = _repo.Bookends.FirstOrDefault(b =>
                string.Equals(b.Id, input.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Task.FromResult(Result<Bookend>.Failure($"Bookend '{input.Id}' not found."));

            existing.ApplyEdit(input.Name, input.Description, new BookendTemplate(input.IntroText, input.OutroText),
                input.ChangeNote, input.Actor, Now);
            UpdateCount++;
            return Task.FromResult(Result<Bookend>.Success(existing));
        }

        public Task<Result<Bookend>> RestoreVersionAsync(string id, int version, string? restoredBy, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<Bookend>> ArchiveAsync(string id, string? actor, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<Bookend>> UnarchiveAsync(string id, string? actor, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class InMemoryPronunciationDictionaryService : IPronunciationDictionaryService
    {
        private readonly InMemoryPronunciationDictionaryRepository _repo;
        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public InMemoryPronunciationDictionaryService(InMemoryPronunciationDictionaryRepository repo) => _repo = repo;

        public Task<Result<IReadOnlyList<PronunciationDictionary>>> ListAsync(CancellationToken ct = default) =>
            _repo.ListAsync(ct);

        public Task<Result<VersionedLibraryListPage<PronunciationDictionary>>> ListAsync(
            VersionedLibraryListQuery query, CancellationToken ct = default) =>
            _repo.ListAsync(query, ct);

        public Task<Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>> GetManyAsync(
            IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
            _repo.GetManyAsync(ids, includeHistory, ct);
        public Task<Result<PronunciationDictionary>> GetAsync(string id, CancellationToken ct = default)
        {
            var result = _repo.GetAsync(id, ct).Result;
            if (result.IsFailure)
                return Task.FromResult(Result<PronunciationDictionary>.Failure(result.Error));
            return result.Value.HasValue
                ? Task.FromResult(Result<PronunciationDictionary>.Success(result.Value.Value))
                : Task.FromResult(Result<PronunciationDictionary>.Failure($"Pronunciation dictionary '{id}' not found."));
        }

        public Task<Result<PronunciationDictionary>> CreateAsync(CreatePronunciationDictionaryInput input, string? createdBy, CancellationToken ct = default)
        {
            var result = PronunciationDictionary.Create(
                input.Name, input.Description, $"el-{Guid.NewGuid():N}", "v1",
                input.Rules, input.ChangeNote, createdBy, Now, input.Id);
            if (result.IsSuccess)
            {
                CreateCount++;
                _repo.Add(result.Value);
            }
            return Task.FromResult(result);
        }

        public Task<Result<PronunciationDictionary>> UpdateAsync(string id, UpdatePronunciationDictionaryInput input, string? editedBy, CancellationToken ct = default)
        {
            var existing = _repo.Dictionaries.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Task.FromResult(Result<PronunciationDictionary>.Failure($"Pronunciation dictionary '{id}' not found."));

            existing.ApplyEdit(input.Name, input.Description, $"el-v{existing.CurrentVersion + 1}",
                input.Rules, input.ChangeNote, editedBy, Now);
            UpdateCount++;
            return Task.FromResult(Result<PronunciationDictionary>.Success(existing));
        }

        public Task<Result<PronunciationDictionary>> RestoreVersionAsync(string id, int version, string? restoredBy, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<PronunciationDictionary>> ArchiveAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<PronunciationDictionary>> UnarchiveAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    // ── SUT factory ──────────────────────────────────────────────────

    private readonly InMemoryScheduleRepository _scheduleRepo = new();
    private readonly InMemoryVoiceProfileRepository _voiceProfileRepo = new();
    private readonly InMemoryDeliveryStyleRepository _deliveryStyleRepo = new();
    private readonly InMemoryBookendRepository _bookendRepo = new();
    private readonly InMemoryPronunciationDictionaryRepository _dictRepo = new();
    private readonly InMemorySafetyTermRepository _safetyTermsRepo = new();
    private readonly InMemoryGuidanceRepository _guidanceRepo = new();
    private readonly InMemoryVerifiedPronunciationRepository _verifiedPronunciationsRepo = new();
    private readonly InMemoryOperatorAssignmentRepository _operatorAssignmentsRepo = new();
    private readonly InMemoryVoiceCalibrationRepository _voiceCalibrationsRepo = new();
    private readonly InMemoryPriorityRoadsRepository _priorityRoadsRepo = new();
    private readonly InMemoryFillerStatementsRepository _fillerStatementsRepo = new();
    private readonly InMemoryFillerAudioCacheService _fillerAudioCache = new();
    private readonly FixedTimeProvider _timeProvider = new(Now);

    private readonly InMemoryVoiceProfileService _voiceProfileService;
    private readonly InMemoryDeliveryStyleService _deliveryStyleService;
    private readonly InMemoryBookendService _bookendService;
    private readonly InMemoryPronunciationDictionaryService _dictService;
    private readonly ScheduleService _scheduleService;
    private readonly ConfigImportService _sut;
    private readonly ConfigExportBuilder _exportBuilder;

    public ConfigImportServiceTests()
    {
        var fakeCronParser = new FakeCronParser();
        var fakeLogger = NullLogger<ScheduleService>.Instance;

        _voiceProfileService = new InMemoryVoiceProfileService(_voiceProfileRepo);
        _deliveryStyleService = new InMemoryDeliveryStyleService(_deliveryStyleRepo);
        _bookendService = new InMemoryBookendService(_bookendRepo);
        _dictService = new InMemoryPronunciationDictionaryService(_dictRepo);

        _scheduleService = new ScheduleService(_scheduleRepo, fakeCronParser, _timeProvider, fakeLogger);
        _sut = new ConfigImportService(
            _scheduleRepo, _scheduleService,
            _voiceProfileService, _deliveryStyleService, EmptyPlaybooks(),
            _bookendService, _dictService,
            _safetyTermsRepo, _guidanceRepo, _priorityRoadsRepo, _verifiedPronunciationsRepo,
            _operatorAssignmentsRepo, _voiceCalibrationsRepo, _fillerStatementsRepo, _fillerAudioCache,
            _timeProvider, NullLogger<ConfigImportService>.Instance);

        _exportBuilder = new ConfigExportBuilder(
            _scheduleRepo, _voiceProfileService, _deliveryStyleService, _bookendService, _dictService,
            _safetyTermsRepo, _guidanceRepo, _priorityRoadsRepo,
            _verifiedPronunciationsRepo, _operatorAssignmentsRepo, _voiceCalibrationsRepo,
            _fillerStatementsRepo, TimeProvider.System);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static CoverageArea MakeCoverage() => CoverageArea.Create(-33.87, 151.21, 50).Value;

    private static CoverageDto MakeCoverageDto() => new(-33.87, 151.21, 50);

    private static VoiceSettings MakeSettings() => VoiceSettings.Create().Value;

    private static VoiceProfile CreateExistingVoiceProfile(string id, string name) =>
        VoiceProfile.Create(name, null, "eleven_base", "Base Voice", "eleven_v3",
            MakeSettings(), null, "test", Now, id: id).Value;

    private static DeliveryStyle CreateExistingDeliveryStyle(string id, string name) =>
        DeliveryStyle.Create(name, null, DeliveryStylePolicy.Neutral,
            null, "test", Now, id).Value;

    private static PronunciationDictionary CreateExistingDict(string id, string name) =>
        PronunciationDictionary.Create(name, null, $"el-{id}", "v1",
            [PronunciationRule.CreateAlias("test", "test-replacement").Value],
            null, "test", Now, id).Value;

    // ── 1. Round-trip idempotency ────────────────────────────────────

    [Fact]
    public async Task ExportBuilder_SchedulelessMarketWithConfig_IsIncludedInTheBundle()
    {
        // bug-bash F4: the export derived its market universe from schedules only, so priority
        // roads / filler / verified config for a market with no schedule (live: cairns-kiis90,
        // 11 real roads) was silently dropped from the bundle.
        _priorityRoadsRepo.Set("cairns-kiis90", [
            new PriorityRoad("Bruce Highway", true, FlowPriorityTier.Critical, true)
        ]);
        _fillerStatementsRepo.Set("cairns-kiis90", [new FillerStatement("f1", "Stay safe.")]);
        _verifiedPronunciationsRepo.Set("cairns-kiis90", [
            new VerifiedPronunciation("Keilor", "Said KEE-ler", true, "test", Now)
        ]);

        // A real schedule for another market, so the schedule-derived universe is non-empty.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _scheduleRepo.Add(MarketSchedule.Create("sydney", "Sydney", "2GB",
            "Australia/Sydney", "*/30 6-9 * * 1-5", true, MakeCoverage(),
            "vp-test", "sydney/traffic/", 30, 15, 5, Now, Now, "", "").Value);

        var allSections = new HashSet<string>(ConfigSections.All, StringComparer.OrdinalIgnoreCase);
        var exportResult = await _exportBuilder.BuildAsync(
            allSections, "https://test/api/config/schema", CancellationToken.None);

        exportResult.IsSuccess.ShouldBeTrue();
        var bundle = exportResult.Value;

        bundle.PriorityRoads!.ShouldContain(r =>
            string.Equals(r.Market, "cairns-kiis90", StringComparison.OrdinalIgnoreCase));
        bundle.FillerStatements!.ShouldContain(f =>
            string.Equals(f.Market, "cairns-kiis90", StringComparison.OrdinalIgnoreCase));
        bundle.VerifiedPronunciations!.ShouldContain(v =>
            string.Equals(v.Market, "cairns-kiis90", StringComparison.OrdinalIgnoreCase));
        bundle.Schedules!.ShouldNotContain(s =>
            string.Equals(s.MarketId, "cairns-kiis90", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanAsync_RoundTripIdempotency_ExportThenImportAllUnchanged()
    {
        // Arrange: seed all sections
        var vp = CreateExistingVoiceProfile("vp-test", "Test Voice");
        _voiceProfileRepo.Add(vp);

        var ds = CreateExistingDeliveryStyle("ds-test", "Test Style");
        _deliveryStyleRepo.Add(ds);

        var pd = CreateExistingDict("pd-test", "Test Dict");
        _dictRepo.Add(pd);

        var schedule = MarketSchedule.Create("sydney", "Sydney Traffic", "2GB",
            "Australia/Sydney", "*/30 6-9 * * 1-5", true, MakeCoverage(),
            "vp-test", "sydney/traffic/", 30, 15, 5, Now, Now,
            "ds-test", "pd-test").Value;
        _scheduleRepo.Add(schedule);

        _safetyTermsRepo.Add(SafetyTerm.Create(SafetyTermKind.Banned, SafetyScope.Global,
            null, "test-banned", "test", Now));

        _guidanceRepo.Add(GuidanceEntry.Create(GuidanceScope.Global, "", GuidanceType.Style,
            "Keep it conversational", true, "test", Now));

        _priorityRoadsRepo.Set("sydney", [new PriorityRoad("M1 Pacific Motorway", true, FlowPriorityTier.High, true)]);
        _verifiedPronunciationsRepo.Set("sydney", [
            new VerifiedPronunciation("Keilor", "Said KEE-ler", true, "test", Now)
        ]);

        _operatorAssignmentsRepo.Add(new OperatorAssignment(
            "operator-1", "Test Operator", ["sydney"], Now, "test"));

        var calibrationSettings = MakeSettings();
        _voiceCalibrationsRepo.Add(new VoiceCalibration(
            calibrationSettings.GetConfigurationKey("voice-1"), "voice-1", calibrationSettings)
        {
            Provider = "elevenlabs",
            SyllablesPerSecond = 4.2,
            SyllablesPerWord = 1.4,
            WordsPerMinute = 180,
            SyllablesPerMinute = 252,
            RunCount = 3,
            ReferenceSyllableCount = 42,
            ReferenceDurationSeconds = 10,
            CalibratedAt = Now,
            ValidFor = TimeSpan.FromDays(30)
        });

        // Act: export via real builder, then import
        var allSections = new HashSet<string>(ConfigSections.All, StringComparer.OrdinalIgnoreCase);
        var exportResult = await _exportBuilder.BuildAsync(allSections, "https://test/api/config/schema", CancellationToken.None);
        exportResult.IsSuccess.ShouldBeTrue();

        var plan = await _sut.PlanAsync(exportResult.Value, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();

        plan.Value.Items.Count.ShouldBe(10);
        // Assert: every item is Unchanged with no apply thunk
        plan.Value.Items.ShouldAllBe(i => i.Report.Action == ConfigImportActions.Unchanged);
        plan.Value.Items.ShouldAllBe(i => i.Apply == null);
        plan.Value.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task PlanAsync_InvalidVerifiedPronunciation_ReportsError()
    {
        var plan = await _sut.PlanAsync(new ConfigBundleDto(VerifiedPronunciations: [
            new ConfigVerifiedPronunciationsDto("sydney", [
                new VerifiedPronunciationDto(new string('x', ConfigValidationLimits.MaxVerifiedPronunciationLength + 1))
            ])
        ]), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Single().Report.Action.ShouldBe(ConfigImportActions.Error);
    }

    [Fact]
    public async Task PlanAsync_InvalidOperatorAssignment_ReportsError()
    {
        var plan = await _sut.PlanAsync(new ConfigBundleDto(OperatorAssignments: [
            new ConfigOperatorAssignmentDto("operator-1", "Test", ["bad/market"])
        ]), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Single().Report.Action.ShouldBe(ConfigImportActions.Error);
    }

    [Fact]
    public async Task PlanAsync_MismatchedVoiceCalibrationKey_ReportsError()
    {
        var settings = MakeSettings();
        var plan = await _sut.PlanAsync(new ConfigBundleDto(VoiceCalibrations: [
            new ConfigVoiceCalibrationDto("wrong-key", "voice-1",
                new VoiceSettingsDto(settings.Speed, settings.Stability, settings.SimilarityBoost,
                    settings.Style, settings.UseSpeakerBoost))
        ]), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Single().Report.Action.ShouldBe(ConfigImportActions.Error);
    }

    [Fact]
    public async Task PlanAsync_SchedulePolygonCoverageWithStaleDerivedFields_ReportsUnchanged()
    {
        // Reproduces the config-transfer false-positive: a persisted schedule whose polygon
        // Coverage carries stale/rounded derived Latitude/Longitude/RadiusKm (e.g. written by an
        // older import or hand-rounded), re-imported with the SAME polygon points unmodified.
        // The polygon shape hasn't changed, so this must report Unchanged, not Update.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var points = new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        };
        var freshCoverage = CoverageArea.CreatePolygon(points).Value;
        var staleCoverage = freshCoverage with { RadiusKm = 32.5 }; // rounded, not the true derived value

        var schedule = MarketSchedule.Create("adelaide", "Adelaide Drive Traffic", "KIIS 102.3",
            "Australia/Adelaide", "*/30 6-9 * * 1-5", true, staleCoverage,
            "vp-test", "adelaide/traffic/", 30, 15, 5, Now, Now,
            "", "").Value;
        _scheduleRepo.Add(schedule);

        var pointDtos = points.Select(p => new CoveragePointDto(p.Lat, p.Lng)).ToList();
        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "adelaide", Name: "Adelaide Drive Traffic", Station: "KIIS 102.3",
                Timezone: "Australia/Adelaide", CronExpression: "*/30 6-9 * * 1-5", Enabled: true,
                TargetDurationSeconds: 30, NotifyBeforePlayoutMinutes: 15, EscalationBeforePlayoutMinutes: 5,
                VoiceProfileId: "vp-test", OutputPath: "adelaide/traffic/",
                Coverage: new CoverageDto(staleCoverage.Latitude, staleCoverage.Longitude, 32.5, "polygon",
                    PolygonPoints: pointDtos))
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Unchanged);
    }

    [Fact]
    public async Task PlanAsync_ScheduleWithValidBookendAssignment_PlansCreate()
    {
        // The bookend universe seeded from the existing library must accept a schedule that
        // assigns a known pair (plus a rotation and token overrides).
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _bookendRepo.Add(Bookend.Create("Intro One", null,
            new BookendTemplate("Looking over {city} today", "That's it for {city} traffic"), null, "test", Now, "bi-1").Value);
        _bookendRepo.Add(Bookend.Create("Outro One", null,
            new BookendTemplate("That's it for {city} traffic", ""), null, "test", Now, "bo-1").Value);

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "perth", Name: "Perth Traffic", Station: "6PR",
                Timezone: "Australia/Perth", CronExpression: "*/30 6-9 * * 1-5",
                Enabled: true, TargetDurationSeconds: 30,
                VoiceProfileId: "vp-test", Coverage: MakeCoverageDto(),
                BookendId: "bi-1",
                BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bi-1"]),
                BookendTokens: new Dictionary<string, string> { ["city"] = "Perth" })
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Single().Report.Action.ShouldBe(ConfigImportActions.Create);
    }

    [Fact]
    public async Task PlanAsync_ScheduleWithUnknownBookendId_ReportsError()
    {
        // An unknown pair id: the single bookend universe rejects a schedule that references
        // a pair that neither exists nor is created by this bundle.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _bookendRepo.Add(Bookend.Create("Outro One", null,
            new BookendTemplate("That's it for traffic", ""), null, "test", Now, "bo-1").Value);

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "perth", Name: "Perth Traffic", Station: "6PR",
                Timezone: "Australia/Perth", CronExpression: "*/30 6-9 * * 1-5",
                Enabled: true, TargetDurationSeconds: 30,
                VoiceProfileId: "vp-test", Coverage: MakeCoverageDto(),
                BookendId: "bk-nope")
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        var item = plan.Value.Items.Single();
        item.Report.Action.ShouldBe(ConfigImportActions.Error);
        item.Report.Detail.ShouldNotBeNull();
        item.Report.Detail!.ShouldContain("does not exist");
    }

    [Fact]
    public async Task PlanAsync_ScheduleWithUnknownBookendRotationMember_ReportsError()
    {
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _bookendRepo.Add(Bookend.Create("Intro One", null,
            new BookendTemplate("Looking over the city", ""), null, "test", Now, "bi-1").Value);

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "perth", Name: "Perth Traffic", Station: "6PR",
                Timezone: "Australia/Perth", CronExpression: "*/30 6-9 * * 1-5",
                Enabled: true, TargetDurationSeconds: 30,
                VoiceProfileId: "vp-test", Coverage: MakeCoverageDto(),
                BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bi-1", "bi-nope"]))
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        var item = plan.Value.Items.Single();
        item.Report.Action.ShouldBe(ConfigImportActions.Error);
        item.Report.Detail.ShouldNotBeNull();
        item.Report.Detail!.ShouldContain("does not exist (bookend rotation)");
    }

    // ── 2. Diff detection ────────────────────────────────────────────

    [Fact]
    public async Task PlanAsync_DeliveryStylePolicyChange_DetectsUpdate()
    {
        _deliveryStyleRepo.Add(CreateExistingDeliveryStyle("ds-1", "Morning Tone"));

        var bundle = new ConfigBundleDto(DeliveryStyles: [
            new ConfigDeliveryStyleDto("ds-1", "Morning Tone", null,
                new DeliveryStylePolicyDto("Warm and calm", ["[calm]"], 1))
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Update);
    }

    [Fact]
    public async Task PlanAsync_BookendsSection_SeedsPendingIdUniverse()
    {
        // The Bookends section is a single list of intro/outro pairs; the planner seeds the
        // universe (existing ∪ created-by-this-bundle), so a schedule can reference a bookend
        // created earlier in the SAME bundle. A pair with BOTH lines blank is a per-item error.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        var bundle = new ConfigBundleDto(
            Bookends: [
                new ConfigBookendDto(Id: "bk-open", Name: "Melbourne Open",
                    IntroText: "Looking over {city} today", OutroText: "That's it for {city} traffic."),
                new ConfigBookendDto(Id: "bk-close", Name: "Melbourne Close",
                    IntroText: "", OutroText: "That's all from {city} traffic."),
                new ConfigBookendDto(Id: "bk-bad", Name: "Blank Pair", IntroText: "", OutroText: "  ")
            ],
            Schedules: [
                new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                    Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                    Enabled: true, TargetDurationSeconds: 30, NotifyBeforePlayoutMinutes: 15,
                    EscalationBeforePlayoutMinutes: 5, VoiceProfileId: "vp-test",
                    OutputPath: "melbourne/traffic/", Coverage: MakeCoverageDto(),
                    BookendId: "bk-open",
                    BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bk-open"]),
                    BookendTokens: new Dictionary<string, string> { ["city"] = "Melbourne" })
            ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();

        var bookendItems = plan.Value.Items.Where(i => i.Report.Section == ConfigSections.Bookends).ToList();
        bookendItems.Count.ShouldBe(3);
        bookendItems.Count(i => i.Report.Action == ConfigImportActions.Create).ShouldBe(2);
        bookendItems.Single(i => i.Report.Action == ConfigImportActions.Error)
            .Report.Detail!.ShouldContain("needs an intro, an outro, or both");

        // The pending ids seeded the bookend universe, so the schedule's primary reference
        // AND its rotation member pass referential validation and the schedule plans clean.
        // (The bundle still has errors overall — the deliberate blank pair above is one.)
        plan.Value.Items.Single(i => i.Report.Section == ConfigSections.Schedules)
            .Report.Action.ShouldBe(ConfigImportActions.Create);
    }

    [Fact]
    public async Task PlanAsync_NewVoiceProfileWithExplicitId_CreatesPreservingThatId()
    {
        var bundle = new ConfigBundleDto(VoiceProfiles: [
            new ConfigVoiceProfileDto(Id: "vp-custom-id", Name: "Custom Voice",
                BaseVoiceId: "eleven_base", Model: "eleven_v3", Settings: new VoiceSettingsDto())
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        plan.Value.Items[0].Report.Key.ShouldBe("vp-custom-id");
    }

    // ── 3. All-or-nothing gate ───────────────────────────────────────

    [Fact]
    public async Task PlanAsync_InvalidGuidanceTextBeyondMaxLen_HasErrorsAndReportListsValidItems()
    {
        var bundle = new ConfigBundleDto(Guidance: [
            new ConfigGuidanceDto(Scope: "Global", Type: "Style", Text: "Short OK"),
            new ConfigGuidanceDto(Scope: "Global", Type: "Style", Text: new string('X', 601))
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var errors = plan.Value.Items.Where(i => i.Report.Action == ConfigImportActions.Error).ToList();
        errors.Count.ShouldBe(1);
        errors[0].Report.Detail.ShouldNotBeNull();
        errors[0].Report.Detail!.ShouldContain("600");

        var creates = plan.Value.Items.Where(i => i.Report.Action == ConfigImportActions.Create).ToList();
        creates.Count.ShouldBe(1);
    }

    // ── 4. Referential integrity ─────────────────────────────────────

    [Fact]
    public async Task PlanAsync_ScheduleReferencesUnknownVoiceProfileId_ErrorNamesId()
    {
        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                VoiceProfileId: "vp-nonexistent",
                Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("vp-nonexistent");
    }

    [Fact]
    public async Task PlanAsync_ScheduleReferencesVoiceProfileWithWrongCase_ReportsError()
    {
        // DKI-02: the import id universes are ORDINAL because at airtime every id resolves through
        // a case-SENSITIVE Table RowKey lookup (StationConfigResolver). 'VP-TEST' vs 'vp-test'
        // used to pass validation (OrdinalIgnoreCase universe), import clean, show as assigned —
        // and the station aired with the DEFAULT voice.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                VoiceProfileId: "VP-TEST",
                Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("VP-TEST");
        error.Report.Detail.ShouldContain("does not exist");
    }

    [Fact]
    public async Task PlanAsync_ScheduleReferencesPronunciationDictionaryWithWrongCase_ReportsError()
    {
        // DKI-02's own example: a bundle assigning 'My-Dict' to a library called 'my-dict' imported
        // clean, showed as assigned in the console, and the station aired with NO dictionary.
        // Ordinal validation fails it loudly at plan time.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _dictRepo.Add(CreateExistingDict("my-dict", "My Dict"));

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                VoiceProfileId: "vp-test", PronunciationDictionaryId: "My-Dict",
                Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("My-Dict");
        error.Report.Detail.ShouldContain("does not exist");
    }

    [Fact]
    public async Task PlanAsync_ScheduleVoiceRotationMemberWithWrongCase_ReportsError()
    {
        // DKI-03: the resolver treats case-variant rotation members as DIFFERENT ids (case-sensitive
        // RowKey lookups), so a member that differs only by case from an existing profile would
        // resolve to nothing and air the PRIMARY voice every time the cursor lands on it. Import
        // must reject the member unless an EXACT-cased id exists.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                VoiceProfileId: "vp-test",
                VoiceRotation: new VoiceRotationDto("roundRobin", VoiceProfileIds: ["vp-test", "VP-TEST"]),
                Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("VP-TEST");
        error.Report.Detail.ShouldContain("voice rotation");
    }

    [Fact]
    public async Task PlanAsync_ScheduleBookendRotationMemberWithWrongCase_ReportsError()
    {
        // The bookend twin of the case-exact rotation rule: 'BI-1' is not the stored 'bi-1' to the
        // resolver's RowKey lookup, so the member must fail import unless an exact id exists.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _bookendRepo.Add(Bookend.Create("Intro One", null,
            new BookendTemplate("Looking over the city", ""), null, "test", Now, "bi-1").Value);

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                VoiceProfileId: "vp-test",
                BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bi-1", "BI-1"]),
                Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("BI-1");
        error.Report.Detail.ShouldContain("bookend rotation");
    }

    [Fact]
    public async Task PlanAsync_ScheduleReferencesVoiceProfileCreatedEarlierInBundle_NoError()
    {
        var bundle = new ConfigBundleDto(
            VoiceProfiles: [
                new ConfigVoiceProfileDto(Id: "vp-new", Name: "New Voice",
                    BaseVoiceId: "eleven_base", Model: "eleven_v3", Settings: new VoiceSettingsDto())
            ],
            Schedules: [
                new ConfigScheduleDto(MarketId: "melbourne", Name: "Melbourne", Station: "3AW",
                    Timezone: "Australia/Melbourne", CronExpression: "*/30 6-9 * * 1-5",
                    VoiceProfileId: "vp-new",
                    Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
            ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();
        plan.Value.Items.ShouldAllBe(i => i.Report.Action == ConfigImportActions.Create);
    }

    // ── 5. New-market completeness ───────────────────────────────────

    [Fact]
    public async Task PlanAsync_NewMarketMissingCronExpression_ErrorNewItem()
    {
        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "perth", Name: "Perth", Station: "6PR",
                Timezone: "Australia/Perth",
                Coverage: MakeCoverageDto(), TargetDurationSeconds: 30)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("New market");
    }

    [Fact]
    public async Task PlanAsync_CompleteNewMarketSchedule_CreatesAndApplyPersists()
    {
        var bundle = new ConfigBundleDto(
            VoiceProfiles: [
                new ConfigVoiceProfileDto(Id: "vp-new", Name: "New Voice",
                    BaseVoiceId: "eleven_base", Model: "eleven_v3", Settings: new VoiceSettingsDto())
            ],
            Schedules: [
                new ConfigScheduleDto(
                    MarketId: "perth", Name: "Perth Traffic", Station: "6PR",
                    Timezone: "Australia/Perth", CronExpression: "*/30 6-9 * * 1-5",
                    Enabled: true, TargetDurationSeconds: 30,
                    VoiceProfileId: "vp-new", Coverage: MakeCoverageDto())
            ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();

        var scheduleItem = plan.Value.Items.Single(i =>
            i.Report.Section == ConfigSections.Schedules && i.Report.Action == ConfigImportActions.Create);
        scheduleItem.Apply.ShouldNotBeNull();

        var report = await _sut.ApplyAsync(plan.Value, false, CancellationToken.None);
        report.Applied.ShouldBeTrue();

        _scheduleRepo.Schedules.ShouldContain(s =>
            string.Equals(s.MarketId, "perth", StringComparison.OrdinalIgnoreCase));
    }

    // ── 6. Upsert-only ──────────────────────────────────────────────

    [Fact]
    public async Task PlanAsync_EntitiesAbsentFromBundle_SurviveApplyUntouched()
    {
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-keep", "Keep Voice"));
        _deliveryStyleRepo.Add(CreateExistingDeliveryStyle("ds-keep", "Keep Style"));

        var bundle = new ConfigBundleDto(VoiceProfiles: [
            new ConfigVoiceProfileDto(Id: "vp-new", Name: "New Voice",
                BaseVoiceId: "eleven_base", Model: "eleven_v3", Settings: new VoiceSettingsDto())
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();

        var report = await _sut.ApplyAsync(plan.Value, false, CancellationToken.None);
        report.Applied.ShouldBeTrue();

        _deliveryStyleRepo.Styles.ShouldContain(s => s.Id == "ds-keep");
    }

    // ── 7. Per-market replace + set equality ─────────────────────────

    [Fact]
    public async Task PlanAsync_PriorityRoadsReorderedIdenticalContent_Unchanged()
    {
        _priorityRoadsRepo.Set("sydney", [
            new PriorityRoad("M1 Pacific Motorway", true, FlowPriorityTier.High, true),
            new PriorityRoad("M2 Hills Motorway", true, FlowPriorityTier.Normal, true)
        ]);

        var bundle = new ConfigBundleDto(PriorityRoads: [
            new PriorityRoadsDto("sydney", [
                new PriorityRoadDto("M2 Hills Motorway", true, "normal", true),
                new PriorityRoadDto("M1 Pacific Motorway", true, "high", true)
            ])
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Unchanged);
    }

    [Fact]
    public async Task PlanAsync_PriorityRoadsFlowTierChanged_UpdateAndApplyReplacesList()
    {
        _priorityRoadsRepo.Set("sydney", [
            new PriorityRoad("M1 Pacific Motorway", true, FlowPriorityTier.High, true)
        ]);

        var bundle = new ConfigBundleDto(PriorityRoads: [
            new PriorityRoadsDto("sydney", [
                new PriorityRoadDto("M1 Pacific Motorway", true, "critical", true)
            ])
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Update);

        var report = await _sut.ApplyAsync(plan.Value, false, CancellationToken.None);
        report.Applied.ShouldBeTrue();

        var roads = await _priorityRoadsRepo.GetRoadsAsync("sydney", CancellationToken.None);
        roads.Value.Single().FlowTier.ShouldBe(FlowPriorityTier.Critical);
    }

    [Fact]
    public async Task PlanAsync_PriorityRoadsOmittedFlowTier_DefaultsToNone()
    {
        var bundle = new ConfigBundleDto(PriorityRoads: [
            new PriorityRoadsDto("sydney", [
                new PriorityRoadDto("M1 Pacific Motorway")
            ])
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();

        var report = await _sut.ApplyAsync(plan.Value, requiresApproval: false, CancellationToken.None);
        report.Applied.ShouldBeTrue();
        var roads = await _priorityRoadsRepo.GetRoadsAsync("sydney", CancellationToken.None);
        roads.Value.Single().FlowTier.ShouldBe(FlowPriorityTier.None);
    }

    [Fact]
    public async Task PlanAsync_PriorityRoadsNullFlowTier_DefaultsToNone()
    {
        var bundle = new ConfigBundleDto(PriorityRoads: [
            new PriorityRoadsDto("sydney", [
                new PriorityRoadDto("M1 Pacific Motorway", FlowTier: null!)
            ])
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();

        var report = await _sut.ApplyAsync(plan.Value, requiresApproval: false, CancellationToken.None);
        report.Applied.ShouldBeTrue();
        var roads = await _priorityRoadsRepo.GetRoadsAsync("sydney", CancellationToken.None);
        roads.Value.Single().FlowTier.ShouldBe(FlowPriorityTier.None);
    }

    [Fact]
    public async Task PlanAsync_PriorityRoadsUnknownFlowTier_ReturnsErrorWithValidValues()
    {
        var bundle = new ConfigBundleDto(PriorityRoads: [
            new PriorityRoadsDto("sydney", [
                new PriorityRoadDto("M1 Pacific Motorway", FlowTier: "BOGUS")
            ])
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("BOGUS");
        error.Report.Detail.ShouldContain("none, normal, high, critical");
        (await _priorityRoadsRepo.GetRoadsAsync("sydney", CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlanAsync_PriorityRoadsControlCharacterInName_ReturnsError()
    {
        var bundle = new ConfigBundleDto(PriorityRoads: [
            new PriorityRoadsDto("sydney", [
                new PriorityRoadDto("M1 Pacific Motorway\u202E")
            ])
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain(
            "must not contain control or formatting characters (including line breaks)");
        (await _priorityRoadsRepo.GetRoadsAsync("sydney", CancellationToken.None)).Value.ShouldBeEmpty();
    }

    // ── 8. Safety-term guards ────────────────────────────────────────

    [Fact]
    public async Task PlanAsync_SafetyTermKindScopeChangeOfExistingId_Error()
    {
        _safetyTermsRepo.Add(new SafetyTerm("st-1", SafetyTermKind.Banned, SafetyScope.Global,
            "", "badword", "test", Now, Now, true));

        var bundle = new ConfigBundleDto(SafetyTerms: [
            new ConfigSafetyTermDto(Id: "st-1", Kind: "Allowed", Scope: "Global",
                Term: "badword", Enabled: true)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();

        var error = plan.Value.Items.Single(i => i.Report.Action == ConfigImportActions.Error);
        error.Report.Detail.ShouldNotBeNull();
        error.Report.Detail!.ShouldContain("kind");
    }

    [Fact]
    public async Task PlanAsync_SafetyTermIdLessDuplicateOfExistingKindScopeTerm_Unchanged()
    {
        _safetyTermsRepo.Add(new SafetyTerm("st-1", SafetyTermKind.Banned, SafetyScope.Global,
            "", "badword", "test", Now, Now, true));

        var bundle = new ConfigBundleDto(SafetyTerms: [
            new ConfigSafetyTermDto(Kind: "Banned", Scope: "Global", Term: "badword", Enabled: true)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Unchanged);
    }

    /// <summary>
    ///     The approve gate re-plans a parked bundle and refuses it unless the fresh report is
    ///     equivalent to the stored one — equivalence being the (section, key, action) multiset.
    ///     A create row keyed by the entity id the plan just minted therefore drifted against
    ///     itself, so a bundle adding a banned word could NEVER be approved wherever
    ///     RequireSecondApprover is on (prod). Reproduced on dev 2026-08-10: 409 "SafetyTerms
    ///     &lt;guid-a&gt; was Create, now absent; SafetyTerms &lt;guid-b&gt; was absent, now Create".
    /// </summary>
    [Fact]
    public async Task PlanAsync_SafetyTermCreate_ReportKeyIsStableAcrossPlans()
    {
        var bundle = new ConfigBundleDto(SafetyTerms: [
            new ConfigSafetyTermDto(Kind: "Banned", Scope: "Global", Term: "cfgword", Enabled: true)
        ]);

        var first = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        var second = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        var a = first.Value.ToReport(dryRun: false, requiresApproval: true, applied: false);
        var b = second.Value.ToReport(dryRun: false, requiresApproval: true, applied: false);

        a.Items.Single().Action.ShouldBe(ConfigImportActions.Create);
        a.Items.Single().Key.ShouldBe("cfgword");
        a.IsEquivalentTo(b).ShouldBeTrue(a.DescribeDrift(b));
    }

    [Fact]
    public async Task PlanAsync_SafetyTermCreateWithExplicitId_ReportKeyIsThatId()
    {
        var bundle = new ConfigBundleDto(SafetyTerms: [
            new ConfigSafetyTermDto(Id: "st-new", Kind: "Banned", Scope: "Global",
                Term: "cfgword", Enabled: true)
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);
        var item = plan.Value.Items.Single().Report;
        item.Action.ShouldBe(ConfigImportActions.Create);
        item.Key.ShouldBe("st-new");
    }

    // ── 9. Apply failure isolation ───────────────────────────────────

    [Fact]
    public async Task ApplyAsync_OneItemFailsToPersist_OtherItemsStillApplyAndFailureIsReported()
    {
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-ok", "OK Voice"));
        _deliveryStyleRepo.Add(CreateExistingDeliveryStyle("ds-doomed", "Doomed Style"));

        // Real DeliveryStyleService over the failable repo: it propagates SaveAsync failures,
        // which the in-memory service fake (mutate-in-place, no save) cannot simulate.
        var sut = new ConfigImportService(
            _scheduleRepo, _scheduleService,
            _voiceProfileService,
            new DeliveryStyleService(_deliveryStyleRepo, _scheduleRepo, _timeProvider,
                NullLogger<DeliveryStyleService>.Instance),
            EmptyPlaybooks(),
            _bookendService, _dictService,
            _safetyTermsRepo, _guidanceRepo, _priorityRoadsRepo, _verifiedPronunciationsRepo,
            _operatorAssignmentsRepo, _voiceCalibrationsRepo, _fillerStatementsRepo, _fillerAudioCache,
            _timeProvider, NullLogger<ConfigImportService>.Instance);

        var bundle = new ConfigBundleDto(
            VoiceProfiles: [
                new ConfigVoiceProfileDto("vp-ok", "OK Voice Changed", null,
                    "eleven_base", "Base", "eleven_v3", new VoiceSettingsDto())
            ],
            DeliveryStyles: [
                new ConfigDeliveryStyleDto("ds-doomed", "Doomed Style Changed", null,
                    new DeliveryStylePolicyDto("New tone", ["[calm]"], 1))
            ]);

        var plan = await sut.PlanAsync(bundle, "test", CancellationToken.None);
        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();
        plan.Value.Items.ShouldAllBe(i => i.Report.Action == ConfigImportActions.Update);

        _deliveryStyleRepo.FailNextSave = true;
        var report = await sut.ApplyAsync(plan.Value, requiresApproval: false, CancellationToken.None);

        // The batch completes: the failed item is reported per-item, the rest land.
        report.Applied.ShouldBeTrue();
        var voiceItem = report.Items.Single(i => i.Section == ConfigSections.VoiceProfiles);
        voiceItem.Outcome.ShouldBe(ConfigImportOutcomes.Applied);
        var styleItem = report.Items.Single(i => i.Section == ConfigSections.DeliveryStyles);
        styleItem.Outcome.ShouldBe(ConfigImportOutcomes.Failed);
        styleItem.Detail.ShouldNotBeNull();
        styleItem.Detail!.ShouldContain("Simulated storage outage");
        _voiceProfileRepo.Profiles.Single(p => p.Id == "vp-ok").Name.ShouldBe("OK Voice Changed");
    }

    // ── ConfigSections.ParseFilter ───────────────────────────────────

    [Fact]
    public void ParseFilter_Null_ReturnsAllTwelveSections()
    {
        var result = ConfigSections.ParseFilter(null);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(12);
        foreach (var section in ConfigSections.All)
            result.Value.ShouldContain(section, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseFilter_UnknownName_Fails()
    {
        var result = ConfigSections.ParseFilter("Voices");
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Voices");
    }

    [Fact]
    public void ParseFilter_MixedCaseSubset_ReturnsParsedSet()
    {
        var result = ConfigSections.ParseFilter("voiceProfiles, SAFETYterms");
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(ConfigSections.VoiceProfiles, StringComparer.OrdinalIgnoreCase);
        result.Value.ShouldContain(ConfigSections.SafetyTerms, StringComparer.OrdinalIgnoreCase);
    }

    // ── Unknown top-level section detection (L9) ────────────────────

    [Fact]
    public void DetectUnknownSections_TypoSectionName_IsReported()
    {
        // 'Schedule' (singular) instead of 'Schedules' — a typo that plain DTO
        // deserialization would otherwise silently drop.
        const string json = """{ "Schema": "test", "Schedule": [ { "MarketId": "sydney" } ] }""";

        var unknown = ConfigImportService.DetectUnknownSections(json);

        unknown.ShouldContain("Schedule");
    }

    [Fact]
    public void DetectUnknownSections_AllKnownSectionsAndMetadata_ReturnsEmpty()
    {
        const string json = """
            {
              "Schema": "test", "SchemaVersion": 1, "ExportedAtUtc": "2026-01-01T00:00:00Z",
              "Source": "test", "VoiceProfiles": [], "DeliveryStyles": [],
              "PronunciationDictionaries": [], "SafetyTerms": [],
              "Guidance": [], "PriorityRoads": [], "FillerStatements": [], "Schedules": []
            }
            """;

        var unknown = ConfigImportService.DetectUnknownSections(json);

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlanAsync_UnknownSectionSupplied_AddsBlockingErrorAndDoesNotNoOp()
    {
        var bundle = new ConfigBundleDto();

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None,
            unknownSections: ["Schedule"]);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        plan.Value.Items.Count.ShouldBe(1);
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Error);
        plan.Value.Items[0].Report.Key.ShouldBe("Schedule");
        plan.Value.Items[0].Report.Detail!.ShouldContain("Schedule");
    }

    [Fact]
    public async Task PlanAsync_NoUnknownSections_ReportsNoErrors()
    {
        var bundle = new ConfigBundleDto();

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();
        plan.Value.Items.ShouldBeEmpty();
    }

    // ── Cron/timezone parity with the interactive API ────────────────

    private static ConfigBundleDto ScheduleBundle(string marketId, string cron, string timezone)
    {
        return new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: marketId, Name: "Probe", Station: "ZZZ 100.1",
                Timezone: timezone, CronExpression: cron, Enabled: false,
                TargetDurationSeconds: 30, NotifyBeforePlayoutMinutes: 15, EscalationBeforePlayoutMinutes: 5,
                VoiceProfileId: "vp-test", OutputPath: "probe/traffic/",
                Coverage: new CoverageDto(-33.85, 151.20, 5.0))
        ]);
    }

    [Fact]
    public async Task PlanAsync_NewScheduleWithUnparseableCron_IsAnError()
    {
        // The interactive PUT rejects this with 400 "Invalid cron expression". Import used to plan
        // it as a clean Create, fail on apply, and report Creates=1 Errors=0 for a schedule that
        // never existed (reproduced on dev 2026-07-28).
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var plan = await _sut.PlanAsync(
            ScheduleBundle("probe-market", "totally not a cron", "Australia/Sydney"), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Error);
        plan.Value.Items[0].Report.Detail!.ShouldContain("Invalid cron expression");
    }

    [Fact]
    public async Task PlanAsync_ScheduleCronTighterThanFloor_IsAnError()
    {
        // Parses fine, so the syntax check passes; it is the FIRING RATE that is refused.
        // Across 13 markets a once-a-minute cron is ~19,000 generations plus syntheses a day.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var plan = await _sut.PlanAsync(
            ScheduleBundle("probe-market", "* * * * *", "Australia/Sydney"), "test", CancellationToken.None);

        plan.Value.HasErrors.ShouldBeTrue();
        plan.Value.Items[0].Report.Detail!.ShouldContain("more often than every 2 minutes");
    }

    [Fact]
    public async Task PlanAsync_ScheduleWithBlankName_IsAnError()
    {
        // Import inherited the interactive API's silent-clear behaviour; both now refuse it.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var bundle = new ConfigBundleDto(Schedules: [
            new ConfigScheduleDto(MarketId: "probe-market", Name: "   ", Station: "ZZZ 100.1",
                Timezone: "Australia/Sydney", CronExpression: "0 7 * * 1-5", Enabled: false,
                TargetDurationSeconds: 30, NotifyBeforePlayoutMinutes: 15, EscalationBeforePlayoutMinutes: 5,
                VoiceProfileId: "vp-test", OutputPath: "probe/traffic/",
                Coverage: new CoverageDto(-33.85, 151.20, 5.0))
        ]);

        var plan = await _sut.PlanAsync(bundle, "test", CancellationToken.None);

        plan.Value.HasErrors.ShouldBeTrue();
        plan.Value.Items[0].Report.Detail!.ShouldContain("cannot be cleared");
    }

    [Fact]
    public async Task PlanAsync_NewScheduleWithUnresolvableTimezone_IsAnError()
    {
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var plan = await _sut.PlanAsync(
            ScheduleBundle("probe-market", "0 7 * * 1-5", "Mars/Olympus"), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Error);
        plan.Value.Items[0].Report.Detail!.ShouldContain("Mars/Olympus");
    }

    [Fact]
    public async Task PlanAsync_ExistingScheduleUpdatedWithBadCron_IsAnError()
    {
        // The UPDATE path was equally unguarded: a bad cron planned as a clean Update.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));
        _scheduleRepo.Add(MarketSchedule.Create("probe-market", "Probe", "ZZZ 100.1",
            "Australia/Sydney", "0 7 * * 1-5", false, MakeCoverage(),
            "vp-test", "probe/traffic/", 30, 15, 5, Now, Now, "", "").Value);

        var plan = await _sut.PlanAsync(
            ScheduleBundle("probe-market", "garbage", "Australia/Sydney"), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Error);
    }

    [Fact]
    public async Task PlanAsync_ValidCronAndTimezone_StillPlansNormally()
    {
        // Guard against over-tightening: a legitimate new schedule must still plan as a Create.
        _voiceProfileRepo.Add(CreateExistingVoiceProfile("vp-test", "Test Voice"));

        var plan = await _sut.PlanAsync(
            ScheduleBundle("probe-market", "0 7 * * 1-5", "Australia/Sydney"), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();
        plan.Value.Items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
    }

    // ── The report must not claim a write that failed ────────────────

    [Fact]
    public void ToReport_ItemPlannedAsCreateThatFailedToApply_CountsAsAnErrorNotACreate()
    {
        // Counters used to follow the PLANNED action, so a failed apply still read as
        // "Creates: 1, Errors: 0, Applied: true" — the report is an operator's only evidence
        // that a config migration actually landed.
        var failed = new ConfigImportItemDto(
            ConfigSections.Schedules, "probe-market", "Probe", ConfigImportActions.Create,
            Detail: "storage rejected the write", Outcome: ConfigImportOutcomes.Failed);
        var applied = new ConfigImportItemDto(
            ConfigSections.Schedules, "other-market", "Other", ConfigImportActions.Create,
            Outcome: ConfigImportOutcomes.Applied);

        var report = new ConfigImportPlan([]).ToReport(
            dryRun: false, requiresApproval: false, applied: true, [failed, applied]);

        report.Creates.ShouldBe(1);
        report.Errors.ShouldBe(1);
    }

    [Fact]
    public void ToReport_DryRunCountsAreUnchanged()
    {
        // No outcomes exist on a dry run, so planned actions are still the right counters.
        var create = new ConfigImportItemDto(
            ConfigSections.Schedules, "a", "A", ConfigImportActions.Create);
        var unchanged = new ConfigImportItemDto(
            ConfigSections.Schedules, "b", "B", ConfigImportActions.Unchanged);

        var report = new ConfigImportPlan([]).ToReport(
            dryRun: true, requiresApproval: false, applied: false, [create, unchanged]);

        report.Creates.ShouldBe(1);
        report.Unchanged.ShouldBe(1);
        report.Errors.ShouldBe(0);
    }
}
