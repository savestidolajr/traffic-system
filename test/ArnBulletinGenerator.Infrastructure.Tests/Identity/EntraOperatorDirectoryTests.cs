using System.Net;
using Azure.Core;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Identity;

public sealed class EntraOperatorDirectoryTests
{
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string OperatorRoleId = "11111111-2222-3333-4444-555555555555";
    private const string AdminRoleId = "66666666-7777-8888-9999-000000000000";

    [Fact]
    public async Task ListRoleHoldersAsync_CachesFreshSnapshotAndUsesItStaleOnGraphFailure()
    {
        var handler = new GraphHandler();
        var clock = new TestClock(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
        var sut = CreateSut(handler, clock, new OperatorOptions
        {
            GraphCacheTtlSeconds = 60,
            GraphCacheStaleOnErrorSeconds = 300
        });

        var first = await sut.ListRoleHoldersAsync();
        first.IsSuccess.ShouldBeTrue();
        first.Value.ShouldContain(principal => principal.ObjectId == "member-1");
        int successfulRequestCount = handler.RequestCount;

        var fresh = await sut.ListRoleHoldersAsync();
        fresh.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(successfulRequestCount);

        clock.Advance(TimeSpan.FromSeconds(61));
        handler.FailAll = true;
        var stale = await sut.ListRoleHoldersAsync();

        stale.IsSuccess.ShouldBeTrue();
        stale.Value.ShouldContain(principal => principal.ObjectId == "member-1");
        handler.RequestCount.ShouldBeGreaterThan(successfulRequestCount);

        clock.Advance(TimeSpan.FromSeconds(300));
        var expired = await sut.ListRoleHoldersAsync();
        expired.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task ListRoleHoldersAsync_ConcurrentRefreshesShareOneGraphLoad()
    {
        var handler = new GraphHandler { BlockServicePrincipal = true };
        var sut = CreateSut(handler, new TestClock(DateTimeOffset.UtcNow), new OperatorOptions());

        var first = sut.ListRoleHoldersAsync();
        await handler.FirstServicePrincipalEntered.Task;
        var second = sut.ListRoleHoldersAsync();
        handler.ReleaseServicePrincipal.TrySetResult(true);

        var results = await Task.WhenAll(first, second);

        results.ShouldAllBe(result => result.IsSuccess);
        handler.RequestCount.ShouldBe(3);
    }

    [Fact]
    public async Task ListRoleHoldersAsync_GroupExpansionLimitFailsInsteadOfTruncating()
    {
        var handler = new GraphHandler { TwoGroups = true };
        var sut = CreateSut(handler, new TestClock(DateTimeOffset.UtcNow), new OperatorOptions
        {
            MaxGroupExpansions = 1
        });

        var result = await sut.ListRoleHoldersAsync();

        result.IsFailure.ShouldBeTrue();
        handler.GroupRequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task ListRoleHoldersAsync_GroupMemberLimitFailsInsteadOfTruncating()
    {
        var handler = new GraphHandler { TwoMembers = true };
        var sut = CreateSut(handler, new TestClock(DateTimeOffset.UtcNow), new OperatorOptions
        {
            MaxMembersPerGroup = 1
        });

        var result = await sut.ListRoleHoldersAsync();

        result.IsFailure.ShouldBeTrue();
        handler.GroupRequestCount.ShouldBe(1);
    }

    private static EntraOperatorDirectory CreateSut(
        GraphHandler handler,
        TestClock clock,
        OperatorOptions options)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(nameof(EntraOperatorDirectory)).Returns(_ => new HttpClient(handler));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AadClientId"] = ClientId
            })
            .Build();
        return new EntraOperatorDirectory(
            new TestTokenCredential(),
            configuration,
            factory,
            Substitute.For<ILogger<EntraOperatorDirectory>>(),
            Options.Create(options),
            clock);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class TestTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("graph-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccessToken("graph-test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private sealed class GraphHandler : HttpMessageHandler
    {
        private int _requestCount;
        private int _groupRequestCount;

        public bool FailAll { get; set; }
        public bool TwoGroups { get; set; }
        public bool TwoMembers { get; set; }
        public bool BlockServicePrincipal { get; set; }
        public TaskCompletionSource<bool> FirstServicePrincipalEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseServicePrincipal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount => Volatile.Read(ref _requestCount);
        public int GroupRequestCount => Volatile.Read(ref _groupRequestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (BlockServicePrincipal && path.Contains("servicePrincipals(appId=", StringComparison.OrdinalIgnoreCase))
            {
                FirstServicePrincipalEntered.TrySetResult(true);
                await ReleaseServicePrincipal.Task.WaitAsync(cancellationToken);
            }

            if (FailAll)
                return Response(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"offline\"}}");

            if (path.Contains("servicePrincipals(appId=", StringComparison.OrdinalIgnoreCase))
                return Response(HttpStatusCode.OK, $"{{\"id\":\"service-principal-1\",\"appRoles\":[{{\"id\":\"{OperatorRoleId}\",\"value\":\"Operator\"}},{{\"id\":\"{AdminRoleId}\",\"value\":\"Admin\"}}]}}");

            if (path.Contains("appRoleAssignedTo", StringComparison.OrdinalIgnoreCase))
            {
                string assignments = TwoGroups
                    ? $"{{\"value\":[{Assignment("group-1")},{Assignment("group-2")}]}}"
                    : $"{{\"value\":[{Assignment("group-1")}]}}";
                return Response(HttpStatusCode.OK, assignments);
            }

            if (path.Contains("transitiveMembers", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _groupRequestCount);
                string members = TwoMembers
                    ? "{\"value\":[{\"id\":\"member-1\",\"displayName\":\"Member One\",\"userPrincipalName\":\"member1@example.com\"},{\"id\":\"member-2\",\"displayName\":\"Member Two\",\"userPrincipalName\":\"member2@example.com\"}]}"
                    : "{\"value\":[{\"id\":\"member-1\",\"displayName\":\"Member One\",\"userPrincipalName\":\"member1@example.com\"}]}";
                return Response(HttpStatusCode.OK, members);
            }

            return Response(HttpStatusCode.NotFound, "{}");
        }

        private static string Assignment(string groupId) =>
            $"{{\"principalId\":\"{groupId}\",\"principalType\":\"Group\",\"principalDisplayName\":\"{groupId}\",\"appRoleId\":\"{OperatorRoleId}\"}}";

        private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body) };
    }
}
