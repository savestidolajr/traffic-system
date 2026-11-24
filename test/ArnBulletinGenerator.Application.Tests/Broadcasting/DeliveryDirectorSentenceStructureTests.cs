using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

/// <summary>
///     The director may re-punctuate a read for weight but may not change how many sentences it
///     has. Dev goldcoast job <c>de2d1b60</c> (2026-09-03) is the case: the composer joined a
///     closure and its condition with a semicolon, every gate passed that as ONE sentence, and the
///     director returned an ellipsis in its place. The persisted read then carried an orphan
///     sentence naming no road — created after the last gate, with no LLM call in the job record
///     to explain it, and independently droppable by the duration fitter.
/// </summary>
public sealed class DeliveryDirectorSentenceStructureTests
{
    private const string Approved =
        "Country Club Drive at Helensvale is closed northbound for roadworks over about half a kay; "
        + "Northbound traffic cannot use the closed section. "
        + "Ferry Road at Southport has a two-minute delay.";

    private readonly IScriptDirector _director = Substitute.For<IScriptDirector>();

    [Fact]
    public async Task An_inserted_sentence_boundary_reverts_to_the_approved_read()
    {
        Directs(Approved.Replace("half a kay;", "half a kay…"));

        string result = await Apply();

        result.ShouldBe(Approved);
    }

    [Fact]
    public async Task A_full_stop_in_place_of_the_semicolon_reverts_too()
    {
        Directs(Approved.Replace("half a kay;", "half a kay."));

        string result = await Apply();

        result.ShouldBe(Approved);
    }

    /// <summary>
    ///     The exemption the guard exists to preserve: an ellipsis used as a mid-sentence pause
    ///     (lowercase word after it) is not a boundary, so it must still survive. Rejecting these
    ///     is the regression behind job 32a5e344 — a whole tagged read thrown away over one mark.
    /// </summary>
    [Fact]
    public async Task A_mid_sentence_pause_and_tags_still_survive()
    {
        string directed = Approved
            .Replace("closed northbound for", "closed northbound… for")
            .Replace("Ferry Road", "[urgent] Ferry Road");
        Directs(directed);

        string result = await Apply();

        result.ShouldBe(directed);
    }

    /// <summary>
    ///     Joining is the same defect mirrored, and it has to be caught HERE rather than by the
    ///     word guard: swapping the full stop for a comma adds no word, so
    ///     <c>WordsEqual</c> sees an identical read.
    /// </summary>
    [Fact]
    public async Task A_joined_sentence_reverts_as_well()
    {
        Directs(Approved.Replace("closed section. Ferry", "closed section, Ferry"));

        string result = await Apply();

        result.ShouldBe(Approved);
    }

    private void Directs(string directed) =>
        _director.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(directed));

    private Task<string> Apply()
    {
        var jobRepository = Substitute.For<IJobRepository>();
        var statusUpdater = new JobStatusUpdater(
            jobRepository,
            Substitute.For<INotificationService>(),
            TimeProvider.System,
            NullLogger<BroadcastOrchestrator>.Instance);
        var sut = new DeliveryDirector(
            _director,
            Options.Create(new DeliveryOptions()),
            NullLogger<BroadcastOrchestrator>.Instance,
            statusUpdater);

        var context = new BroadcastWithTraffic
        {
            JobId = "de2d1b60",
            Location = "goldcoast",
            Traffic = "Traffic",
            TargetDurationSeconds = 30,
            Incidents = [],
            // A non-neutral style, or the director is skipped outright.
            DeliveryStyle = Option<DeliveryStylePolicy>.Some(
                new DeliveryStylePolicy("Warm, energetic Australian read.", ["urgent"], 2)),
            Model = Option<string>.Some("eleven_v3")
        };

        return sut.ApplyDeliveryDirectionAsync(context, Approved, CancellationToken.None);
    }
}
