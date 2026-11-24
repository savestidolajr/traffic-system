using System.Diagnostics;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     Buys semantic content against a word budget. Whole stories are fitted or dropped whole,
///     except splittable group candidates, whose members are bought one at a time so a seven-road
///     sweep can shed four roads instead of dying whole while the read runs 15 seconds under its
///     slot (measured Adelaide, 2026-08-26). Selection is kept separate from prose so fitting can
///     never strand a connective or leave half a required fact behind, and the planner's story
///     order remains the composer's order.
/// </summary>
public static class Fitter
{
    /// <summary>
    ///     One planned story entering the budget. A splittable group candidate carries one
    ///     <see cref="MemberCandidate" /> per <see cref="PlannedStory.Members" /> entry, each priced
    ///     independently so the fitter can voice a subset.
    /// </summary>
    private sealed class StoryCandidate
    {
        public required int Index { get; init; }
        public required PlannedStory Story { get; init; }
        public required BulletinEvent[] Events { get; init; }
        public required IReadOnlyList<string> RequiredEvidenceIds { get; init; }
        public required int RequiredWords { get; init; }
        public required bool Mandatory { get; init; }
        public required IReadOnlyList<string> MissingEventIds { get; init; }
        public bool IsGroup => Story.IsSplittableGroup;
        public bool Kept { get; set; }
        public List<MemberCandidate> Members { get; } = [];
        public List<OptionalPurchase> OptionalPurchases { get; } = [];
        public List<FitDecision> MemberDecisions { get; } = [];
        public List<FitDecision> PurchaseDecisions { get; } = [];

        public IEnumerable<MemberCandidate> SelectedMembers =>
            Members.Where(member => member.Kept);
    }

    private sealed class MemberCandidate
    {
        public required string MemberId { get; init; }
        public required int Order { get; init; }
        public required string EventId { get; init; }
        public required BulletinEvent? Event { get; init; }
        public required IReadOnlyList<string> RequiredEvidenceIds { get; init; }
        public required IReadOnlyList<OptionalFact> OptionalFacts { get; init; }
        public required int Priority { get; init; }
        public required int Cost { get; init; }
        public bool Missing => Event is null;
        public bool Kept { get; set; }
        public bool Decided { get; set; }
    }

    private sealed class OptionalPurchase
    {
        public required int Index { get; init; }
        public required OptionalFact Fact { get; init; }
        public required int Words { get; init; }

        /// <summary>Owning group member, or null for a story-level optional.</summary>
        public required string? MemberId { get; init; }

        public bool Kept { get; set; }
    }

    /// <summary>
    ///     One purchasable item in the shared tail pool: an unbought group member or an optional
    ///     fact. Remaining group members compete with optional details at equal rank because a
    ///     group that voices three of seven roads beats a group that voices none.
    /// </summary>
    private sealed class PoolItem
    {
        public required int Priority { get; init; }
        public required int StoryIndex { get; init; }

        /// <summary>Members sort ahead of optional facts at equal priority: substance before decoration.</summary>
        public required int KindRank { get; init; }

        public required int InnerIndex { get; init; }
        public required bool PreviouslyKept { get; init; }
        public MemberCandidate? Member { get; init; }
        public StoryCandidate? Owner { get; init; }
        public OptionalPurchase? Purchase { get; init; }
    }

    private sealed record SelectionResult(
        IReadOnlyList<StoryCandidate> Candidates,
        IReadOnlyList<FitDecision> Decisions,
        int BudgetedWords);

    /// <summary>Fits a plan once, preserving its order and making mandatory fixtures non-droppable.</summary>
    public static FittedPlan Fit(
        BulletinPlan plan,
        NormalizedInput input,
        int targetWords,
        TraceRecorder trace)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(trace);

