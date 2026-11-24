using System.Text;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Domain.Tests.Corpus;

/// <summary>
///     Replays every persisted dev-bulletin script through the REAL deterministic gate suite
///     (not the ad-hoc regex proxies used for today's findings) and reports, per gate and per
///     code era, exactly which historical bulletins would trip it today. Running it doubles as
///     a regression net: after a prompt or gate change, a historical script that starts tripping
///     a NEW gate is visible immediately. Both facts are gated on <c>ARN_CORPUS_SCAN=1</c>
///     (the <c>ARN_EVAL_LIVE</c> convention) so the normal suite never runs them.
/// </summary>
public sealed class DevBulletinGateScanTests(ITestOutputHelper output)
{
    private const string EnvVar = "ARN_CORPUS_SCAN";
    private const string SkipMessage =
        "Skipped: set ARN_CORPUS_SCAN=1 to run the dev-bulletin corpus gate scan (writes output/corpus-scan/gate-scan.md).";

    [Fact]
    public void GateScan_EveryDeterministicGate_OverDevBulletinHistory()
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1")
        {
            output.WriteLine(SkipMessage);
            return;
        }

        var load = DevBulletinCorpus.Load();
        var results = CorpusGateScan.Run(load.Jobs);

        string reportPath = Path.Combine(CorpusPaths.RepoRoot(), "output", "corpus-scan", "gate-scan.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, CorpusGateScan.RenderReport(load, results));

        foreach (string line in CorpusGateScan.RenderDigest(load, results))
            output.WriteLine(line);

        Assert.True(load.Jobs.Count > 0, "No replayable jobs found in the dev-bulletin corpus.");
        Assert.True(results.Count == GateSpec.All.Count, "Every enumerated gate must produce a result.");
    }

    /// <summary>
    ///     The two facts the ad-hoc regex scan could not get right, answered with the real gates:
    ///     how many historical scripts trip <see cref="RollCallDetector" />, and how many trip
    ///     <see cref="SectionOrderDetector" />.
    /// </summary>
    [Fact]
    public void RollCallAndSectionOrderFacts_FromRealGates()
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1")
        {
            output.WriteLine(SkipMessage);
            return;
        }

        var load = DevBulletinCorpus.Load();
        var results = CorpusGateScan.Run(load.Jobs);
        int rollCall = results.First(r => r.Name == "RollCall").OffendingCount;
        int sectionOrder = results.First(r => r.Name == "SectionOrder").OffendingCount;

        output.WriteLine(
            $"FACT roll-call: {rollCall} of {load.Jobs.Count} historical scripts trip RollCallDetector " +
            "(real gate, not the 4-road-names-per-sentence regex proxy).");
        output.WriteLine(
            $"FACT section-order: {sectionOrder} of {load.Jobs.Count} historical scripts trip SectionOrderDetector. " +
            "NOTE: the corpus does NOT persist the station's fill order, so SectionOrderDetector resolved the " +
            "model default weather-then-fuel order (BroadcastContextBase.FillOrder default) for every job.");

        Assert.True(load.Jobs.Count > 0, "No replayable jobs found in the dev-bulletin corpus.");
    }
}

/// <summary>One gate's verdict shape for the scan.</summary>
public sealed record Offense(string JobId, string Market, string Era, string Feedback);

public sealed record GateResult(string Name, bool IsBlocking, int OffendingCount, IReadOnlyList<Offense> Offenses);

/// <summary>
///     The deterministic gate scan itself: runs every gate over every replayable script and
///     renders the digest and the markdown report. The gate list is enumerated EXPLICITLY (no
///     reflection), mirroring <see cref="ScriptReviewLoop.ComputeViolationCounts" />'s set and
///     its blocking-versus-style split, plus the two fidelity gates the final grounding hard
///     gate runs that ComputeViolationCounts does not score (Fragment, HazardBlockage), plus
///     the three detectors that remain flat in <c>Services/</c>
///     (FakeLocation, UnselectedSource, RepeatedStatePhrase).
/// </summary>
public static class CorpusGateScan
{
    public static IReadOnlyList<GateResult> Run(IReadOnlyList<DevBulletinJob> jobs)
    {
        var results = new List<GateResult>(GateSpec.All.Count);
        foreach (var gate in GateSpec.All)
        {
            var offenses = new List<Offense>();
            foreach (var job in jobs)
            {
                var (hit, feedback) = gate.Detect(job.ScriptText, job.Context);
                if (hit)
                    offenses.Add(new Offense(job.JobId, job.Market, job.Era, feedback));
            }

            results.Add(new GateResult(gate.Name, gate.IsBlocking, offenses.Count, offenses));
        }

        return results;
    }

