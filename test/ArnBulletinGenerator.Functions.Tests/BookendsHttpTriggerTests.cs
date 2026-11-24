using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Covers the bookends trigger surface the delivery-style tests exercise for that library:
///     Name/Description length caps, pair template validation (both lines blank rejected, per-side
///     cap), the case-insensitive duplicate-name guard across the WHOLE library (no kind split),
///     the persistence-free preview resolving BOTH lines with a token reported once, and the typed
///     in-use Conflict -> 409 mapping on delete.
/// </summary>
public sealed class BookendsHttpTriggerTests
{
    private readonly IBookendService _bookends = Substitute.For<IBookendService>();
    private readonly BookendsHttpTrigger _sut;

    public BookendsHttpTriggerTests()
    {
        _sut = new BookendsHttpTrigger(_bookends, Substitute.For<ILogger<BookendsHttpTrigger>>());

        // Default: no existing bookends, so duplicate-name tests that don't care about it never
        // hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success([]));
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static CreateBookendRequest ValidCreateRequest(string name = "Drive-Time Pair") =>
        new(name, "A description.", "Looking over {city} today.", "That's it for {city} traffic.");

    private static Bookend ExistingBookend(string name) =>
        Bookend.Create(name, null,
            new BookendTemplate("Looking over {city} today", "That's it for {city} traffic."),
            null, null, DateTime.UtcNow).Value;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Create_NameTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest(new string('a', ConfigValidationLimits.BookendNameMax + 1));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Create_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with
        {
            Description = new string('d', ConfigValidationLimits.BookendDescriptionMax + 1)
        };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    [Fact]
    public async Task Create_IntroTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with { IntroText = new string('x', BookendTemplate.MaxTextLength + 1) };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain($"at most {BookendTemplate.MaxTextLength}");
        await _bookends.DidNotReceive().CreateAsync(
            Arg.Any<CreateBookendInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_OutroTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with { OutroText = new string('x', BookendTemplate.MaxTextLength + 1) };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("outro must be at most");
        await _bookends.DidNotReceive().CreateAsync(
            Arg.Any<CreateBookendInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_BothLinesBlank_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with { IntroText = "   ", OutroText = "" };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("needs an intro, an outro, or both");
        await _bookends.DidNotReceive().CreateAsync(
            Arg.Any<CreateBookendInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success([ExistingBookend("Drive-Time Pair")]));

        // Case-insensitive match against the existing pair's name, library-wide.
        var request = ValidCreateRequest("drive-time pair");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("already exists");
        await _bookends.DidNotReceive().CreateAsync(
            Arg.Any<CreateBookendInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ValidRequest_Succeeds()
    {
        var created = ExistingBookend("Drive-Time Pair");
        _bookends.CreateAsync(Arg.Any<CreateBookendInput>(), Arg.Any<CancellationToken>())
            .Returns(Result<Bookend>.Success(created));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(ValidCreateRequest(), "http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var dto = JsonSerializer.Deserialize<BookendDto>(body, JsonOptions);
        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(created.Id);
        dto.IntroText.ShouldBe("Looking over {city} today");
        dto.OutroText.ShouldBe("That's it for {city} traffic.");
        // A token referenced by BOTH lines is exposed once in the union.
        dto.Tokens.ShouldBe(["city"]);
    }

    [Fact]
    public async Task List_ReturnsAllPairs()
    {
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success([ExistingBookend("Drive-Time Pair")]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/bookends"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var list = JsonSerializer.Deserialize<BookendsListResponseDto>(body, JsonOptions);
        list.ShouldNotBeNull();
        list.Items.Count.ShouldBe(1);
        list.Items[0].IntroText.ShouldBe("Looking over {city} today");
        list.Items[0].OutroText.ShouldBe("That's it for {city} traffic.");
    }

    [Fact]
    public async Task Preview_ResolvesBothLines_WithBuiltInsAndOverrides()
    {
        var request = new PreviewBookendRequest(
            "Looking over {city} for {station}",
            "That's it from {city}.",
            "goldcoast", "KIIS 106.5 Gold Coast",
            new Dictionary<string, string> { ["city"] = "Gold Coast" });

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Preview(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends/preview"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var response = JsonSerializer.Deserialize<PreviewBookendResponse>(body, JsonOptions);
        response.ShouldNotBeNull();
        response.ResolvedIntro.ShouldBe("Looking over Gold Coast for KIIS 106.5 Gold Coast");
        response.ResolvedOutro.ShouldBe("That's it from Gold Coast.");
        response.UnresolvedTokens.ShouldBeEmpty();
        response.Values["station"].ShouldBe("KIIS 106.5 Gold Coast");
        // The override wins over the built-in derivation ("goldcoast" would derive to "Goldcoast").
        response.Values["city"].ShouldBe("Gold Coast");
    }

    [Fact]
    public async Task Preview_ReportsUnresolvedTokenOnce_WhenInBothLines_WithoutPersistence()
    {
        var request = new PreviewBookendRequest(
            "Looking over {city} in {region}",
            "That's it from {region}.",
            "melbourne", "KIIS 101.1", null);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Preview(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends/preview"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var response = JsonSerializer.Deserialize<PreviewBookendResponse>(body, JsonOptions);
        response.ShouldNotBeNull();
        response.UnresolvedTokens.ShouldBe(["region"]);
        response.ResolvedIntro.ShouldNotContain("{region}");
        response.ResolvedOutro.ShouldNotContain("{region}");
        await _bookends.DidNotReceive().CreateAsync(
            Arg.Any<CreateBookendInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_BothLinesBlank_ReturnsBadRequest()
    {
        var request = new PreviewBookendRequest(null, null, "melbourne", "KIIS 101.1", null);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Preview(MockHttpRequestData.CreatePost(request, "http://localhost/api/bookends/preview"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("needs an intro, an outro, or both");
    }

    [Fact]
    public async Task Delete_InUse_ReturnsConflict()
    {
        _bookends.DeleteAsync("bk-1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.Conflict(
                "library.in_use", "This bookend is still assigned to melbourne. Unassign it before deleting.")));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Delete(MockHttpRequestData.CreateDelete("http://localhost/api/bookends/bk-1"), "bk-1", Ctx()));

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("still assigned");
    }

    [Fact]
    public async Task Delete_NotInUse_ReturnsNoContent()
    {
        _bookends.DeleteAsync("bk-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var response = await _sut.Delete(
            MockHttpRequestData.CreateDelete("http://localhost/api/bookends/bk-1"), "bk-1", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_TypedInvalidInput_ReturnsBadRequest_NotServerError()
    {
        // A permanent client-input failure (over-ceiling single version, e.g. VersionHistoryTrimmer)
        // must map to 400 via its TYPED disposition — the message alone contains no matching
        // keyword, so message matching alone would send it to a 500.
        _bookends.UpdateAsync(Arg.Any<UpdateBookendInput>(), Arg.Any<CancellationToken>())
            .Returns(Result<Bookend>.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large",
                "This bookend is too large to save — remove or shorten some entries and save again.")));

        var request = new UpdateBookendRequest(
            "Drive-Time Pair", "A description.", "Looking over {city} today.", "That's it for {city} traffic.");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/bookends/bk-1"), "bk-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("too large to save");
    }
}