        var stopwatch = Stopwatch.StartNew();
        trace.StageStart("Fit", $"Fitting {plan.Stories.Count} stories to {targetWords} words");
        var result = Select(plan, input, targetWords, previous: null, stable: false);
        var fitted = BuildPlan(result, targetWords);
        RecordTrace(trace, plan, targetWords, fitted, result.Decisions, stopwatch.Elapsed.TotalMilliseconds);
        return fitted;
    }

    /// <summary>
    ///     Re-fits after a duration correction while preferring stories - and individual group
    ///     members - that survived the prior fit. This prevents a small target change from
    ///     replacing the read's established content.
    /// </summary>
    public static FittedPlan Refit(
        FittedPlan previous,
        BulletinPlan plan,
        NormalizedInput input,
        int newTargetWords,
        TraceRecorder trace)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(trace);

        var stopwatch = Stopwatch.StartNew();
        trace.StageStart("Fit", $"Refitting {plan.Stories.Count} stories to {newTargetWords} words");
        var result = Select(plan, input, newTargetWords, previous, stable: true);
        var decisions = AddRefitReasons(result.Candidates, result.Decisions, previous, newTargetWords);
        var fitted = BuildPlan(result with { Decisions = decisions }, newTargetWords);
        RecordTrace(trace, plan, newTargetWords, fitted, decisions, stopwatch.Elapsed.TotalMilliseconds);
        return fitted;
    }

    private static SelectionResult Select(
        BulletinPlan plan,
        NormalizedInput input,
        int targetWords,
        FittedPlan? previous,
        bool stable)
    {
        if (plan.Stories.Count == 0)
        {
            return new SelectionResult([], [], 0);
        }

        var requiredEvents = new HashSet<string>(input.RequiredEventIds, StringComparer.Ordinal);
        var candidates = plan.Stories
            .Select((story, index) => CreateCandidate(story, index, input, requiredEvents))
            .ToList();

        var previousKept = new HashSet<string>(StringComparer.Ordinal);
        var previousMembers = new HashSet<string>(StringComparer.Ordinal);
        if (previous is not null)
        {
            foreach (var story in previous.Stories)
            {
                previousKept.Add(story.StoryId);
                if (story.Members is not null)
                {
                    previousMembers.UnionWith(story.Members);
                }
            }
        }

        var budgeted = 0;
        var storyDecisions = new Dictionary<int, FitDecision>();

        // Mandatory whole stories are voiced whatever the budget does: only station-required
        // events can make a story mandatory; selected weather and fuel remain eligible content.
        foreach (var candidate in candidates.Where(c => c.Mandatory && !c.IsGroup && c.MissingEventIds.Count == 0))
        {
            candidate.Kept = true;
            budgeted += candidate.RequiredWords;
            storyDecisions[candidate.Index] = new FitDecision(
                candidate.Story.StoryId,
                true,
                candidate.RequiredWords,
                MandatoryReason(candidate, budgeted, targetWords));
        }

        foreach (var candidate in candidates.Where(c => !c.IsGroup && c.MissingEventIds.Count > 0))
        {
            storyDecisions[candidate.Index] = new FitDecision(
                candidate.Story.StoryId,
                false,
                candidate.RequiredWords,
                $"dropped: references missing event(s): {string.Join(", ", candidate.MissingEventIds)}");
        }

        // Missing members are a data fact, not a budget one, so they are marked for every group up
        // front and never compete for words.
        foreach (var groupCandidate in candidates.Where(c => c.IsGroup))
        {
            foreach (var member in groupCandidate.Members.Where(member => member.Missing))
            {
                member.Decided = true;
                groupCandidate.MemberDecisions.Add(new FitDecision(
                    member.MemberId,
                    false,
                    member.Cost,
                    $"dropped from group {groupCandidate.Story.StoryId}: references missing event {member.EventId}"));
            }
        }

        // Required content for every non-mandatory candidate, plain story AND group head, in ONE
        // priority-ordered pass. Buying all plain stories first cost Adelaide its lead: measured
        // live 2026-08-26 the p95 James Congdon Drive / South Road group was evaluated only after
        // p52, p65 and p38 plain stories had taken the budget, leaving 3 words, and the highest
        // priority story in the bulletin was dropped with "no member fits within the word budget".
        // With the one distinctive story gone the read collapsed into four interchangeable
        // "roadworks at X" lines. Priority has to mean the same thing for both shapes.
        var nonMandatory = candidates
            .Where(candidate => !candidate.Mandatory && (candidate.IsGroup || candidate.MissingEventIds.Count == 0))
            .OrderBy(candidate => stable && previousKept.Contains(candidate.Story.StoryId) ? 0 : 1)
            .ThenBy(candidate => stable && previousKept.Contains(candidate.Story.StoryId)
                ? candidate.Index
                : int.MaxValue)
            .ThenByDescending(candidate => candidate.Story.Priority)
            .ThenBy(candidate => candidate.Index);

        foreach (var candidate in nonMandatory)
        {
            if (candidate.IsGroup)
            {
                var groupMembers = ResolvableMembers(candidate, stable, previousMembers);
                if (groupMembers.Count == 0)
                    continue;

                var groupHead = groupMembers[0];
                groupHead.Decided = true;
                if (Fits(budgeted, groupHead.Cost, targetWords))
                {
                    groupHead.Kept = true;
                    candidate.Kept = true;
                    budgeted += groupHead.Cost;
                    candidate.MemberDecisions.Add(new FitDecision(
                        groupHead.MemberId,
                        true,
                        groupHead.Cost,
                        HeadKeptReason(candidate, groupHead.Cost, false, budgeted, targetWords)));
                }
                else
                {
                    candidate.MemberDecisions.Add(new FitDecision(
                        groupHead.MemberId,
                        false,
                        groupHead.Cost,
                        MemberDropReason(candidate, groupHead.Cost, budgeted, targetWords)));
                }

                continue;
            }

            if (Fits(budgeted, candidate.RequiredWords, targetWords))
            {
                candidate.Kept = true;
                budgeted += candidate.RequiredWords;
                storyDecisions[candidate.Index] = new FitDecision(
                    candidate.Story.StoryId,
                    true,
                    candidate.RequiredWords,
                    "kept: fits within the remaining word budget");
            }
            else
            {
                storyDecisions[candidate.Index] = new FitDecision(
                    candidate.Story.StoryId,
                    false,
                    candidate.RequiredWords,
                    DropReason(candidate.RequiredWords, budgeted, targetWords));
            }
        }

        // One member per affordable group is bought before anything optional anywhere, because a
        // group that voices nothing is worse than a group that voices three of seven roads
        // (Adelaide, 2026-08-26: all seven minor-road events dropped whole under a 15-second
        // underspend). A mandatory group satisfies its obligation with its first affordable
        // member, overshoot permitted, exactly like a mandatory whole story.
        foreach (var candidate in candidates.Where(c => c.IsGroup && c.Mandatory))
        {
            var resolvable = ResolvableMembers(candidate, stable, previousMembers);
            if (resolvable.Count == 0)
            {
                continue;
            }

            var head = resolvable[0];
            head.Decided = true;
            var forced = candidate.Mandatory;
            if (forced || Fits(budgeted, head.Cost, targetWords))
            {
                head.Kept = true;
                budgeted += head.Cost;
                candidate.MemberDecisions.Add(new FitDecision(
                    head.MemberId,
                    true,
                    head.Cost,
                    HeadKeptReason(candidate, head.Cost, forced, budgeted, targetWords)));
            }
            else
            {
                candidate.MemberDecisions.Add(new FitDecision(
                    head.MemberId,
                    false,
                    head.Cost,
                    MemberDropReason(candidate, head.Cost, budgeted, targetWords)));
            }
        }

        // Shared tail pool: remaining group members and optional facts, bought together in
        // descending priority. Story-level and member-level optionals ride only on content that
        // is already voiced; a group whose first member did not fit earns its optionals only if
        // one of its members is bought from this pool.
        var pool = new List<PoolItem>();
        foreach (var candidate in candidates)
        {
            if (!candidate.Kept)
            {
                continue;
            }

            var nextIndex = 0;
            foreach (var fact in candidate.Story.OptionalFacts)
            {
                var purchase = new OptionalPurchase
                {
                    Index = nextIndex++,
                    Fact = fact,
                    Words = OptionalWords(candidate.Events, fact),
                    MemberId = null,
                };
                pool.Add(new PoolItem
                {
                    Priority = fact.Priority,
                    StoryIndex = candidate.Index,
                    KindRank = 1,
                    InnerIndex = purchase.Index,
                    PreviouslyKept = false,
                    Owner = candidate,
                    Purchase = purchase,
                });
            }

            foreach (var member in candidate.SelectedMembers)
            {
                foreach (var fact in member.OptionalFacts)
                {
                    var purchase = new OptionalPurchase
                    {
                        Index = nextIndex++,
                        Fact = fact,
                        Words = OptionalWords([member.Event!], fact),
                        MemberId = member.MemberId,
                    };
                    pool.Add(new PoolItem
                    {
                        Priority = fact.Priority,
                        StoryIndex = candidate.Index,
                        KindRank = 1,
                        InnerIndex = purchase.Index,
                        PreviouslyKept = false,
                        Owner = candidate,
                        Purchase = purchase,
                    });
                }
            }
        }

        foreach (var candidate in candidates.Where(c => c.IsGroup))
        {
            foreach (var member in candidate.Members.Where(member => !member.Missing && !member.Decided))
            {
                pool.Add(new PoolItem
                {
                    Priority = member.Priority,
                    StoryIndex = candidate.Index,
                    KindRank = 0,
                    InnerIndex = member.Order,
                    PreviouslyKept = stable && previousMembers.Contains(member.MemberId),
                    Owner = candidate,
                    Member = member,
                });
            }
        }

        foreach (var item in pool
                     .OrderByDescending(item => item.PreviouslyKept)
                     .ThenByDescending(item => item.Priority)
                     .ThenBy(item => item.StoryIndex)
                     .ThenBy(item => item.KindRank)
                     .ThenBy(item => item.InnerIndex))
        {
            if (item.Member is not null && item.Owner is not null)
            {
                var member = item.Member;
                member.Decided = true;
                if (Fits(budgeted, member.Cost, targetWords))
                {
                    member.Kept = true;
                    budgeted += member.Cost;
                    item.Owner.MemberDecisions.Add(new FitDecision(
                        member.MemberId,
                        true,
                        member.Cost,
                        $"kept in group {item.Owner.Story.StoryId}: {member.Cost} words"));
                }
                else
                {
                    item.Owner.MemberDecisions.Add(new FitDecision(
                        member.MemberId,
                        false,
                        member.Cost,
                        MemberDropReason(item.Owner, member.Cost, budgeted, targetWords)));
                }
            }
            else if (item.Purchase is not null && item.Owner is not null)
            {
                var purchase = item.Purchase;
                var fits = Fits(budgeted, purchase.Words, targetWords);
                purchase.Kept = fits;
                item.Owner.OptionalPurchases.Add(purchase);
                if (fits)
                {
                    budgeted += purchase.Words;
                }

                item.Owner.PurchaseDecisions.Add(new FitDecision(
                    OptionalSubject(item.Owner.Story, purchase.MemberId, purchase.Index, purchase.Fact),
                    fits,
                    purchase.Words,
                    fits
                        ? "kept: optional detail fits in the remaining word budget"
                        : DropReason(purchase.Words, budgeted, targetWords)));
            }
        }

        // A group contributes when at least one member survived; only a group that voiced
        // nothing is dropped, and never partially voiced prose is left behind.
        foreach (var candidate in candidates.Where(c => c.IsGroup))
        {
            var selected = candidate.SelectedMembers.ToList();
            if (selected.Count > 0)
            {
                candidate.Kept = true;
                storyDecisions[candidate.Index] = new FitDecision(
                    candidate.Story.StoryId,
                    true,
                    selected.Sum(member => member.Cost),
                    $"kept: {selected.Count} of {candidate.Members.Count} members fit within the word budget");
            }
            else
            {
                candidate.Kept = false;
                storyDecisions[candidate.Index] = new FitDecision(
                    candidate.Story.StoryId,
                    false,
                    candidate.Members.Where(member => !member.Missing).Sum(member => member.Cost),
                    candidate.Members.All(member => member.Missing)
                        ? $"dropped: all {candidate.Members.Count} member(s) reference missing events"
                        : "dropped: no member fits within the word budget");
            }
        }

        var orderedDecisions = candidates
            .OrderBy(candidate => candidate.Index)
            .SelectMany(candidate => new[] { storyDecisions[candidate.Index] }
                .Concat(candidate.MemberDecisions)
                .Concat(candidate.PurchaseDecisions))
            .ToList();
        return new SelectionResult(candidates, orderedDecisions, budgeted);
    }

    private static StoryCandidate CreateCandidate(
        PlannedStory story,
        int index,
        NormalizedInput input,
        IReadOnlySet<string> requiredEvents)
    {
        if (story.IsSplittableGroup)
        {
            // Group candidates are priced per member; the story-level figures stay empty because
            // acceptance is decided member by member, never on the story's aggregate cost.
            var candidate = new StoryCandidate
            {
                Index = index,
                Story = story,
                Events = [],
                RequiredEvidenceIds = [],
                RequiredWords = 0,
                Mandatory = story.EventIds.Any(requiredEvents.Contains),
                MissingEventIds = [],
            };
            var members = story.Members!;
            for (var order = 0; order < members.Count; order++)
            {
                var member = members[order];
                var requiredEvidenceIds = DistinctStable(member.RequiredFacts);
                var bulletinEvent = input.Event(member.EventId);
                candidate.Members.Add(new MemberCandidate
                {
                    MemberId = member.MemberId,
                    Order = order,
                    EventId = member.EventId,
                    Event = bulletinEvent,
                    RequiredEvidenceIds = requiredEvidenceIds,
                    OptionalFacts = member.OptionalFacts,
                    Priority = member.Priority,
                    Cost = bulletinEvent is null
                        ? 0
                        : WordEstimator.EstimateStoryWords([bulletinEvent], requiredEvidenceIds),
                });
            }

            return candidate;
        }

        var events = new List<BulletinEvent>();
        var missing = new List<string>();
        var seenEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventId in story.EventIds)
        {
            if (!seenEvents.Add(eventId))
            {
                continue;
            }

            var bulletinEvent = input.Event(eventId);
            if (bulletinEvent is null)
            {
                missing.Add(eventId);
            }
            else
            {
                events.Add(bulletinEvent);
            }
        }

        var requiredIds = DistinctStable(story.RequiredFacts);
        return new StoryCandidate
        {
            Index = index,
            Story = story,
            Events = events.ToArray(),
            RequiredEvidenceIds = requiredIds,
            RequiredWords = WordEstimator.EstimateStoryWords(events.ToArray(), requiredIds),
            Mandatory = story.EventIds.Any(requiredEvents.Contains),
            MissingEventIds = missing,
        };
    }

    /// <summary>Resolvable members in buy order: previously-kept first, then priority, then planner order.</summary>
    private static List<MemberCandidate> ResolvableMembers(
        StoryCandidate candidate,
        bool stable,
        IReadOnlySet<string> previousMembers) =>
        candidate.Members
            .Where(member => !member.Missing)
            .OrderByDescending(member => stable && previousMembers.Contains(member.MemberId))
            .ThenByDescending(member => member.Priority)
            .ThenBy(member => member.Order)
            .ToList();

    private static bool Fits(int budgeted, int words, int targetWords) =>
        targetWords > 0 && budgeted + words <= targetWords;

    private static int OptionalWords(BulletinEvent[] events, OptionalFact fact)
    {
        if (fact.EstimatedWords > 0)
        {
            return fact.EstimatedWords;
        }

        var ids = new HashSet<string>(fact.EvidenceIds, StringComparer.Ordinal);
        var words = 0;
        foreach (var bulletinEvent in events)
        {
            foreach (var eventFact in bulletinEvent.Facts)
            {
                if (ids.Contains(eventFact.EvidenceId))
                {
                    words += WordEstimator.EstimateFactWords(eventFact);
                }
            }
        }

        return words;
    }

    private static FittedPlan BuildPlan(SelectionResult result, int targetWords)
    {
        var stories = result.Candidates
            .Where(candidate => candidate.Kept)
            .OrderBy(candidate => candidate.Index)
            .Select(candidate =>
            {
                var mustExpress = MustExpressFor(candidate);
                return new FittedStory(
                    candidate.Story.StoryId,
                    candidate.IsGroup
                        ? candidate.SelectedMembers.Select(member => member.EventId).ToList()
                        : candidate.Story.EventIds,
                    candidate.Story.Role,
                    mustExpress,
                    AuthorizedFor(candidate, mustExpress),
                    EstimatedWordsFor(candidate),
                    candidate.Story.Relationship,
                    candidate.IsGroup
                        ? candidate.SelectedMembers.Select(member => member.MemberId).ToList()
                        : null);
            })
            .ToList();
        return new FittedPlan(stories, targetWords, result.Decisions);
    }

    /// <summary>
    ///     Everything the budget was SPENT on: the story's required facts plus every optional
    ///     actually bought. The composer is obliged to voice these - handing it merely permission
    ///     produced a 69-word read against an 82-word budget and a shrinking treadmill on every
    ///     revision (Adelaide, 2026-08-26).
    /// </summary>
    private static IReadOnlyList<string> MustExpressFor(StoryCandidate candidate)
    {
        var evidence = new List<string>();
        if (candidate.IsGroup)
        {
            foreach (var member in candidate.SelectedMembers)
            {
                evidence.AddRange(member.RequiredEvidenceIds);
            }
        }
        else
        {
            evidence.AddRange(candidate.RequiredEvidenceIds);
        }

        foreach (var purchase in candidate.OptionalPurchases
                     .Where(purchase => purchase.Kept)
                     .OrderBy(purchase => purchase.Index))
        {
            evidence.AddRange(purchase.Fact.EvidenceIds);
        }

        return DistinctStable(evidence);
    }

    /// <summary>
    ///     Must-express plus every remaining evidence id of the voiced events. Citing these is
    ///     free: the composer may name the suburb it is already flying past without the fitter
    ///     having priced that mention first.
    /// </summary>
    private static IReadOnlyList<string> AuthorizedFor(
        StoryCandidate candidate,
        IReadOnlyList<string> mustExpress)
    {
        var evidence = new List<string>(mustExpress);
        var seen = new HashSet<string>(evidence, StringComparer.Ordinal);
        var events = candidate.IsGroup
            ? candidate.SelectedMembers.Select(member => member.Event!)
            : candidate.Events;
        foreach (var bulletinEvent in events)
        {
            foreach (var evidenceId in bulletinEvent.EvidenceIds)
            {
                if (seen.Add(evidenceId))
                {
                    evidence.Add(evidenceId);
                }
            }
        }

        return evidence;
    }

    private static int EstimatedWordsFor(StoryCandidate candidate) =>
        (candidate.IsGroup
            ? candidate.SelectedMembers.Sum(member => member.Cost)
            : candidate.RequiredWords)
        + candidate.OptionalPurchases.Where(purchase => purchase.Kept).Sum(purchase => purchase.Words);

    private static IReadOnlyList<FitDecision> AddRefitReasons(
        IReadOnlyList<StoryCandidate> candidates,
        IReadOnlyList<FitDecision> decisions,
        FittedPlan previous,
        int newTargetWords)
    {
        var previousKept = new HashSet<string>(StringComparer.Ordinal);
        var previousMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var story in previous.Stories)
        {
            previousKept.Add(story.StoryId);
            if (story.Members is not null)
            {
                previousMembers.UnionWith(story.Members);
            }
        }

        var previousStories = new Dictionary<string, FittedStory>(StringComparer.Ordinal);
        foreach (var story in previous.Stories)
        {
            previousStories.TryAdd(story.StoryId, story);
        }

        var previousOptional = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!previousStories.TryGetValue(candidate.Story.StoryId, out var previousStory))
            {
                continue;
            }

            var previousEvidence = new HashSet<string>(previousStory.EvidenceIds, StringComparer.Ordinal);
            foreach (var purchase in candidate.OptionalPurchases)
            {
                var ids = purchase.Fact.EvidenceIds;
                previousOptional[OptionalSubject(candidate.Story, purchase.MemberId, purchase.Index, purchase.Fact)] =
                    ids.Count > 0 && ids.All(previousEvidence.Contains);
            }
        }

        var oldTarget = previous.TargetWords;
        var delta = newTargetWords < oldTarget
            ? $"target fell {oldTarget} -> {newTargetWords} words"
            : $"target changed {oldTarget} -> {newTargetWords} words";
        return decisions.Select(decision =>
        {
            var wasKept = previousOptional.TryGetValue(decision.Subject, out var optionalWasKept)
                ? optionalWasKept
                : previousKept.Contains(decision.Subject) || previousMembers.Contains(decision.Subject);
            var wasConsidered = previousOptional.ContainsKey(decision.Subject)
                || previousKept.Contains(decision.Subject)
                || previousMembers.Contains(decision.Subject);
            if (wasConsidered && wasKept != decision.Kept)
            {
                var action = decision.Kept ? "kept" : "dropped";
                return decision with
                {
                    Reason = $"{action} on refit: {delta}; {decision.Reason}",
                };
            }

            return decision;
        }).ToList();
    }

    private static void RecordTrace(
        TraceRecorder trace,
        BulletinPlan plan,
        int targetWords,
        FittedPlan fitted,
        IReadOnlyList<FitDecision> decisions,
        double durationMs)
    {
        var artifact = decisions.ToDictionary(
            decision => decision.Subject,
            decision => decision.Kept
                ? $"KEPT {decision.Words}w - {decision.Reason}"
                : $"DROPPED - {decision.Reason}",
            StringComparer.Ordinal);
        trace.Artifact("Fit", "Selection", artifact);
        trace.Decision("Fit", $"{fitted.Stories.Count}/{plan.Stories.Count} stories, {fitted.Stories.Sum(story => story.EstimatedWords)}/{targetWords} words");
        trace.StageEnd("Fit", $"Selected {fitted.Stories.Count} of {plan.Stories.Count} stories", durationMs);
    }

    private static string MandatoryReason(StoryCandidate candidate, int budgeted, int targetWords)
    {
        var fixture = candidate.Story.Role is StoryRole.Weather or StoryRole.Fuel
            ? $"mandatory {candidate.Story.Role.ToString().ToLowerInvariant()} fixture"
            : "mandatory station fixture";
        return budgeted > targetWords
            ? $"kept: {fixture}; mandatory content overshoots the target by {budgeted - targetWords} words"
            : $"kept: {fixture}";
    }

    private static string HeadKeptReason(
        StoryCandidate candidate,
        int cost,
        bool forced,
        int budgeted,
        int targetWords)
    {
        var reason = $"kept in group {candidate.Story.StoryId}: {cost} words";
        if (forced && budgeted > targetWords)
        {
            reason += targetWords <= 0
                ? "; mandatory fixture is voiced whatever the target"
                : $"; mandatory fixture overshoots the {targetWords}-word target";
        }

        return reason;
    }

    private static string MemberDropReason(StoryCandidate candidate, int words, int budgeted, int targetWords)
    {
        if (targetWords <= 0)
        {
            return $"dropped from group {candidate.Story.StoryId}: target is {targetWords} words; non-mandatory members cannot fit";
        }

        var overshoot = Math.Max(0, budgeted + words - targetWords);
        return $"dropped from group {candidate.Story.StoryId}: {words} words would overshoot the {targetWords}-word target by {overshoot}";
    }

    private static string DropReason(int words, int budgeted, int targetWords)
    {
        if (targetWords <= 0)
        {
            return $"dropped: target is {targetWords} words; non-mandatory content cannot fit";
        }

        var overshoot = Math.Max(0, budgeted + words - targetWords);
        return $"dropped: {words} words would overshoot the {targetWords}-word target by {overshoot}";
    }

    private static IReadOnlyList<string> DistinctStable(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static string OptionalSubject(PlannedStory story, string? memberId, int index, OptionalFact fact) =>
        memberId is null
            ? $"{story.StoryId} optional {index + 1} ({string.Join(", ", fact.EvidenceIds)})"
            : $"{memberId} optional {index + 1} ({string.Join(", ", fact.EvidenceIds)})";

}
