using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class MetaLeakDetectorTests
{
    [Fact]
    public void Flags_the_word_data_anywhere_in_the_script()
    {
        const string script = "City Road is running well, with no delays across the other clear roads in the data.";

        var result = MetaLeakDetector.Detect(script);

        result.HasMetaLeak.ShouldBeTrue();
        result.Feedback.ShouldContain("META-LEAK");
    }

    [Fact]
    public void Flags_data_as_a_standalone_word_case_insensitively()
    {
        MetaLeakDetector.Detect("According to the DATA, the M4 is clear.").HasMetaLeak.ShouldBeTrue();
    }

    [Fact]
    public void Does_not_flag_a_word_containing_data_as_a_substring()
    {
        // "database"/"updated" etc. must not false-positive on the whole-word check.
        MetaLeakDetector.Detect("The Warringah Freeway is shut, updated moments ago.").HasMetaLeak.ShouldBeFalse();
    }

    [Fact]
    public void Passes_a_clean_script()
    {
        MetaLeakDetector.Detect("The M4 is running well, with the rest of the run clear.").HasMetaLeak.ShouldBeFalse();
    }

    [Fact]
    public void Flags_the_exact_live_shipped_defect()
    {
        // Observed live (Newcastle): database-structure language describing the SOURCE, not the
        // road — the same "presenter never references their source" violation as the word "data",
        // just without that literal word.
        const string script =
            "No other problems are logged on the listed roads, so overall flow across the area is steady.";

        var result = MetaLeakDetector.Detect(script);

        result.HasMetaLeak.ShouldBeTrue();
        result.Feedback.ShouldContain("META-LEAK");
    }

    [Theory]
    [InlineData("Three incidents are logged this morning across the network.")]
    [InlineData("The M4 is clear, on record as the quietest run today.")]
    [InlineData("This incident is now in the system for review.")]
    public void Flags_other_database_structure_phrases(string script) =>
        MetaLeakDetector.Detect(script).HasMetaLeak.ShouldBeTrue();

    [Theory]
    // "logged on" (a login idiom) and "listed for sale" are ordinary English, not a source-leak —
    // only the SPECIFIC phrase shapes ("X are logged", "the listed roads") should trip the gate.
    [InlineData("The driver logged on to check the app before setting off.")]
    [InlineData("Roadside stalls listed goods for sale near the market.")]
    [InlineData("Warringah Freeway has a great safety record this year.")]
    public void Does_not_flag_ordinary_english_using_the_same_words(string script) =>
        MetaLeakDetector.Detect(script).HasMetaLeak.ShouldBeFalse();

    [Fact]
    public void Passes_empty_script()
    {
        MetaLeakDetector.Detect(string.Empty).HasMetaLeak.ShouldBeFalse();
    }

    // --- New branch: internal system jargon ("corridor" / "monitored route") -----------------
    // These are our own pipeline labels for the data structures, never words a presenter voices.

    [Fact]
    public void Flags_the_exact_live_newcastle_internal_jargon_defect()
    {
        // Observed live (Newcastle): "monitored routes"/"corridors" describe our data model, not
        // the road — the same "presenter never references their source" violation, in jargon form.
        const string script =
            "The rest of the run through Newcastle's monitored routes is clear, including the main " +
            "approaches into the CBD and the loop roads around the harbour, so drivers can expect an " +
            "easy commute on those corridors.";

        var result = MetaLeakDetector.Detect(script);

        result.HasMetaLeak.ShouldBeTrue();
        result.Feedback.ShouldContain("META-LEAK");
    }

    [Theory]
    [InlineData("The M4 corridor is flowing well this morning.")]              // "corridor" singular
    [InlineData("Both corridors into the city are clear right now.")]          // "corridors" plural
    [InlineData("The monitored route through Parramatta is quiet.")]           // "monitored route" singular
    [InlineData("All monitored routes are moving freely this hour.")]          // "monitored routes" plural
    [InlineData("The Monitored Route into town is clear.")]                    // mixed case
    [InlineData("Traffic on the CORRIDOR is heavy near the bridge.")]          // upper case
    public void Flags_internal_system_jargon_in_any_case_and_number(string script)
    {
        var result = MetaLeakDetector.Detect(script);

        result.HasMetaLeak.ShouldBeTrue();
        result.Feedback.ShouldContain("META-LEAK");
    }

    [Fact]
    public void Passes_a_clean_script_using_allowed_on_air_phrasing()
    {
        // The style's own approved phrasings — none contain "corridor"/"monitored route".
        const string script =
            "The Pacific Highway's shut at Hexham. Cessnock Road and Nelson Bay Road are both clear, " +
            "and the rest of the run's flowing freely.";

        MetaLeakDetector.Detect(script).HasMetaLeak.ShouldBeFalse();
    }

    [Theory]
    // Natural on-air phrases the style explicitly ALLOWS. Teeth: an over-broad regex catching bare
    // "run"/"route" would redden these ("a good run", "running well", "the run's clear").
    [InlineData("The M4's having a good run this morning.")]
    [InlineData("The Hume is running well, no delays.")]
    [InlineData("The rest of the run's clear across the network.")]
    [InlineData("Traffic is flowing freely on every approach.")]
    public void Does_not_flag_allowed_natural_on_air_phrases(string script) =>
        MetaLeakDetector.Detect(script).HasMetaLeak.ShouldBeFalse();

    [Fact]
    public void The_data_class_still_fires_and_wins_over_the_new_internal_term_branch()
    {
        // "data" AND "corridors" both present: the FIRST branch must win, proving the appended
        // internal-term branch neither shadows nor reorders the pre-existing data check.
        var result = MetaLeakDetector.Detect("According to the data, both corridors are clear.");

        result.HasMetaLeak.ShouldBeTrue();
        result.Feedback.ShouldContain("the word \"data\""); // unique to the data-branch feedback
    }

    [Fact]
    public void The_database_phrase_class_still_fires_after_appending_the_new_branch()
    {
        var result = MetaLeakDetector.Detect("Everything on the listed roads is quiet this morning.");

        result.HasMetaLeak.ShouldBeTrue();
        result.Feedback.ShouldContain("listed roads"); // unique to the database-phrase feedback
    }

    // --- Repair: mechanical last-resort fix for the internal-term class ONLY -------------------
    // Repair swaps our jargon for the natural on-air word ("monitored route(s)" -> "route(s)",
    // "corridor(s)" -> "road(s)"), leaving the un-substitutable "data"/database-phrase classes
    // for the LLM loop. Each test below reddens on a wrong substitution or broken case-preservation.

    [Fact]
    public void Repair_fixes_the_live_newcastle_internal_term_defect_end_to_end()
    {
        // The exact live Newcastle shape the word-target expansion pass reintroduces: both jargon
        // classes in one sentence. After Repair the whole script must clear Detect.
        const string script =
            "The rest of the Newcastle run's clear across the monitored routes, with priority " +
            "corridors maintaining a good run.";

        var result = MetaLeakDetector.Repair(script);

        MetaLeakDetector.Detect(result).HasMetaLeak.ShouldBeFalse();
        result.ShouldContain("routes");
        result.ShouldContain("roads");
        result.ShouldNotContain("monitored");
        result.ShouldNotContain("corridor");
    }

    [Fact]
    public void Repair_drops_monitored_and_keeps_the_singular_route()
    {
        // Exact result has teeth on BOTH the dropped word and the preserved number: producing
        // "routes", keeping "monitored", or losing "route" all redden this.
        var result = MetaLeakDetector.Repair("The monitored route through Parramatta is quiet.");

        result.ShouldBe("The route through Parramatta is quiet.");
        result.ShouldNotContain("monitored");
        result.ShouldContain("route");
    }

    [Theory]
    [InlineData("Both corridors into the city are clear.", "Both roads into the city are clear.")]
    [InlineData("The M4 corridor is flowing well.", "The M4 road is flowing well.")]
    public void Repair_swaps_mid_sentence_corridor_terms_for_roads(string input, string expected) =>
        // Mid-sentence stays lowercase; singular->road and plural->roads are enforced exactly, so a
        // number-mismatch (corridor->roads / corridors->road) or a stray capital reddens.
        MetaLeakDetector.Repair(input).ShouldBe(expected);

    [Fact]
    public void Repair_preserves_the_leading_capital_when_corridors_starts_a_sentence()
    {
        // Sentence-start "Corridors" must become "Roads" (capital R). A lowercase "roads" or an
        // untouched "Corridors" reddens.
        MetaLeakDetector.Repair("Corridors are clear.").ShouldBe("Roads are clear.");
    }

    [Fact]
    public void Repair_preserves_the_case_of_the_route_word_after_dropping_monitored()
    {
        // The route-word case rides through the capture group ($1), independent of the corridor
        // path: "Monitored Routes" -> "Routes" keeps the capital R.
        MetaLeakDetector.Repair("Monitored Routes are clear.").ShouldBe("Routes are clear.");
    }

    [Fact]
    public void Repair_collapses_any_whitespace_between_monitored_and_route()
    {
        // The two words may be separated by any run of whitespace (\s+), including a newline; the
        // whole "monitored<ws>" span is removed, leaving the route word attached to its context.
        MetaLeakDetector.Repair("Delays across the monitored\n  routes are easing.")
            .ShouldBe("Delays across the routes are easing.");
    }

    [Fact]
    public void Repair_is_idempotent_on_a_leaking_input()
    {
        const string leaking = "Both corridors are clear and the monitored routes are moving.";

        var once = MetaLeakDetector.Repair(leaking);
        var twice = MetaLeakDetector.Repair(once);

        // Non-vacuous: the first pass genuinely changed the input, and a second pass is a no-op.
        once.ShouldNotBe(leaking);
        twice.ShouldBe(once);
    }

    [Fact]
    public void Repair_leaves_the_data_class_untouched()
    {
        // Repair only handles the internal-term class; "data" has no safe substitution, so the
        // string comes back verbatim with the leak intact (Detect still owns the warning path).
        const string script = "the rest of the clear roads in the data";

        var result = MetaLeakDetector.Repair(script);

        result.ShouldBe(script);
        result.ShouldContain("data");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Repair_returns_blank_or_whitespace_input_unchanged(string script) =>
        MetaLeakDetector.Repair(script).ShouldBe(script);
    [Fact]
    public void Detect_computes_exact_sentence_order_and_sentence_scope()
    {
        const string script = "The M4 is moving nicely. The rest of the clear roads in the data are flowing well. Anzac Bridge is clear.";

        var result = MetaLeakDetector.Detect(script);

        result.HasMetaLeak.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        var issue = result.Issue!;
        issue.Order.ShouldBe(2);
        issue.Defect.ShouldBe("META_LEAK");
        issue.Scope.ShouldBe(ArnBulletinGenerator.Domain.Models.RefineScope.Sentence);
        issue.IsPatchable.ShouldBeTrue();
    }
}
