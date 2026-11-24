using System.Net;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class HealthAndAuthE2ETests(E2EHttpClientFixture fixture)
{
    [Fact]
    public async Task Health_AllowsAnonymous_ReturnsReadyOrDegraded()
    {
        using var response = await fixture.AnonymousClient.GetAsync("health");
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("storage", Case.Insensitive);
    }

    [Fact]
    public async Task Jobs_WithoutPrincipal_ReturnsUnauthorized()
    {
        using var response = await fixture.AnonymousClient.GetAsync("jobs");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    ///     A station's schedule is the market's own operational config, so an Operator may write
    ///     it (market-guarded); deleting the station is not, and stays Admin. Both halves in one
    ///     test so the grant can never be widened to DELETE without this failing.
    /// </summary>
    [Fact]
    public async Task Schedules_OperatorMayWriteButNotDelete()
    {
        var market = $"e2e-rbac-{Guid.NewGuid():N}"[..20];
        var voiceProfileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, "rbac");
        var body = new
        {
            Name = "E2E RBAC Probe",
            Station = "E2E",
            Timezone = "Australia/Brisbane",
            CronExpression = "0 7 * * 1-5",
            Enabled = false,
            TargetDurationSeconds = 30.0,
            NotifyBeforePlayoutMinutes = 5,
            EscalationBeforePlayoutMinutes = 2,
            Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 20.0 },
            Content = new { Traffic = true, Weather = false, Fuel = false },
            VoiceProfileId = voiceProfileId
        };

        using (var write = await fixture.OperatorClient.PutAsync(
                   $"schedules/{market}", E2EHttpClientFixture.JsonContent(body)))
            write.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var operatorDelete = await fixture.OperatorClient.DeleteAsync($"schedules/{market}"))
            operatorDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using (var adminDelete = await fixture.Client.DeleteAsync($"schedules/{market}"))
            adminDelete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    ///     Station-scope guidance rides the unscoped PUT /guidance / DELETE /guidance/{id}
    ///     routes, so their role depends on the payload/query scope: an Operator may write
    ///     Station rows for a market they hold (resolved via the schedule catalogue) but is
    ///     403'd for Global rows and for Station rows in an unassigned market; Admin is
    ///     unchanged. Mirrors Schedules_OperatorMayWriteButNotDelete with the scoping made
    ///     real (the operator is assigned exactly one market).
    /// </summary>
    [Fact]
    public async Task Guidance_OperatorMayWriteStationScopeForAssignedMarket_ButNotGlobalOrOtherMarkets()
    {
        // The Dev Operator principal's object id — assigned one market below so the guard is
        // exercised; restored to unrestricted afterwards so sibling tests see the default scope.
        const string operatorObjectId = "00000000-0000-0000-0000-000000000001";
        var marketA = $"e2e-guid-{Guid.NewGuid():N}"[..20];
        var marketB = $"e2e-guid-{Guid.NewGuid():N}"[..20];
        const string stationA = "E2E RBAC FM One";
        const string stationB = "E2E RBAC FM Two";

        var voiceProfileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, "guid-rbac");
        foreach (var (market, station) in new[] { (marketA, stationA), (marketB, stationB) })
        {
            var scheduleBody = new
            {
                Name = "E2E RBAC Guidance Seed",
                Station = station,
                Timezone = "Australia/Brisbane",
                CronExpression = "0 7 * * 1-5",
                Enabled = false,
                TargetDurationSeconds = 30.0,
                NotifyBeforePlayoutMinutes = 5,
                EscalationBeforePlayoutMinutes = 2,
                Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 20.0 },
                Content = new { Traffic = true, Weather = false, Fuel = false },
                VoiceProfileId = voiceProfileId
            };
            using (var seed = await fixture.Client.PutAsync(
                       $"schedules/{market}", E2EHttpClientFixture.JsonContent(scheduleBody)))
                seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var assign = await fixture.Client.PutAsync(
                   $"operators/{operatorObjectId}",
                   E2EHttpClientFixture.JsonContent(new { DisplayName = "E2E RBAC Operator", Markets = new[] { marketA } })))
            assign.StatusCode.ShouldBe(HttpStatusCode.OK);

        try
        {
            string stationGuidanceId;
            using (var writeStation = await fixture.OperatorClient.PutAsync(
                       "guidance",
                       E2EHttpClientFixture.JsonContent(new
                       {
                           Scope = "Station",
                           ScopeId = stationA,
                           Type = "Style",
                           Text = "Operator-maintained station rule",
                           Enabled = true
                       })))
            {
                writeStation.StatusCode.ShouldBe(HttpStatusCode.OK);
                var json = await BackendE2ETestSupport.ReadJsonAsync(writeStation);
                stationGuidanceId = json.StringProp("id", "Id")!;
                stationGuidanceId.ShouldNotBeNullOrWhiteSpace();
            }

            using (var writeGlobal = await fixture.OperatorClient.PutAsync(
                       "guidance",
                       E2EHttpClientFixture.JsonContent(new
                       {
                           Scope = "Global",
                           ScopeId = (string?)null,
                           Type = "Editorial",
                           Text = "Operator must not mint global guidance",
                           Enabled = true
                       })))
                writeGlobal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            using (var writeOtherMarket = await fixture.OperatorClient.PutAsync(
                       "guidance",
                       E2EHttpClientFixture.JsonContent(new
                       {
                           Scope = "Station",
                           ScopeId = stationB,
                           Type = "Style",
                           Text = "Operator must not touch another market's station",
                           Enabled = true
                       })))
                writeOtherMarket.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            string globalGuidanceId;
            using (var adminWriteGlobal = await fixture.Client.PutAsync(
                       "guidance",
                       E2EHttpClientFixture.JsonContent(new
                       {
                           Scope = "Global",
                           ScopeId = (string?)null,
                           Type = "Editorial",
                           Text = "Admin-owned global guidance",
                           Enabled = true
                       })))
            {
                adminWriteGlobal.StatusCode.ShouldBe(HttpStatusCode.OK);
                var json = await BackendE2ETestSupport.ReadJsonAsync(adminWriteGlobal);
                globalGuidanceId = json.StringProp("id", "Id")!;
                globalGuidanceId.ShouldNotBeNullOrWhiteSpace();
            }

            using (var deleteGlobal = await fixture.OperatorClient.DeleteAsync(
                       $"guidance/{globalGuidanceId}?scope=Global"))
                deleteGlobal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            using (var deleteStation = await fixture.OperatorClient.DeleteAsync(
                       $"guidance/{stationGuidanceId}?scope=Station&scopeId={Uri.EscapeDataString(stationA)}"))
                deleteStation.StatusCode.ShouldBe(HttpStatusCode.OK);

            using (var adminDeleteGlobal = await fixture.Client.DeleteAsync(
                       $"guidance/{globalGuidanceId}?scope=Global"))
                adminDeleteGlobal.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            // Restore the shared Dev Operator to the default unrestricted scope so sibling
            // tests (e.g. Schedules_OperatorMayWriteButNotDelete) are unaffected by ordering.
            using var restore = await fixture.Client.PutAsync(
                $"operators/{operatorObjectId}",
                E2EHttpClientFixture.JsonContent(new { DisplayName = "E2E RBAC Operator", Markets = Array.Empty<string>() }));
            restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
