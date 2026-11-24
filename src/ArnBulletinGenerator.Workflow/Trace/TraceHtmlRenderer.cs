using System.Globalization;
using System.Text;

namespace ArnBulletinGenerator.Workflow.Trace;

/// <summary>
///     Renders the trace as a deliberately boring, self-contained forensic record. The prompts and
///     responses are the audit trail: keeping them verbatim in native disclosure panes means a
///     run remains explainable from a copied <c>file://</c> artifact, without a server or script.
/// </summary>
public static class TraceHtmlRenderer
{
    private const string Styles = """
        :root{--bg:#0d1117;--bg2:#161b22;--bg3:#21262d;--border:#30363d;--text:#e6edf3;--dim:#8b949e;--bright:#f0f6fc;--blue:#58a6ff;--green:#3fb950;--orange:#d29922;--red:#f85149;--purple:#bc8cff;--cyan:#39dada;--pink:#f778ba}
        *{box-sizing:border-box}
        html{background:var(--bg)}
        body{margin:0;background:var(--bg);color:var(--text);font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Arial,sans-serif;line-height:1.5}
        .header{background:linear-gradient(135deg,#0d1117,#161b22);border-bottom:1px solid var(--border);padding:22px 32px}
        .header-inner,.container{max-width:1200px;margin:0 auto}
        h1{margin:0;color:var(--bright);font-size:20px;font-weight:650;letter-spacing:.1px}
        h1 .muted{color:var(--dim);font-weight:400}
        .description{margin:4px 0 14px;color:var(--dim);font-size:13px;max-width:900px}
        .meta-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(145px,1fr));gap:8px}
        .meta-box{background:var(--bg3);border:1px solid #2b333d;border-radius:6px;padding:8px 10px;min-width:0}
        .label{display:block;color:var(--dim);font-size:9px;font-weight:700;letter-spacing:.65px;text-transform:uppercase;margin-bottom:2px}
        .value{color:var(--text);font-size:12px;overflow-wrap:anywhere}
        .outcome-pass,.outcome-fail{display:inline-block;padding:2px 9px;border-radius:10px;font-size:10px;font-weight:750;letter-spacing:.5px;text-transform:uppercase}
        .outcome-pass{color:var(--green);background:#173622;border:1px solid #285b38}
        .outcome-fail{color:var(--red);background:#3b1b1c;border:1px solid #703033}
        .container{padding:20px 32px 44px}
        h2{font-size:15px;font-weight:650;color:var(--bright);margin:26px 0 11px;padding-bottom:6px;border-bottom:1px solid var(--border)}
        h2:first-child{margin-top:0}
        .panel{background:var(--bg2);border:1px solid var(--border);border-radius:8px;padding:14px}
        .script-panel{border-color:#385275;background:linear-gradient(135deg,#141f2c,#161b22);padding:18px 22px}
        .script-label{color:var(--blue);font-size:10px;font-weight:750;letter-spacing:1px;text-transform:uppercase;margin-bottom:9px}
        .script{margin:0;color:#f0f3f6;font-family:Georgia,"Times New Roman",serif;font-size:20px;line-height:1.65;white-space:pre-wrap;overflow-wrap:anywhere}
        .empty{color:var(--dim);font-size:12px}
        .stage-strip{display:flex;flex-wrap:wrap;gap:7px}
        .stage-cell{flex:1 1 145px;background:var(--bg3);border:1px solid #2b333d;border-radius:6px;padding:9px 11px;min-width:145px}
        .stage-cell .stage-name{color:var(--bright);font-size:11px;font-weight:700;overflow-wrap:anywhere}
        .stage-cell .stage-stats{color:var(--dim);font:11px "SF Mono",Consolas,monospace;margin-top:3px}
        .timeline{position:relative;padding-left:26px}
        .timeline:before{content:"";position:absolute;left:10px;top:3px;bottom:3px;width:2px;background:var(--border)}
        .iteration{color:var(--blue);font-size:11px;font-weight:750;letter-spacing:.65px;text-transform:uppercase;margin:18px 0 5px 0}
        .iteration:first-child{margin-top:0}
        .event{position:relative;margin:0 0 3px;padding:7px 10px;border:1px solid transparent;border-radius:5px;background:transparent}
        .event:hover{background:var(--bg2);border-color:#252d36}
        .event.error{background:#291719;border-color:#713034;box-shadow:inset 3px 0 var(--red)}
        .event-marker{position:absolute;left:-22px;top:10px;width:15px;height:15px;border:2px solid var(--bg);border-radius:50%;background:var(--dim)}
        .kind-start,.kind-end{background:var(--blue)}
        .kind-llm{background:var(--purple)}
        .kind-decision{background:var(--orange)}
        .kind-artifact{background:var(--cyan)}
        .kind-check{background:var(--green)}
        .kind-measurement{background:var(--blue)}
        .kind-failure{background:var(--red)}
        .event-head{display:flex;align-items:baseline;gap:8px;min-width:0;flex-wrap:wrap}
        .seq,.when,.duration{color:var(--dim);font:10px "SF Mono",Consolas,monospace;white-space:nowrap}
        .stage{color:var(--bright);font-size:11px;font-weight:750;letter-spacing:.35px}
        .kind{font-size:9px;font-weight:750;letter-spacing:.45px;text-transform:uppercase;padding:1px 6px;border-radius:8px;color:#0d1117;background:var(--dim)}
        .kind-label-start,.kind-label-end{background:#8cc7ff}.kind-label-llm{background:#d9b9ff}.kind-label-decision{background:#f1c66b}.kind-label-artifact{background:#86f1f1}.kind-label-check{background:#92e39b}.kind-label-measurement{background:#8cc7ff}.kind-label-failure{background:#ffaaa5;color:#260708}
        .summary{color:var(--text);font-size:12px;overflow-wrap:anywhere}
        .error-text{color:#ffaaa5;font-size:11px;font-weight:650;margin-top:4px;overflow-wrap:anywhere}
        details{border:1px solid var(--border);border-radius:5px;background:#11161d}
        .event-details{margin-top:7px}
        details>summary{cursor:pointer;user-select:none;color:var(--dim);font-size:10px;font-weight:650;letter-spacing:.35px;padding:5px 8px}
        details>summary:hover{color:var(--bright)}
        .detail-body{padding:0 8px 8px}
        .data-block{margin-top:8px}
        .data-label{color:var(--dim);font-size:9px;font-weight:750;letter-spacing:.6px;text-transform:uppercase;margin-bottom:3px;overflow-wrap:anywhere}
        pre{margin:0;background:#010409;border:1px solid #252d36;border-radius:4px;color:var(--text);font:11px/1.55 "SF Mono","Cascadia Code",Consolas,monospace;white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word}
        .data-block pre{padding:8px;max-height:360px;overflow:auto}
        .llm-meta{display:flex;flex-wrap:wrap;gap:6px;margin:8px 0 9px}
        .llm-pill{color:var(--dim);background:var(--bg3);border-radius:10px;padding:2px 8px;font:10px "SF Mono",Consolas,monospace;overflow-wrap:anywhere}
        .prompt-pane{margin-top:7px}
        .prompt-pane pre{padding:10px;max-height:430px;overflow:auto}
        .index-summary{color:var(--dim);font-size:12px;margin:0 0 15px}
        .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:10px}
        .card{background:var(--bg2);border:1px solid var(--border);border-radius:7px;padding:13px 14px;min-width:0}
        .card:hover{border-color:#4b5968}
        .card h2{border:0;margin:0 0 8px;padding:0;font-size:13px}
        a{color:var(--blue);text-decoration:none}a:hover{text-decoration:underline}
        .card-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 12px}
        .card-grid .value{font-size:11px}
        .baseline-comparison{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin-top:12px}
        .comparison-pane{background:#171b21;border:1px solid #46515e;border-radius:8px;padding:16px 18px;min-width:0}
        .comparison-pane-baseline{border-style:dashed}
        .comparison-label{color:var(--bright);font-size:10px;font-weight:750;letter-spacing:1px;text-transform:uppercase;margin-bottom:9px}
        .comparison-meta{display:flex;flex-wrap:wrap;gap:6px 14px;margin-top:12px;color:var(--dim);font:10px "SF Mono",Consolas,monospace}
        @media(max-width:900px){.baseline-comparison{grid-template-columns:1fr}}
        @media(max-width:720px){.header{padding:17px 16px}.container{padding:16px}.meta-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.event-head{display:block}.event-head>*{margin-right:7px}.script{font-size:18px}}
        """;