    public static IEnumerable<string> RenderDigest(DevBulletinCorpusLoad load, IReadOnlyList<GateResult> results)
    {
        yield return $"Dev-bulletin gate scan: {load.Jobs.Count} replayable jobs across {load.Eras.Count} eras "
            + $"(skipped {load.TotalJobs - load.Jobs.Count}: {load.SkippedNoScript} no script, "
            + $"{load.SkippedNoSnapshot} no snapshot, {load.SkippedDeserializeFailure} deserialize failure).";
        yield return "Gate x era matrix (cells = jobs tripping the gate):";
        var eraNames = load.Eras.Select(e => e.Era).ToList();
        int[] widths = eraNames.Select(n => n.Length).ToArray();
        int nameWidth = results.Max(r => r.Name.Length);
        var sb = new StringBuilder();
        sb.Append('|').Append(new string(' ', nameWidth + 2)).Append('|');
        for (int i = 0; i < eraNames.Count; i++)
            sb.Append(' ').Append(eraNames[i].PadRight(widths[i])).Append(" |");
        sb.Append(' ').Append("Total".PadRight(5)).Append(" |");
        yield return sb.ToString();
        foreach (var result in results)
        {
            sb.Clear();
            sb.Append("| ").Append(result.Name.PadRight(nameWidth)).Append(" |");
            int total = 0;
            for (int i = 0; i < eraNames.Count; i++)
            {
                int count = result.Offenses.Count(o => o.Era == eraNames[i]);
                total += count;
                sb.Append(' ').Append(count.ToString().PadRight(widths[i])).Append(" |");
            }
            sb.Append(' ').Append(total.ToString().PadRight(5)).Append(" |");
            yield return sb.ToString();
        }
    }

