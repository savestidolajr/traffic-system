using System.Net;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class ScheduleLifecycleE2ETests(E2EHttpClientFixture fixture)
{
    [Fact(Timeout = 150_000)]
    public async Task Schedule_CreateUpdatePollDelete_RoundTripsAndCreatesJob()
    {
        var market = $"e2e-schedule-{Guid.NewGuid():N}"[..24];
        var voiceProfileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, "schedule");
        var createBody = new
        {
            Name = "E2E Schedule",
            Station = "E2E FM",
            Timezone = "Australia/Brisbane",
            CronExpression = "*/5 * * * *", // at the 5-minute floor; lead time is 15min so it is due on the next poll
            Enabled = true,
            TargetDurationSeconds = 30.0,
            NotifyBeforePlayoutMinutes = 5,
            EscalationBeforePlayoutMinutes = 2,
            Coverage = new { Latitude = -28.0167, Longitude = 153.4, RadiusKm = 35.0 },
            Content = new { Traffic = true, Weather = true, Fuel = false },
            VoiceProfileId = voiceProfileId
        };

        using (var create = await fixture.Client.PutAsync($"schedules/{market}", E2EHttpClientFixture.JsonContent(createBody)))
        {
            create.StatusCode.ShouldBe(HttpStatusCode.OK);
            var created = await BackendE2ETestSupport.ReadJsonAsync(create);
            created.ContainsText("E2E FM").ShouldBeTrue();
        }

        using (var list = await fixture.Client.GetAsync("schedules"))
        {
            list.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await list.Content.ReadAsStringAsync()).ShouldContain(market);
        }

        var updateBody = new
        {
            TargetDurationSeconds = 45.0
        };
        using (var update = await fixture.Client.PutAsync($"schedules/{market}", E2EHttpClientFixture.JsonContent(updateBody)))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
            var updated = await BackendE2ETestSupport.ReadJsonAsync(update);
            updated.ContainsText("45").ShouldBeTrue();
        }

        await Eventually.UntilAsync(async () =>
        {
            using var jobs = await fixture.Client.GetAsync("jobs");
            if (jobs.StatusCode != HttpStatusCode.OK)
                return false;
            var json = await jobs.Content.ReadAsStringAsync();
            return json.Contains(market, StringComparison.OrdinalIgnoreCase) || json.Contains("E2E FM", StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(5), "Schedule poller did not create a job for the E2E schedule.");

        using (var delete = await fixture.Client.DeleteAsync($"schedules/{market}"))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var getDeleted = await fixture.Client.GetAsync($"schedules/{market}"))
        {
            getDeleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}
