using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class EvidenceRestatementDetectorTests
{
    private static DraftedSentence Sentence(int order, string text, params string[] evidenceIds) =>
        new(order, text, evidenceIds);

    [Fact]
    public void Flags_equal_evidence_sets_and_names_the_offending_sentences()
    {
        var sentences = new[]
        {
            Sentence(1, "Church Street is slow near Harris Park.", "e1", "e2"),
            Sentence(2, "Traffic is moving slowly on Church Street by Harris Park.", "e2", "e1", "e1")
        };

        var result = EvidenceRestatementDetector.Detect(sentences);

        result.HasRestatement.ShouldBeTrue();
        result.Feedback.ShouldContain("RESTATEMENT");
        result.Feedback.ShouldContain("Church Street is slow near Harris Park.");
        result.Feedback.ShouldContain("Traffic is moving slowly on Church Street by Harris Park.");
    }

    [Fact]
    public void Repair_keeps_the_first_sentence_and_drops_the_later_duplicate()
    {
        var sentences = new[]
        {
            Sentence(1, "Church Street is slow near Harris Park.", "e1"),
            Sentence(2, "Traffic is moving slowly on Church Street by Harris Park.", "e1")
        };
        const string script = "Church Street is slow near Harris Park. Traffic is moving slowly on Church Street by Harris Park.";

        string repaired = EvidenceRestatementDetector.Repair(script, sentences);
        var remaining = EvidenceRestatementDetector.RepairSentences(sentences)!;

        repaired.ShouldBe("Church Street is slow near Harris Park.");
        remaining.Count.ShouldBe(1);
        remaining[0].Order.ShouldBe(1);
        EvidenceRestatementDetector.Detect(remaining).HasRestatement.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_different_ids_for_two_locations_on_one_road()
    {
        var sentences = new[]
        {
            Sentence(1, "Church Street is slow near Harris Park.", "church-harris"),
            Sentence(2, "Church Street is slow near Parramatta.", "church-parramatta")
        };

        EvidenceRestatementDetector.Detect(sentences).HasRestatement.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_grouped_evidence_followed_by_a_different_fact()
    {
        var sentences = new[]
        {
            Sentence(1, "The M1 and M4 are both busy.", "m1", "m4"),
            Sentence(2, "The M7 has a crash near the interchange.", "m7")
        };

        EvidenceRestatementDetector.Detect(sentences).HasRestatement.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_partial_overlap_or_a_strict_subset()
    {
        var sentences = new[]
        {
            Sentence(1, "The M1 and M4 are both busy.", "m1", "m4"),
            Sentence(2, "The M4 has a lane blocked.", "m4"),
            Sentence(3, "The M4 and M7 are slowing.", "m4", "m7")
        };

        EvidenceRestatementDetector.Detect(sentences).HasRestatement.ShouldBeFalse();
    }

    [Fact]
    public void Never_compares_sentences_without_citations()
    {
        var sentences = new[]
        {
            Sentence(1, "Good morning from the traffic desk."),
            Sentence(2, "Stay with us for the latest update."),
            Sentence(3, "Church Street is slow.", "e1")
        };

        EvidenceRestatementDetector.Detect(sentences).HasRestatement.ShouldBeFalse();
    }

    [Fact]
    public void Null_single_and_empty_inputs_are_clean_no_ops()
    {
        EvidenceRestatementDetector.Detect(null).HasRestatement.ShouldBeFalse();
        EvidenceRestatementDetector.Detect(Array.Empty<DraftedSentence>()).HasRestatement.ShouldBeFalse();
        EvidenceRestatementDetector.Detect(new[] { Sentence(1, "Church Street is slow.", "e1") })
            .HasRestatement.ShouldBeFalse();

        EvidenceRestatementDetector.Repair("Church Street is slow.", null).ShouldBe("Church Street is slow.");
        EvidenceRestatementDetector.Repair("Church Street is slow.", new[] { Sentence(1, "Church Street is slow.", "e1") })
            .ShouldBe("Church Street is slow.");
    }
}