    public static string Render(WorkflowTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var html = new StringBuilder(24_000);
        BeginDocument(html, "Workflow trace | " + trace.RunId);
        AppendHeader(html, trace);
        html.Append("<main class=\"container\">");
        AppendFinalScript(html, trace);
        AppendStageStrip(html, trace);
        AppendTimeline(html, trace);
        html.Append("</main></body></html>");
        return html.ToString();
    }

    public static string RenderIndex(IReadOnlyList<WorkflowTrace> traces)
    {
        ArgumentNullException.ThrowIfNull(traces);
        var ordered = traces.OrderByDescending(t => t.StartedAt).ToList();
        var html = new StringBuilder(18_000);
        BeginDocument(html, "Workflow trace index");
        html.Append("<header class=\"header\"><div class=\"header-inner\"><h1>Workflow trace index</h1><p class=\"description\">Recorded runs, newest first</p></div></header><main class=\"container\">");
        var successful = ordered.Count(IsPassing);
        html.Append("<p class=\"index-summary\"><strong>").Append(Esc(ordered.Count.ToString(CultureInfo.InvariantCulture))).Append("</strong> run").Append(ordered.Count == 1 ? "" : "s").Append(" &middot; <strong>").Append(Esc(FormatNumber(Median(ordered.Select(t => (double)t.LlmCallCount).ToList())))).Append("</strong> median LLM calls &middot; <strong>").Append(Esc(FormatMs(Median(ordered.Select(t => t.TotalMs).ToList())))).Append("</strong> median wall time &middot; <strong>").Append(Esc(successful.ToString(CultureInfo.InvariantCulture))).Append("</strong> succeeded</p>");
        if (ordered.Count == 0)
        {
            html.Append("<div class=\"panel empty\">No workflow traces.</div>");
        }
        else
        {
            html.Append("<div class=\"cards\">");
            foreach (var trace in ordered)
            {
                var passing = IsPassing(trace);
                html.Append("<article class=\"card\"><h2><a href=\"").Append(Esc(trace.RunId + ".html")).Append("\">").Append(Esc(trace.RunId)).Append("</a> <span class=\"").Append(passing ? "outcome-pass" : "outcome-fail").Append("\">").Append(Esc(OutcomeText(trace))).Append("</span></h2><div class=\"card-grid\">");
                CardValue(html, "Workflow", trace.WorkflowId);
                CardValue(html, "Market", trace.Market);
                CardValue(html, "Station", trace.Station);
                CardValue(html, "Target", FormatNumber(trace.TargetSeconds) + "s");
                CardValue(html, "Wall time", FormatMs(trace.TotalMs));
                CardValue(html, "LLM calls", trace.LlmCallCount.ToString(CultureInfo.InvariantCulture));
                var baseline = FindBaseline(trace);
                if (baseline is not null) CardValue(html, "Baseline", WordCountText(baseline.Script, baseline.Words));
                CardValue(html, "Iterations", trace.Iterations.ToString(CultureInfo.InvariantCulture));
                CardValue(html, "Started", FormatTimestamp(trace.StartedAt));
                html.Append("</div></article>");
            }
            html.Append("</div>");
        }
        html.Append("</main></body></html>");
        return html.ToString();
    }

