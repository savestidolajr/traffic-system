using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class FutureLessonPolicyTests
{
    [Fact]
    public void Validate_ExplicitReusableInstruction_Succeeds()
    {
        var result = FutureLessonPolicy.Validate(
            FutureLessonCategory.PriorityOrdering,
            "Lead with a full motorway closure before ordinary congestion.");

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Too short")]
    public void Validate_MissingOrShortInstruction_IsRejected(string? text)
    {
        var result = FutureLessonPolicy.Validate(
            FutureLessonCategory.WordingStyle,
            text);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InstructionOverLimit_IsRejected()
    {
        var result = FutureLessonPolicy.Validate(
            FutureLessonCategory.ContentSelection,
            new string('x', FutureLessonPolicy.MaxTextLength + 1));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validate_UnknownCategory_IsRejected()
    {
        var result = FutureLessonPolicy.Validate(
            (FutureLessonCategory)999,
            "Use this valid-length instruction for later bulletins.");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CreateId_NormalizesStationWhitespaceCasingAndInstructionSpacing()
    {
        string first = FutureLessonPolicy.CreateId(
            "KIIS 101.1",
            FutureLessonCategory.WordingStyle,
            "Use direct conversational traffic language.");
        string second = FutureLessonPolicy.CreateId(
            "  kiis 101.1  ",
            FutureLessonCategory.WordingStyle,
            "  use DIRECT conversational   traffic language.  ");

        first.ShouldBe(second);
    }

}
