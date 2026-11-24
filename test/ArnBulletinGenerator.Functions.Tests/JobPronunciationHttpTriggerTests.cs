using System.Net;
using System.Text;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     The whole point of addressing the pronunciation fix by JOB is that it can be market-scoped,
///     so the guard being wired is the behaviour under test here — an operator must not be able to
///     correct a word on a station they are not assigned to.
/// </summary>
public sealed class JobPronunciationHttpTriggerTests
{
    private const string JobId = "job-1";

    private readonly IJobPronunciationService _pronunciation = Substitute.For<IJobPronunciationService>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly JobPronunciationHttpTrigger _sut;

    public JobPronunciationHttpTriggerTests()
    {
        _marketAccess.EnsureCanActOnJobAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(Result.Success());

        _pronunciation.AddAliasRuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<JobAliasEdit>.Success(new JobAliasEdit(Dictionary(), AliasEditOutcome.Changed)));

        _sut = new JobPronunciationHttpTrigger(_pronunciation, _marketAccess);
    }

    [Fact]
    public async Task AddRule_OperatorOutsideTheJobsMarket_Returns403AndWritesNothing()
    {
        _marketAccess.EnsureCanActOnJobAsync(Arg.Any<string>(), false, JobId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Forbidden: you are not assigned to market 'Melbourne'."));

        var response = await _sut.AddRule(OperatorRequest("Coomera", "Koomerah"), JobId, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await _pronunciation.DidNotReceiveWithAnyArgs().AddAliasRuleAsync(
            default!, default!, default!, default, default);
    }

    [Fact]
    public async Task AddRule_AssignedOperator_SavesAndReportsWhatItDid()
    {
        var response = await _sut.AddRule(OperatorRequest("Coomera", "Koomerah"), JobId, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("\"Outcome\":\"changed\"");
        // Top-level Id is the DICTIONARY's, so the activity audit files the change under the
        // dictionary rather than the job it was made from.
        body.ShouldContain("\"Id\":\"pd-sydney\"");
        await _pronunciation.Received(1).AddAliasRuleAsync(
            JobId, "Coomera", "Koomerah", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Koomerah")]
    [InlineData("Coomera", "   ")]
    public async Task AddRule_MissingWordOrReplacement_Returns400(string word, string replacement)
    {
        var response = await _sut.AddRule(OperatorRequest(word, replacement), JobId, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddRule_BulletinAiredWithNoDictionary_Returns409()
    {
        _pronunciation.AddAliasRuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<JobAliasEdit>.Failure(JobPronunciationService.NoDictionaryOnJob));

        var response = await _sut.AddRule(OperatorRequest("Coomera", "Koomerah"), JobId, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddRule_AiredDictionaryNoLongerInLibrary_Returns404()
    {
        _pronunciation.AddAliasRuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<JobAliasEdit>.Failure(JobPronunciationService.DictionaryNotInLibrary));

        var response = await _sut.AddRule(OperatorRequest("Coomera", "Koomerah"), JobId, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static Microsoft.Azure.Functions.Worker.Http.HttpRequestData OperatorRequest(
        string word, string replacement)
    {
        var request = MockHttpRequestData.CreatePost(
            new { Word = word, Replacement = replacement },
            $"http://localhost/api/jobs/{JobId}/pronunciation-rules");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """{"auth_typ":"aad","claims":[{"typ":"oid","val":"scoped-op"},{"typ":"roles","val":"Operator"}]}""")));
        return request;
    }

    private static PronunciationDictionary Dictionary() =>
        PronunciationDictionary.Create(
            "Sydney Roads", null, "el-dict-1", "el-ver-2",
            [PronunciationRule.CreateAlias("Coomera", "Koomerah").Value],
            "Changed \"Coomera\" from bulletin review", "Tim", DateTime.UtcNow, "pd-sydney").Value;
}