    public static string Write(WorkflowTrace trace, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.GetFullPath(Path.Combine(outputDirectory, trace.RunId + ".html"));
        File.WriteAllText(path, Render(trace), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string WriteIndex(IReadOnlyList<WorkflowTrace> traces, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(traces);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.GetFullPath(Path.Combine(outputDirectory, "index.html"));
        File.WriteAllText(path, RenderIndex(traces), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void BeginDocument(StringBuilder html, string title)
    {
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"><title>").Append(Esc(title)).Append("</title><style>").Append(Styles).Append("</style></head><body>");
    }

    private static void AppendHeader(StringBuilder html, WorkflowTrace trace)
    {
        var passing = IsPassing(trace);
        html.Append("<header class=\"header\"><div class=\"header-inner\"><h1>Workflow <span class=\"muted\">").Append(Esc(trace.WorkflowId)).Append("</span></h1><p class=\"description\">").Append(Esc(trace.WorkflowDescription)).Append("</p><div class=\"meta-grid\">");
        MetaValue(html, "Job", trace.JobId);
        MetaValue(html, "Market", trace.Market);
        MetaValue(html, "Station", trace.Station);
        MetaValue(html, "Target seconds", FormatNumber(trace.TargetSeconds));
        MetaValue(html, "Wall time", FormatMs(trace.TotalMs));
        MetaValue(html, "LLM calls", trace.LlmCallCount.ToString(CultureInfo.InvariantCulture));
        MetaValue(html, "Iterations", trace.Iterations.ToString(CultureInfo.InvariantCulture));
        html.Append("<div class=\"meta-box\"><span class=\"label\">Outcome</span><span class=\"").Append(passing ? "outcome-pass" : "outcome-fail").Append("\">").Append(Esc(OutcomeText(trace))).Append("</span></div>");
        html.Append("</div></div></header>");
    }

    private static void AppendFinalScript(StringBuilder html, WorkflowTrace trace)
    {
        var final = FindFinalScriptArtifact(trace);
        if (final is not null)
        {
            html.Append("<h2>Final script</h2><section class=\"panel script-panel\"><div class=\"script-label\">Finished read</div><p class=\"script\">").Append(Esc(final.Script)).Append("</p></section>");
        }

        var baseline = FindBaseline(trace);
        if (baseline is null)
        {
            return;
        }

        var newScript = final?.Script ?? "No final script recorded.";
        html.Append("<h2>Baseline comparison</h2><section class=\"baseline-comparison\"><article class=\"comparison-pane comparison-pane-new\"><div class=\"comparison-label\">New workflow</div><p class=\"script\">").Append(Esc(newScript)).Append("</p><div class=\"comparison-meta\"><span>words: ").Append(Esc(WordCountText(newScript, final?.Words))).Append("</span><span>target: ").Append(Esc(FormatNumber(trace.TargetSeconds))).Append("s</span></div></article><article class=\"comparison-pane comparison-pane-baseline\"><div class=\"comparison-label\">Deployed pipeline (same input data)</div><p class=\"script\">").Append(Esc(baseline.Script)).Append("</p><div class=\"comparison-meta\"><span>words: ").Append(Esc(WordCountText(baseline.Script, baseline.Words))).Append("</span>");
        if (!string.IsNullOrWhiteSpace(baseline.MeasuredSeconds))
        {
            html.Append("<span>measured: ").Append(Esc(baseline.MeasuredSeconds)).Append("s</span>");
        }
        html.Append("</div></article></section>");
    }

    private static void AppendStageStrip(StringBuilder html, WorkflowTrace trace)
    {
        var events = trace.Events.OrderBy(e => e.Sequence).ToList();
        var stageNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in events)
        {
            if (seen.Add(item.Stage)) stageNames.Add(item.Stage);
        }
        html.Append("<h2>Stage timing</h2><section class=\"panel\"><div class=\"stage-strip\">");
        if (stageNames.Count == 0)
        {
            html.Append("<span class=\"empty\">No stages recorded.</span>");
        }
        else
        {
            var denominator = trace.TotalMs > 0 ? trace.TotalMs : stageNames.Sum(stage => StageMilliseconds(events, stage));
            foreach (var stage in stageNames)
            {
                var milliseconds = StageMilliseconds(events, stage);
                var share = denominator > 0 ? milliseconds / denominator * 100 : 0;
                html.Append("<div class=\"stage-cell\"><div class=\"stage-name\">").Append(Esc(stage)).Append("</div><div class=\"stage-stats\">").Append(Esc(FormatMs(milliseconds))).Append(" &middot; ").Append(Esc(FormatNumber(share))).Append("%</div></div>");
            }
        }
        html.Append("</div></section>");
    }

    private static void AppendTimeline(StringBuilder html, WorkflowTrace trace)
    {
        var events = trace.Events.OrderBy(e => e.Sequence).ToList();
        html.Append("<h2>Timeline <span class=\"label\" style=\"display:inline;font-size:10px;letter-spacing:0\">").Append(Esc(events.Count.ToString(CultureInfo.InvariantCulture))).Append(" events</span></h2><section class=\"timeline\">");
        if (events.Count == 0)
        {
            html.Append("<p class=\"empty\">No events recorded.</p>");
        }
        else
        {
            var previousIteration = int.MinValue;
            foreach (var item in events)
            {
                if (trace.Iterations > 1 && item.Iteration != previousIteration)
                {
                    previousIteration = item.Iteration;
                    html.Append("<h3 class=\"iteration\">Iteration ").Append(Esc(item.Iteration.ToString(CultureInfo.InvariantCulture))).Append("</h3>");
                }
                AppendEvent(html, trace, item);
            }
        }
        html.Append("</section>");
    }

    private static void AppendEvent(StringBuilder html, WorkflowTrace trace, TraceEvent item)
    {
        var kind = KindName(item.Kind);
        var kindClass = KindClass(item.Kind);
        html.Append("<article class=\"event").Append(item.Error is null ? "" : " error").Append("\"><span class=\"event-marker ").Append(kindClass).Append("\"></span><div class=\"event-head\"><span class=\"seq\">#").Append(Esc(item.Sequence.ToString(CultureInfo.InvariantCulture))).Append("</span><span class=\"when\">").Append(Esc(FormatMs((item.At - trace.StartedAt).TotalMilliseconds))).Append(" after start</span>");
        if (item.DurationMs is not null) html.Append("<span class=\"duration\">").Append(Esc(FormatMs(item.DurationMs.Value))).Append(" duration</span>");
        html.Append("<span class=\"stage\">").Append(Esc(item.Stage)).Append("</span><span class=\"kind kind-label-").Append(kindClass.Replace("kind-", string.Empty, StringComparison.Ordinal)).Append("\">").Append(Esc(kind)).Append("</span></div><div class=\"summary\">").Append(Esc(item.Summary)).Append("</div>");
        if (item.Error is not null) html.Append("<div class=\"error-text\">ERROR: ").Append(Esc(item.Error)).Append("</div>");
        if ((item.Data is not null && item.Data.Count > 0) || item.LlmCall is not null)
        {
            html.Append("<details class=\"event-details\"><summary>Inspect details</summary><div class=\"detail-body\">");
            if (item.Data is not null)
            {
                foreach (var pair in item.Data.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    html.Append("<div class=\"data-block\"><div class=\"data-label\">").Append(Esc(pair.Key)).Append("</div><pre>").Append(Esc(pair.Value)).Append("</pre></div>");
                }
            }
            if (item.LlmCall is not null) AppendLlmCall(html, item.LlmCall);
            html.Append("</div></details>");
        }
        html.Append("</article>");
    }

    private static void AppendLlmCall(StringBuilder html, LlmCallRecord call)
    {
        html.Append("<div class=\"llm-meta\"><span class=\"llm-pill\">role: ").Append(Esc(call.Role)).Append("</span><span class=\"llm-pill\">model: ").Append(Esc(call.Model)).Append("</span><span class=\"llm-pill\">duration: ").Append(Esc(FormatMs(call.DurationMs))).Append("</span>");
        if (call.PromptTokens is not null) html.Append("<span class=\"llm-pill\">prompt tokens: ").Append(Esc(call.PromptTokens.Value.ToString(CultureInfo.InvariantCulture))).Append("</span>");
        if (call.CompletionTokens is not null) html.Append("<span class=\"llm-pill\">completion tokens: ").Append(Esc(call.CompletionTokens.Value.ToString(CultureInfo.InvariantCulture))).Append("</span>");
        if (call.Error is not null) html.Append("<span class=\"llm-pill outcome-fail\">error: ").Append(Esc(call.Error)).Append("</span>");
        html.Append("</div>");
        PromptPane(html, "System prompt", call.SystemPrompt);
        PromptPane(html, "Rendered user prompt", call.UserPrompt);
        PromptPane(html, "Raw response", call.RawResponse);
    }

    private static void PromptPane(StringBuilder html, string title, string text)
    {
        html.Append("<details class=\"prompt-pane\"><summary>").Append(Esc(title)).Append("</summary><pre>").Append(Esc(text)).Append("</pre></details>");
    }

    private static void MetaValue(StringBuilder html, string label, string value)
    {
        html.Append("<div class=\"meta-box\"><span class=\"label\">").Append(Esc(label)).Append("</span><span class=\"value\">").Append(Esc(value)).Append("</span></div>");
    }

    private static void CardValue(StringBuilder html, string label, string value)
    {
        html.Append("<div><span class=\"label\">").Append(Esc(label)).Append("</span><span class=\"value\">").Append(Esc(value)).Append("</span></div>");
    }

    private static double StageMilliseconds(IReadOnlyList<TraceEvent> events, string stage)
    {
        var ends = events.Where(e => string.Equals(e.Stage, stage, StringComparison.Ordinal) && e.Kind == TraceEventKind.StageEnd && e.DurationMs is not null).Sum(e => e.DurationMs!.Value);
        if (ends > 0) return ends;
        return events.Where(e => string.Equals(e.Stage, stage, StringComparison.Ordinal) && e.DurationMs is not null).Sum(e => e.DurationMs!.Value);
    }

    private sealed record ScriptArtifact(string Script, string? Words);

    private sealed record BaselineArtifact(string Script, string? Words, string? MeasuredSeconds);

    private static ScriptArtifact? FindFinalScriptArtifact(WorkflowTrace trace)
    {
        var exact = new[] { "finalScript", "finishedRead", "finishedScript", "script", "read" };
        ScriptArtifact? fallback = null;
        foreach (var item in trace.Events.Where(e => e.Kind == TraceEventKind.Artifact).OrderBy(e => e.Sequence))
        {
            if (item.Data is null) continue;
            foreach (var key in exact)
            {
                foreach (var pair in item.Data)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        return new ScriptArtifact(pair.Value, FindData(item.Data, "words"));
                    }
                }
            }
            foreach (var pair in item.Data.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (fallback is null && (pair.Key.Contains("script", StringComparison.OrdinalIgnoreCase) || pair.Key.Contains("read", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    fallback = new ScriptArtifact(pair.Value, FindData(item.Data, "words"));
                }
            }
        }
        return fallback;
    }

    private static BaselineArtifact? FindBaseline(WorkflowTrace trace)
    {
        foreach (var item in trace.Events.Where(e => e.Kind == TraceEventKind.Artifact).OrderBy(e => e.Sequence))
        {
            if (item.Data is null || !TryFindData(item.Data, "baselineScript", out var script)) continue;
            return new BaselineArtifact(script, FindData(item.Data, "baselineWords"), FindData(item.Data, "baselineMeasuredSeconds"));
        }
        return null;
    }

    private static string? FindData(IReadOnlyDictionary<string, string> data, string key) =>
        TryFindData(data, key, out var value) ? value : null;

    private static bool TryFindData(IReadOnlyDictionary<string, string> data, string key, out string value)
    {
        foreach (var pair in data.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static string WordCountText(string script, string? recordedWords) =>
        !string.IsNullOrWhiteSpace(recordedWords)
            ? recordedWords
            : CountWords(script).ToString(CultureInfo.InvariantCulture);

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }
        return count;
    }

    private static bool IsPassing(WorkflowTrace trace)
    {
        if (trace.Error is not null) return false;
        var outcome = trace.Outcome ?? string.Empty;
        return !outcome.Contains("fail", StringComparison.OrdinalIgnoreCase) && !outcome.Contains("error", StringComparison.OrdinalIgnoreCase) && !outcome.Contains("reject", StringComparison.OrdinalIgnoreCase);
    }

    private static string OutcomeText(WorkflowTrace trace) => trace.Error ?? trace.Outcome ?? (trace.CompletedAt is null ? "incomplete" : "success");

    private static string KindName(TraceEventKind kind) => kind switch
    {
        TraceEventKind.StageStart => "start",
        TraceEventKind.StageEnd => "end",
        TraceEventKind.LlmCall => "llm call",
        TraceEventKind.Decision => "decision",
        TraceEventKind.Artifact => "artifact",
        TraceEventKind.Check => "check",
        TraceEventKind.Measurement => "measurement",
        TraceEventKind.Failure => "failure",
        _ => "event"
    };

    private static string KindClass(TraceEventKind kind) => kind switch
    {
        TraceEventKind.StageStart => "kind-start",
        TraceEventKind.StageEnd => "kind-end",
        TraceEventKind.LlmCall => "kind-llm",
        TraceEventKind.Decision => "kind-decision",
        TraceEventKind.Artifact => "kind-artifact",
        TraceEventKind.Check => "kind-check",
        TraceEventKind.Measurement => "kind-measurement",
        TraceEventKind.Failure => "kind-failure",
        _ => "kind-info"
    };

    private static string FormatTimestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string FormatMs(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + " ms";

    private static string FormatNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var ordered = values.OrderBy(value => value).ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }

    private static string Esc(string? value) => (value ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);
}
