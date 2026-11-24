using System.Net;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Covers the numeric editorial-settings endpoints and their injected duration defaults.
/// </summary>
public sealed class EditorialHttpTriggerTests
{
    private readonly IEditorialSettingsRepository _settingsRepository =
        Substitute.For<IEditorialSettingsRepository>();

    private readonly EditorialHttpTrigger _sut;

    public EditorialHttpTriggerTests()
    {

        _sut = new EditorialHttpTrigger(
            _settingsRepository,
            Options.Create(new SpeechDurationOptions { MaxSlowDownFactor = 1.0 }),
            TimeProvider.System, Substitute.For<ILogger<EditorialHttpTrigger>>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();



    [Fact]
    public async Task GetEffectiveEditorialSettings_UsesInjectedSpeechDurationDefaults()
    {
        _settingsRepository.GetApplicableAsync(
                Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EditorialSettings>>.Success([]));

        var response = await _sut.GetEffectiveEditorialSettings(
            MockHttpRequestData.CreateGet(
                "http://localhost/api/editorial/settings/effective?market=sydney"),
            Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var effective = await MockHttpRequestData.ReadResponseJsonAsync<EffectiveEditorialSettingsDto>(response);
        effective.ShouldNotBeNull();
        effective!.MaxSlowDownFactor.ShouldBe(1.0);
    }

    [Fact]
    public async Task GetEffectiveEditorialSettings_ReflectsMarketScopedOverride()
    {
        _settingsRepository.GetApplicableAsync(
                Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EditorialSettings>>.Success(
                [new EditorialSettings(EditorialScope.Market, "sydney", MaxSlowDownFactor: 1.2)]));

        var response = await _sut.GetEffectiveEditorialSettings(
            MockHttpRequestData.CreateGet(
                "http://localhost/api/editorial/settings/effective?market=sydney"),
            Ctx());

        var effective = await MockHttpRequestData.ReadResponseJsonAsync<EffectiveEditorialSettingsDto>(response);
        effective.ShouldNotBeNull();
        effective!.MaxSlowDownFactor.ShouldBe(1.2);
        effective.ConfiguredByArn.ShouldContain(nameof(EditorialSettings.MaxSlowDownFactor));
    }

    [Fact]
    public async Task UpsertEditorialSettings_OutOfRangeSlowDownFactor_ReturnsBadRequest()
    {
        var request = new UpsertEditorialSettingsRequest(
            EditorialScope.Global, null, MaxSlowDownFactor: 1.9);

        var response = await _sut.UpsertEditorialSettings(
            MockHttpRequestData.CreatePut(
                request, "http://localhost/api/editorial/settings"),
            Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _settingsRepository.DidNotReceive().SaveAsync(
            Arg.Any<EditorialSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertEditorialSettings_AcceptsAllSlotFitOverrides()
    {
        EditorialSettings? saved = null;
        _settingsRepository.SaveAsync(
                Arg.Do<EditorialSettings>(value => saved = value),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var request = new UpsertEditorialSettingsRequest(
            EditorialScope.Global,
            null,
            MaxSlowDownFactor: 1.2,
            MaxSpeedUpFactor: 1.25,
            MaxSilencePadSeconds: 1.4,
            MaxSafeOverrunTrimSeconds: 8.0);

        var response = await _sut.UpsertEditorialSettings(
            MockHttpRequestData.CreatePut(request, "http://localhost/api/editorial/settings"),
            Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.ShouldNotBeNull();
        saved!.MaxSlowDownFactor.ShouldBe(1.2);
        saved.MaxSpeedUpFactor.ShouldBe(1.25);
        saved.MaxSilencePadSeconds.ShouldBe(1.4);
        saved.MaxSafeOverrunTrimSeconds.ShouldBe(8.0);
    }
}
