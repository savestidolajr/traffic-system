using System.Net;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public sealed class ElevenLabsPronunciationDictionaryManagementTests
{
    private static ElevenLabsOptions Options() => new()
    {
        ApiKey = "k",
        BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech",
        ModelId = "eleven_v3",
        OutputFormat = "mp3_44100_192",
        UseTimestampsEndpoint = false
    };

    private static ElevenLabsClient Client(ElevenLabsOptions opts, FakeHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<ElevenLabsClient>.Instance);

    // Locator threading and stack ordering live in ElevenLabsPronunciationDictionaryTests; this
    // class covers the dictionary MANAGEMENT endpoints (create / add rules / remove rules).

    // ── Management endpoints ────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PostsAddFromRules_WithAliasAndPhonemeShapes_ParsesIds()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            content: """{"id":"d1","version_id":"v1","version_rules_num":2}""");
        var client = Client(Options(), handler);

        var rules = new[]
        {
            PronunciationRule.CreateAlias("Warringah", "Warringer").Value,
            PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value
        };

        var result = await client.CreateAsync("GC", rules);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DictionaryId.ShouldBe("d1");
        result.Value.VersionId.ShouldBe("v1");
        handler.LastUri!.ShouldContain("/pronunciation-dictionaries/add-from-rules");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        doc.RootElement.GetProperty("name").GetString().ShouldBe("GC");
        var arr = doc.RootElement.GetProperty("rules");
        arr.GetArrayLength().ShouldBe(2);

        var alias = arr[0];
        alias.GetProperty("type").GetString().ShouldBe("alias");
        alias.GetProperty("string_to_replace").GetString().ShouldBe("Warringah");
        alias.GetProperty("alias").GetString().ShouldBe("Warringer");

        var phoneme = arr[1];
        phoneme.GetProperty("type").GetString().ShouldBe("phoneme");
        phoneme.GetProperty("phoneme").GetString().ShouldBe("ˈlɒklən");
        phoneme.GetProperty("alphabet").GetString().ShouldBe("ipa");
    }

    [Fact]
    public async Task AddRulesAsync_PostsToAddRules_ReturnsNewVersion()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            content: """{"id":"d1","version_id":"v9","version_rules_num":3}""");
        var client = Client(Options(), handler);

        var result = await client.AddRulesAsync("d1", [PronunciationRule.CreateAlias("Nerang", "Nerrang").Value]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("v9");
        handler.LastUri!.ShouldContain("/pronunciation-dictionaries/d1/add-rules");
    }

    [Fact]
    public async Task RemoveRulesAsync_PostsRuleStrings_ReturnsNewVersion()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            content: """{"id":"d1","version_id":"v10","version_rules_num":1}""");
        var client = Client(Options(), handler);

        var result = await client.RemoveRulesAsync("d1", ["Coomera", "Nerang"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("v10");
        handler.LastUri!.ShouldContain("/pronunciation-dictionaries/d1/remove-rules");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var strings = doc.RootElement.GetProperty("rule_strings");
        strings.GetArrayLength().ShouldBe(2);
        strings[0].GetString().ShouldBe("Coomera");
    }

    [Fact]
    public async Task AddRulesAsync_HttpError_SurfacesFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.UnprocessableEntity, content: """{"detail":{"message":"bad"}}""");
        var client = Client(Options(), handler);

        var result = await client.AddRulesAsync("d1", [PronunciationRule.CreateAlias("Nerang", "Nerrang").Value]);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task SetRulesAsync_PostsToSetRules_ReplacesAllRules_ReturnsNewVersion()
    {
        // Set-rules backs the alias-only version line: the same dictionary, at a version holding
        // only the rules that are safe on every model.
        var handler = new FakeHandler(HttpStatusCode.OK,
            content: """{"id":"d1","version_id":"v11","version_rules_num":1}""");
        var client = Client(Options(), handler);

        var result = await client.SetRulesAsync("d1", [PronunciationRule.CreateAlias("Warringah", "Warringa").Value]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("v11");
        handler.LastUri!.ShouldContain("/pronunciation-dictionaries/d1/set-rules");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var rules = doc.RootElement.GetProperty("rules");
        rules.GetArrayLength().ShouldBe(1);
        rules[0].GetProperty("type").GetString().ShouldBe("alias");
        rules[0].GetProperty("alias").GetString().ShouldBe("Warringa");
    }

    [Fact]
    public async Task ArchiveAsync_PatchesTheArchivedFlag_Succeeds()
    {
        // ElevenLabs has no hard delete for pronunciation dictionaries; archiving (PATCH
        // archived=true) is the closest removal and the finding-6 rollback target.
        var handler = new FakeHandler(HttpStatusCode.OK, content: """{"id":"d1"}""");
        var client = Client(Options(), handler);

        var result = await client.ArchiveAsync("d1");

        result.IsSuccess.ShouldBeTrue();
        handler.LastUri!.ShouldContain("/pronunciation-dictionaries/d1");
        handler.LastMethod.ShouldBe(HttpMethod.Patch);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        doc.RootElement.GetProperty("archived").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_NotFound_TreatedAsSuccess()
    {
        // Idempotent rollback: a dictionary that is already gone has nothing left to archive.
        var handler = new FakeHandler(HttpStatusCode.NotFound);
        var client = Client(Options(), handler);

        var result = await client.ArchiveAsync("d1");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_HttpError_SurfacesFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.UnprocessableEntity, content: """{"detail":{"message":"bad"}}""");
        var client = Client(Options(), handler);

        var result = await client.ArchiveAsync("d1");

        result.IsFailure.ShouldBeTrue();
    }

    /// <summary>
    ///     ElevenLabs applies alias rules in the order supplied and the FIRST match wins, so a
    ///     general rule sent before a more specific one silently swallows it. Measured against the
    ///     live API on 2026-08-24 with identical text ("The Warringah Freeway is busy this morning."):
    ///     general-first heard "The Banana Freeway", specific-first heard "The kiwi".
    ///     <para>
    ///         The live defect (test job a907ac7b): "Warringah" sat at position 8 of the Sydney Roads
    ///         dictionary and an operator's later "Warringah Freeway" fix at position 19, so two
    ///         dictionary edits appeared to do nothing. The wire payload must therefore always be
    ///         ordered longest replaced-string first, whatever order the rules are stored in.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task BuildRulePayloads_SendsLongestMatchFirst_SoASpecificRuleIsNeverShadowed()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            content: """{"id":"d1","version_id":"v1","version_rules_num":3}""");
        var client = Client(Options(), handler);

        // Stored in the order an operator actually created them: general first, specific appended.
        var rules = new[]
        {
            PronunciationRule.CreateAlias("Warringah", "Were-rin-gah").Value,
            PronunciationRule.CreateAlias("Glebe", "Gleeb").Value,
            PronunciationRule.CreateAlias("Warringah Freeway", "woringguh Freeway").Value
        };

        var result = await client.AddRulesAsync("d1", rules);

        result.IsSuccess.ShouldBeTrue();
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var sent = doc.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Select(r => r.GetProperty("string_to_replace").GetString())
            .ToList();

        sent.ShouldBe(["Warringah Freeway", "Warringah", "Glebe"]);
        sent.IndexOf("Warringah Freeway").ShouldBeLessThan(sent.IndexOf("Warringah"));
    }

    private sealed class FakeHandler(HttpStatusCode status, string content = "", byte[]? bytes = null)
        : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri?.ToString();
            LastMethod = request.Method;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = bytes is not null
                    ? new ByteArrayContent(bytes)
                    : new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
