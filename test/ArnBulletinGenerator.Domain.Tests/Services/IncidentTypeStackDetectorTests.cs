using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class IncidentTypeStackDetectorTests
{
    [Fact]
    public void Flags_two_type_nouns_stacked_in_a_trailing_with_clause()
    {
        // Observed live: "Reid Highway is shut at Altone Road and Lord Street for upgrades,
        // with closures, speed reductions and detours in place."
        const string script =
            "Reid Highway is shut at Altone Road and Lord Street for upgrades, with closures, speed reductions and detours in place.";

        var result = IncidentTypeStackDetector.Detect(script);

        result.HasTypeStack.ShouldBeTrue();
        result.Feedback.ShouldContain("TYPE-STACK");
    }

    [Fact]
    public void Flags_two_type_nouns_stacked_in_a_trailing_including_clause()
    {
        const string script =
            "Warringah Freeway citybound is closed at Brook Street, with road closure and construction there.";

        IncidentTypeStackDetector.Detect(script).HasTypeStack.ShouldBeTrue();
    }

    [Fact]
    public void Passes_a_single_type_noun_in_the_trailing_clause()
    {
        const string script = "Reid Highway is shut at Altone Road and Lord Street, with construction underway.";

        IncidentTypeStackDetector.Detect(script).HasTypeStack.ShouldBeFalse();
    }

    [Fact]
    public void Passes_a_clean_sentence_with_no_trailing_with_or_including_clause()
    {
        const string script = "The M4 is closed at Church Street, so steer around it.";

        IncidentTypeStackDetector.Detect(script).HasTypeStack.ShouldBeFalse();
    }

    [Fact]
    public void Passes_a_trailing_clause_that_is_not_about_incident_types()
    {
        const string script = "Traffic is heavy on the M4, with drivers urged to allow extra time.";

        IncidentTypeStackDetector.Detect(script).HasTypeStack.ShouldBeFalse();
    }

    [Fact]
    public void Passes_empty_script()
    {
        IncidentTypeStackDetector.Detect(string.Empty).HasTypeStack.ShouldBeFalse();
    }

    [Fact]
    public void Repair_StripsTheOffendingClause_KeepsTheRestOfTheScript()
    {
        const string script =
            "Reid Highway is shut at Altone Road and Lord Street for upgrades, with closures, speed reductions and detours in place. Mitchell Freeway outbound is slow near Powis Street.";

        string repaired = IncidentTypeStackDetector.Repair(script);

        IncidentTypeStackDetector.Detect(repaired).HasTypeStack.ShouldBeFalse();
        repaired.ShouldContain("Reid Highway is shut at Altone Road and Lord Street for upgrades.");
        repaired.ShouldContain("Mitchell Freeway outbound is slow near Powis Street.");
        repaired.ShouldNotContain("speed reductions");
        repaired.ShouldNotContain("detours");
    }

    [Fact]
    public void Repair_LeavesACleanScriptUnchanged()
    {
        const string script = "The M4 is closed at Church Street, so steer around it.";

        IncidentTypeStackDetector.Repair(script).ShouldBe(script);
    }

    [Fact]
    public void Repair_HandlesTheOffendingSentenceAsTheOnlySentence()
    {
        const string script =
            "Reid Highway is shut at Altone Road and Lord Street for upgrades, with closures, speed reductions and detours in place.";

        IncidentTypeStackDetector.Repair(script)
            .ShouldBe("Reid Highway is shut at Altone Road and Lord Street for upgrades.");
    }

    [Fact]
    public void Repair_is_idempotent_on_a_stacked_input()
    {
        const string stacked =
            "Reid Highway is shut at Altone Road and Lord Street for upgrades, with closures, speed reductions and detours in place.";

        var once = IncidentTypeStackDetector.Repair(stacked);
        var twice = IncidentTypeStackDetector.Repair(once);

        // Non-vacuous: the first pass genuinely stripped the clause, and a second pass is a no-op.
        once.ShouldNotBe(stacked);
        twice.ShouldBe(once);
    }

    [Fact]
    public void Detect_computes_exact_sentence_order_and_sentence_scope()
    {
        const string script =
            "M4 Western Motorway is moving well. " +
            "Reid Highway is shut at Altone Road for upgrades, with closures, speed reductions and detours in place. " +
            "Anzac Bridge is all clear.";

        var result = IncidentTypeStackDetector.Detect(script);

        result.HasTypeStack.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        var issue = result.Issue!;
        issue.Order.ShouldBe(2);
        issue.Defect.ShouldBe("INCIDENT_TYPE_STACK");
        issue.Scope.ShouldBe(ArnBulletinGenerator.Domain.Models.RefineScope.Sentence);
        issue.IsPatchable.ShouldBeTrue();
    }
}
