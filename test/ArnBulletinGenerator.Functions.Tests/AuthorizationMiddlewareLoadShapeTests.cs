using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Exercises the real authorization boundary with a deliberately mixed principal population.
///     The test is bounded and entirely in-process: it protects against mutable per-invocation auth
///     state leaking between concurrent Function contexts without depending on host scheduling.
/// </summary>
public sealed class AuthorizationMiddlewareLoadShapeTests
{
    private const string AdminRoute = "/api/style-rules";
    private const string AdminMethod = "PUT";

    [Fact]
    public async Task MixedPrincipalShapes_ParallelDecisionsMatchSequentialDecisions()
    {
        var cases = CreateCases();
        var middleware = new AuthorizationMiddleware(
            Configuration(), Substitute.For<ILogger<AuthorizationMiddleware>>());

        var expected = new Dictionary<string, Decision>(StringComparer.Ordinal);
        foreach (var principalCase in cases)
            expected[principalCase.Name] = await InvokeAsync(middleware, principalCase);

        var results = new ConcurrentBag<(string Name, Decision Actual)>();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(async index =>
        {
            var principalCase = cases[index % cases.Count];
            results.Add((principalCase.Name, await InvokeAsync(middleware, principalCase)));
        }));

        results.Count.ShouldBe(200);
        foreach (var (name, actual) in results)
            AssertDecision(actual, expected[name], name);

        AssertDecision(expected["admin-200-markets"], new Decision(HttpStatusCode.OK, true, ["Admin"]), "admin");
        AssertDecision(expected["operator-200-markets"], new Decision(HttpStatusCode.OK, true, ["Operator", "Admin"]), "operator");
        AssertDecision(expected["unassigned-operator"], new Decision(HttpStatusCode.OK, true, ["Operator", "Admin"]), "unassigned");
        AssertDecision(expected["malformed-base64"], new Decision(HttpStatusCode.Unauthorized, false, []), "malformed");
        AssertDecision(expected["dev-fallback"], new Decision(HttpStatusCode.OK, true, ["Admin"]), "fallback");
    }

    private static IReadOnlyList<PrincipalCase> CreateCases()
    {
        string[] markets = Enumerable.Range(1, 200).Select(index => $"market-{index:D3}").ToArray();
        var adminClaims = new List<Claim>
        {
            new("oid", "admin-id"),
            new("roles", "Admin")
        };
        adminClaims.AddRange(markets.Select(market => new Claim("market", market)));
        string adminJson = JsonSerializer.Serialize(new { auth_typ = "aad", claims = adminClaims });
        string marketClaims = JsonSerializer.Serialize(
            markets.Select(market => new Claim("market", market)).ToArray());

        return
        [
            new("admin-200-markets", Encode(adminJson)),
            new("operator-200-markets", Encode($"{{\"identityProvider\":\"aad\",\"userId\":\"operator-id\",\"userDetails\":\"operator@arn.com.au\",\"userRoles\":[\"Operator\"],\"claims\":{marketClaims}}}")),
            new("unassigned-operator", Encode("{\"identityProvider\":\"aad\",\"userId\":\"unassigned-id\",\"userDetails\":\"unassigned@arn.com.au\",\"userRoles\":[\"Operator\"]}")),
            new("malformed-base64", "this-is-not-base64"),
            new("dev-fallback", null)
        ];
    }

    private static async Task<Decision> InvokeAsync(
        AuthorizationMiddleware middleware,
        PrincipalCase principalCase)
    {
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        var request = new FakeHttpRequestData(
            context, new Uri($"http://localhost{AdminRoute}"), AdminMethod, Stream.Null);
        if (principalCase.Header is not null)
            request.Headers.Add(ClientPrincipal.HeaderName, principalCase.Header);

        var features = new TestInvocationFeatures();
        var httpFeature = Substitute.For<IHttpRequestDataFeature>();
        httpFeature.GetHttpRequestDataAsync(Arg.Any<FunctionContext>())
            .Returns(new ValueTask<HttpRequestData?>(request));
        features.Set(httpFeature);

        var invocationResult = Substitute.For<InvocationResult>();
        var bindingsType = typeof(InvocationResult).Assembly.GetType(
            "Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature")!;
        object bindings = Substitute.For([bindingsType], [])!;
        bindingsType.GetProperty("InvocationResult")!.SetValue(bindings, invocationResult);
        features.Register(bindingsType, bindings);
        context.Features.Returns(features);
        context.GetInvocationResult().Returns(invocationResult);

        bool nextCalled = false;
        await middleware.Invoke(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var response = context.GetInvocationResult().Value as HttpResponseData;
        var status = nextCalled
            ? HttpStatusCode.OK
            : response is null ? (HttpStatusCode)0 : response.StatusCode;
        var parsed = ClientPrincipal.Parse(request);
        IReadOnlyList<string> roles = parsed?.Roles
            ?? (principalCase.Header is null ? ["Admin"] : []);
        if (parsed is not null && !roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            roles = [..roles, "Admin"];
        return new Decision(status, nextCalled, roles);
    }

    private static void AssertDecision(Decision actual, Decision expected, string name)
    {
        actual.Status.ShouldBe(expected.Status, name);
        actual.NextCalled.ShouldBe(expected.NextCalled, name);
        actual.Roles.ShouldBe(expected.Roles, name);
    }


    private static IConfiguration Configuration()
    {
        var configuration = Substitute.For<IConfiguration>();
        configuration["AadClientId"].Returns("11111111-1111-1111-1111-111111111111");
        configuration[ClientPrincipal.DevFallbackRoleSetting].Returns("Admin");
        return configuration;
    }
    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private sealed record PrincipalCase(string Name, string? Header);

    private sealed record Decision(HttpStatusCode Status, bool NextCalled, IReadOnlyList<string> Roles);

    private sealed record Claim(string typ, string val);
}