    public static string RenderReport(DevBulletinCorpusLoad load, IReadOnlyList<GateResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dev-bulletin corpus gate scan");
        sb.AppendLine();
        sb.AppendLine($"Corpus: `test/corpus/dev-bulletins/`. Snapshot date: see `MANIFEST.json` (`snapshotUtc`).");
        sb.AppendLine();
        sb.AppendLine(
            $"{load.Jobs.Count} replayable jobs (of {load.TotalJobs} total; skipped "
            + $"{load.SkippedNoScript} with no script, {load.SkippedNoSnapshot} with no generator-input "
            + $"snapshot, {load.SkippedDeserializeFailure} whose snapshot failed to deserialise"
            + (load.DeserializeFailureDetails.Count > 0
                ? $": {string.Join("; ", load.DeserializeFailureDetails)}" : string.Empty)
            + ").");
        sb.AppendLine();
        sb.AppendLine(
            "Every gate below is the REAL deterministic detector, enumerated explicitly and run with the "
            + "writer context rebuilt from each job's `GeneratorInputJson` snapshot. Blocking/style split "
            + "mirrors `ScriptReviewLoop.ComputeViolationCounts`; the two fidelity gates the final "
            + "grounding hard gate adds (Fragment, HazardBlockage) are listed as blocking.");
        sb.AppendLine();
        sb.AppendLine("## Gate x era matrix");
        sb.AppendLine();
        sb.AppendLine("Cells = number of jobs in that era whose script trips the gate today.");
        sb.AppendLine();
        var eraNames = load.Eras.Select(e => e.Era).ToList();
        sb.Append("| Gate |");
        foreach (var era in eraNames)
            sb.Append(' ').Append(era).Append(" |");
        sb.AppendLine(" Total |");
        sb.Append("|---|");
        foreach (var _ in eraNames)
            sb.Append("---|");
        sb.AppendLine("---:|");
        foreach (var result in results)
        {
            sb.Append("| ").Append(result.Name).Append(" |");
            int total = 0;
            foreach (var era in eraNames)
            {
                int count = result.Offenses.Count(o => o.Era == era);
                total += count;
                sb.Append(' ').Append(count).Append(" |");
            }
            sb.Append(' ').Append(total).Append(" |");
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## The two facts the ad-hoc regex scan could not get right");
        sb.AppendLine();
        int rollCall = results.First(r => r.Name == "RollCall").OffendingCount;
        int sectionOrder = results.First(r => r.Name == "SectionOrder").OffendingCount;
        sb.AppendLine(
            $"- **RollCall**: {rollCall} of {load.Jobs.Count} historical scripts trip `RollCallDetector` "
            + "(the real gate, not a \"4+ road names in one sentence\" regex proxy).");
        sb.AppendLine(
            $"- **SectionOrder**: {sectionOrder} of {load.Jobs.Count} historical scripts trip "
            + "`SectionOrderDetector`. NOTE: the corpus does NOT persist the station's fill order, so the "
            + "detector resolved the model default weather-then-fuel order "
            + "(`BroadcastContextBase.FillOrder` default) for every job — a station whose on-air order "
            + "was fuel-then-weather would score differently in reality.");
        sb.AppendLine();

        foreach (var result in results)
        {
            sb.Append("## ").AppendLine(result.Name);
            sb.AppendLine();
            sb.AppendLine(
                $"Trips {result.OffendingCount} of {load.Jobs.Count} replayable jobs. "
                + (result.IsBlocking ? "Blocking (fidelity/safety)" : "Style") + " gate.");
            sb.AppendLine();
            if (result.OffendingCount == 0)
            {
                sb.AppendLine("_No offender._");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("First 5 offenders (feedback truncated to ~140 chars):");
            sb.AppendLine();
            sb.AppendLine("| JobId | Market | Era | Feedback |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var offense in result.Offenses.Take(5))
            {
                string feedback = Truncate(offense.Feedback, 140);
                sb.Append("| `").Append(offense.JobId).Append("` | ").Append(offense.Market)
                    .Append(" | ").Append(offense.Era).Append(" | ").Append(EscapeMarkdown(feedback))
                    .AppendLine(" |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
            return text;
        return text[..(max - 3)] + "...";
    }

    private static string EscapeMarkdown(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
}

/// <summary>One explicitly enumerated gate: name, blocking/style bucket, and the detector call.</summary>
public sealed record GateSpec(
    string Name,
    bool IsBlocking,
    Func<string, BroadcastWithTraffic, (bool Hit, string Feedback)> Detect)
{
    private const bool Blocking = true;
    private const bool Style = false;

    /// <summary>
    ///     Mirrors <c>ScriptReviewLoop.ComputeViolationCounts</c> exactly (same detectors, same
    ///     blocking/style buckets, same order), then adds the three fidelity gates the final
    ///     grounding hard gate runs but ComputeViolationCounts does not score. No reflection.
    /// </summary>
    public static IReadOnlyList<GateSpec> All { get; } =
    [
        // Style bucket — ComputeViolationCounts style count.
        new("RollCall", Style, static (s, c) => { var r = RollCallDetector.Detect(s, c); return (r.HasRollCall, r.Feedback); }),
        new("DuplicateAnchor", Style, static (s, c) => { var r = DuplicateAnchorDetector.Detect(s, c); return (r.HasDuplicateAnchor, r.Feedback); }),
        new("RepeatedPreposition", Style, static (s, _) => { var r = RepeatedPrepositionDetector.Detect(s); return (r.HasRepeatedPreposition, r.Feedback); }),
        new("ImpactStack", Style, static (s, _) => { var r = ImpactStackDetector.Detect(s); return (r.HasImpactStack, r.Feedback); }),
        new("SectionOrder", Style, static (s, c) => { var r = SectionOrderDetector.Detect(s, c); return (r.HasOutOfOrderSection, r.Feedback); }),
        new("RepeatedStatePhrase", Style, static (s, _) => { var r = RepeatedStatePhraseDetector.Detect(s); return (r.HasExcessRepetition, r.Feedback); }),
        new("SplitClearRun", Style, static (s, _) => { var r = SplitClearRunDetector.Detect(s); return (r.HasSplitClearRun, r.Feedback); }),
        new("ConnectorFrequency", Style, static (s, _) => { var r = ConnectorFrequencyDetector.Detect(s); return (r.HasOveruse, r.Feedback); }),
        new("RedundantTail", Style, static (s, _) => { var r = RedundantTailDetector.Detect(s); return (r.HasRedundantTail, r.Feedback); }),
        new("Vagueness", Style, static (s, _) => { var r = VaguenessDetector.Detect(s); return (r.HasVagueness, r.Feedback); }),
        // Blocking bucket — ComputeViolationCounts blocking count.
        new("MetaLeak", Blocking, static (s, _) => { var r = MetaLeakDetector.Detect(s); return (r.HasMetaLeak, r.Feedback); }),
        new("IncidentTypeStack", Blocking, static (s, _) => { var r = IncidentTypeStackDetector.Detect(s); return (r.HasTypeStack, r.Feedback); }),
        new("FakeLocation", Blocking, static (s, _) => { var r = FakeLocationDetector.Detect(s); return (r.HasFakeLocation, r.Feedback); }),
        new("UnselectedSource", Blocking, static (s, c) => { var r = UnselectedSourceDetector.Detect(s, c); return (r.HasFabrication, r.Feedback); }),
        new("RequiredSource", Blocking, static (s, c) => { var r = RequiredSourceDetector.Detect(s, c); return (r.HasMissingSource, r.Feedback); }),
        new("SuburbRetention", Blocking, static (s, c) => { var r = SuburbRetentionDetector.Detect(s, c); return (r.HasMissingSuburb, r.Feedback); }),
        new("SentenceClone", Blocking, static (s, c) => { var r = SentenceCloneDetector.Detect(s, c); return (r.HasClone, r.Feedback); }),
        new("FalseClosure", Blocking, static (s, c) => { var r = FalseClosureDetector.Detect(s, c); return (r.HasFalseClosure, r.Feedback); }),
        // Fidelity gates the final grounding hard gate runs, not scored by ComputeViolationCounts.
        new("Fragment", Blocking, static (s, _) => { var r = FragmentDetector.Detect(s); return (r.HasFragment, r.Feedback); }),
        new("HazardBlockage", Blocking, static (s, c) => { var r = HazardBlockageDetector.Detect(s, c); return (r.HasHazardBlockage, r.Feedback); }),
    ];
}
