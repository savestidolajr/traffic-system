using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Middleware;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class AuthorizationMiddlewareTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // TryParsePrincipal — base64 + JSON + auth_typ + roles claim extraction
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParsePrincipal_ValidAadWithRoles_ReturnsRoles()
    {
        string json = """
            {
              "auth_typ": "aad",
              "claims": [
                { "typ": "oid", "val": "abc123" },
                { "typ": "name", "val": "John Doe" },
                { "typ": "roles", "val": "Operator" },
                { "typ": "roles", "val": "Admin" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var parsed = AuthorizationMiddleware.TryParsePrincipalForTesting(json, out var roles, out _);
        parsed.ShouldBeTrue();
        roles.ShouldContain("Operator");
        roles.ShouldContain("Admin");
        roles.Count.ShouldBe(2);
    }

    [Fact]
    public void TryParsePrincipal_LegacyRoleClaimType_AlsoExtracted()
    {
        string json = """
            {
              "auth_typ": "aad",
              "claims": [
                { "typ": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "val": "Operator" }
              ]
            }
            """;

        var parsed = AuthorizationMiddleware.TryParsePrincipalForTesting(json, out var roles, out _);
        parsed.ShouldBeTrue();
        roles.ShouldContain("Operator");
    }

    [Fact]
    public void TryParsePrincipal_NonAadAuthTyp_Rejected()
    {
        string json = """{"auth_typ":"facebook","claims":[]}""";
        var parsed = AuthorizationMiddleware.TryParsePrincipalForTesting(json, out _, out _);
        parsed.ShouldBeFalse();
    }

    [Fact]
    public void TryParsePrincipal_InvalidJson_Rejected()
    {
        var parsed = AuthorizationMiddleware.TryParsePrincipalForTesting("not-json", out _, out _);
        parsed.ShouldBeFalse();
    }

    [Fact]
    public void TryParsePrincipal_NoClaimsArray_StillSucceedsWithEmptyRoles()
    {
        string json = """{"auth_typ":"aad"}""";
        var parsed = AuthorizationMiddleware.TryParsePrincipalForTesting(json, out var roles, out _);
        parsed.ShouldBeTrue();
        roles.ShouldBeEmpty();
    }

    [Fact]
    public void TryParsePrincipal_SwaClientPrincipal_ExtractsAppRolesOnly()
    {
        // The SWA linked backend forwards identityProvider + userRoles (no auth_typ).
        string json = """
            {
              "identityProvider": "aad",
              "userDetails": "op@arn.com.au",
              "userRoles": ["anonymous", "authenticated", "Operator", "Admin"]
            }
            """;

        var parsed = AuthorizationMiddleware.TryParsePrincipalForTesting(json, out var roles, out _);
        parsed.ShouldBeTrue();
        roles.ShouldContain("Operator");
        roles.ShouldContain("Admin");
        roles.ShouldNotContain("authenticated");
        roles.ShouldNotContain("anonymous");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HasRequiredRole — Admin elevates, Operator only matches Operator
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasRequiredRole_AdminCanDoEverything()
    {
        AuthorizationMiddleware.HasRequiredRoleForTesting(["Admin"], "Admin").ShouldBeTrue();
        AuthorizationMiddleware.HasRequiredRoleForTesting(["Admin"], "Operator").ShouldBeTrue();
    }

    [Fact]
    public void HasRequiredRole_OperatorCanDoOperatorOnly()
    {
        AuthorizationMiddleware.HasRequiredRoleForTesting(["Operator"], "Operator").ShouldBeTrue();
        AuthorizationMiddleware.HasRequiredRoleForTesting(["Operator"], "Admin").ShouldBeFalse();
    }

    [Fact]
    public void HasRequiredRole_NoRoles_Denied()
    {
        AuthorizationMiddleware.HasRequiredRoleForTesting([], "Admin").ShouldBeFalse();
        AuthorizationMiddleware.HasRequiredRoleForTesting([], "Operator").ShouldBeFalse();
    }

    [Fact]
    public void HasRequiredRole_RoleComparisonIsCaseInsensitive()
    {
        AuthorizationMiddleware.HasRequiredRoleForTesting(["operator"], "Operator").ShouldBeTrue();
        AuthorizationMiddleware.HasRequiredRoleForTesting(["admin"], "Admin").ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RouteRoleMatrix — verifies the route → required-role mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/schedules", "Operator")]
    [InlineData("GET", "/api/jobs", "Operator")]
    [InlineData("GET", "/api/jobs/abc123", "Operator")]
    [InlineData("POST", "/api/jobs/abc/approve", "Operator")]
    [InlineData("POST", "/api/jobs/abc/reject", "Operator")]
    [InlineData("POST", "/api/jobs/abc/publish", "Operator")]
    [InlineData("POST", "/api/jobs/abc/regenerate", "Operator")]
    [InlineData("POST", "/api/jobs/abc/pronunciation-rules", "Operator")]
    [InlineData("POST", "/api/jobs/abc/retone", "Operator")]
    [InlineData("POST", "/api/jobs/abc/rewrite", "Operator")]
    [InlineData("POST", "/api/jobs/abc/audition", "Operator")]
    [InlineData("POST", "/api/jobs/abc/direct", "Operator")]
    [InlineData("GET", "/api/map-config", "Operator")]
    [InlineData("PUT", "/api/pronunciation-dictionaries/pd-1", "Operator")]
    [InlineData("PUT", "/api/markets/mel/priority-roads", "Operator")]
    [InlineData("PUT", "/api/markets/mel/verified-pronunciations", "Operator")]
    [InlineData("PUT", "/api/markets/mel/filler-statements", "Operator")]
    [InlineData("PUT", "/api/markets/mel/incident-priorities", "Operator")]
    [InlineData("PUT", "/api/style-rules", "Admin")]
    [InlineData("PUT", "/api/safety/terms", "Admin")]
    [InlineData("POST", "/api/broadcast", "Operator")]
    [InlineData("PUT", "/api/schedules/mel", "Operator")]
    [InlineData("DELETE", "/api/schedules/mel", "Admin")]
    [InlineData("DELETE", "/api/jobs/abc", "Admin")]
    [InlineData("GET", "/api/activity", "Operator")]
    [InlineData("PUT", "/api/guidance", "Operator")]
    [InlineData("DELETE", "/api/guidance/abc", "Operator")]
    [InlineData("POST", "/api/guidance/promote", "Admin")]
    [InlineData("POST", "/api/voices/profiles/vp-1/voiceover", "Operator")]
    [InlineData("GET", "/api/voices/voiceovers/voiceover_vp1_20260812_101500_000.wav", "Operator")]
    [InlineData("POST", "/api/voices/profiles/preview", "Operator")]
    [InlineData("POST", "/api/voices/profiles/vp-1/archive", "Operator")]
    // The bookend library: every write is Operator by explicit decision, DELETE is not.
    [InlineData("GET", "/api/bookends", "Operator")]
    [InlineData("GET", "/api/bookends/bk-1", "Operator")]
    [InlineData("POST", "/api/bookends", "Operator")]
    [InlineData("PUT", "/api/bookends/bk-1", "Operator")]
    [InlineData("POST", "/api/bookends/preview", "Operator")]
    [InlineData("POST", "/api/bookends/bk-1/archive", "Operator")]
    [InlineData("POST", "/api/bookends/bk-1/unarchive", "Operator")]
    [InlineData("POST", "/api/bookends/bk-1/versions/2/restore", "Operator")]
    [InlineData("DELETE", "/api/bookends/bk-1", "Admin")]
    // The three shared libraries follow the bookend shape (2026-08-13, second wave): every write
    // is Operator, DELETE is not. Curating the shelf is operational work; deleting is the one
    // action with no undo.
    [InlineData("POST", "/api/voices/profiles", "Operator")]
    [InlineData("PUT", "/api/voices/profiles/vp-1", "Operator")]
    [InlineData("POST", "/api/voices/profiles/remix", "Operator")]
    [InlineData("POST", "/api/voices/vid-1/calibrate", "Operator")]
    [InlineData("DELETE", "/api/voices/profiles/vp-1", "Admin")]
    [InlineData("POST", "/api/pronunciation-dictionaries", "Operator")]
    [InlineData("POST", "/api/pronunciation-dictionaries/preview-rule", "Operator")]
    [InlineData("POST", "/api/pronunciation-dictionaries/pd-1/versions/2/restore", "Operator")]
    [InlineData("DELETE", "/api/pronunciation-dictionaries/pd-1", "Admin")]
    [InlineData("POST", "/api/delivery-styles", "Operator")]
    [InlineData("PUT", "/api/delivery-styles/ds-1", "Operator")]
    [InlineData("POST", "/api/delivery-styles/preview", "Operator")]
    [InlineData("DELETE", "/api/delivery-styles/ds-1", "Admin")]
    // NOT opened by that wave — the admin categories the owner kept: editorial/system prompt,
    // content safety, global guidance, the assignment register, bulk config transfer, raw synthesis.
    [InlineData("POST", "/api/synthesize", "Admin")]
    [InlineData("GET", "/api/config/export", "Admin")]
    [InlineData("GET", "/api/operators", "Admin")]
    [InlineData("PUT", "/api/operators/oid-1", "Admin")]
    [InlineData("DELETE", "/api/editorial/settings", "Admin")]
    public void RouteRoleMatrix_RoutesMapToExpectedRoles(string method, string path, string expected)
    {
        RouteRoleMatrix.RequiredRole(method, path).ShouldBe(expected);
    }

    [Fact]
    public void RouteRoleMatrix_EveryRegisteredHttpRoute_IsCoveredAndOperatorWritesAreScoped()
    {
        // Reflection over the ACTUAL [Function]/[HttpTrigger] surface, not a hand-copied list:
        // a list in a test can drift exactly the way the matrix did (finding 9).
        var triggers = RegisteredHttpRoutes();
        triggers.Count.ShouldBeGreaterThan(80);

        foreach (var (method, path) in triggers)
        {
            string role = RouteRoleMatrix.RequiredRole(method, path);
            role.ShouldBeOneOf(AuthorizationMiddleware.RoleAdmin, AuthorizationMiddleware.RoleOperator);

            // The inverse-error guard. An Operator-reachable NON-GET route must be RECOVERABLE
            // AND ATTRIBUTABLE — reached one of two ways: scoped to a market/job, or a library
            // mutation carrying the full compensating-control set. A future route that mutates
            // platform-wide config with neither property, and resolves to Operator, fails loudly.
            //
            // This used to demand market/job scoping alone. That was replaced (2026-08-13) once
            // scopability was shown to be a PROXY rather than the property being protected:
            // POST /jobs/{id}/pronunciation-rules already let an operator mint a new dictionary
            // version, so the identical mutation was Operator through a job and Admin through the
            // library — a difference of URL shape, not of consequence. The rule now names the
            // thing we actually require, so it still fails on a route that has neither.
            if (role == AuthorizationMiddleware.RoleOperator && method != "GET")
            {
                // (a) Market/job scoping — five route shapes: /markets/{m}/…,
                // /stations/{station}/… (resolved to its market by the handler),
                // /schedules/{marketId} (the station's own config), /jobs/{id}/… (scoped by the
                // job's market), and /api/guidance PUT/DELETE, where the scope rides in the
                // payload/query and the handler re-checks Admin for Global rows and applies
                // EnsureMarketAccessAsync to Market/Station rows. POST /api/broadcast joins them:
                // the route is flat, but the handler resolves a market and runs
                // EnsureMarketAccessAsync on it before any job is created.
                bool marketOrJobScoped =
                    path.Contains("/markets/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/stations/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/schedules/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/jobs/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/guidance", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/broadcast", StringComparison.OrdinalIgnoreCase)
                    // Ledger verdicts: /ledger/{market}/events/{key}/verdict — the market rides
                    // in the route and LedgerHttpTrigger runs EnsureMarketAccessAsync on it.
                    // Spot-check sign-off is job-scoped the same way approve is: the handler
                    // loads the job and market-guards against ITS market before writing.
                    || (path.Contains("/ledger/", StringComparison.OrdinalIgnoreCase)
                        && path.EndsWith("/verdict", StringComparison.OrdinalIgnoreCase))
                    || path.StartsWith("/api/spot-checks/", StringComparison.OrdinalIgnoreCase);

                // (b) A shared LIBRARY write. These have no market to scope against, and are
                // Operator-writable by explicit decision: bookends 2026-08-13, then voice
                // profiles / pronunciation dictionaries / delivery styles the same day. What
                // defends them is not a market guard but the control set every one of them
                // carries: an immutable version per write with author + change note,
                // ActivityLoggingMiddleware recording the mutation, restore-any-version,
                // Archive/Unarchive as the reversible retire path, and LibraryReferenceGuard
                // refusing to delete anything a station still points at. DELETE is excluded from
                // every one of those grants, so it never reaches this branch — which is what
                // keeps "recoverable" true.
                bool auditedLibraryWrite =
                    path.StartsWith("/api/bookends", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/voices", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/pronunciation-dictionaries", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/delivery-styles", StringComparison.OrdinalIgnoreCase);

                // The voiceover render is neither: it mutates nothing at all (no job, no
                // schedule, no shared config) and hands back one throwaway WAV bounded by the
                // per-request script ceiling. Named separately so it cannot be mistaken for a
                // library write that needs the control set.
                bool mutatesNothing = path.EndsWith("/voiceover", StringComparison.OrdinalIgnoreCase);

                (marketOrJobScoped || auditedLibraryWrite || mutatesNothing).ShouldBeTrue(
                    $"{method} {path} resolves to Operator but is neither market/job-scoped, nor an "
                    + "audited library write, nor free of side effects");
            }
        }
    }

    /// <summary>Enumerates the deployed HTTP trigger surface from the build-generated
    /// <c>functions.metadata</c> (copied to the test output by the Functions SDK). The compiled
    /// methods retain only the [Function] attribute — the [HttpTrigger] attribute is consumed by
    /// the source generator and re-emitted here as JSON bindings, so this file, not reflection,
    /// is the ACTUAL deployed surface.</summary>
    private static IReadOnlyList<(string Method, string Path)> RegisteredHttpRoutes() =>
        RegisteredRoutes.All();

    [Fact]
    public void RouteRoleMatrix_HealthSuffixPaths_NotBypassed()
    {
        // Only exact /api/health or /health paths should bypass auth at the middleware level.
        // Paths with health as a prefix segment should still be checked.
        RouteRoleMatrix.RequiredRole("GET", "/api/jobs/health-x/approve").ShouldBe("Operator");
        RouteRoleMatrix.RequiredRole("GET", "/api/health-x").ShouldBe("Operator");
        RouteRoleMatrix.RequiredRole("GET", "/api/healthcheck").ShouldBe("Operator");
    }

    [Theory]
    [InlineData("/api/GetRoles")]
    [InlineData("/api/getroles")]
    public async Task Invoke_GetRolesBypassesAuthBeforeRouteRoleLookup(string path)
    {
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        var request = new FakeHttpRequestData(context, new Uri($"http://localhost{path}"), "POST", Stream.Null);
        var features = new TestInvocationFeatures();
        var httpFeature = Substitute.For<IHttpRequestDataFeature>();
        httpFeature.GetHttpRequestDataAsync(Arg.Any<FunctionContext>())
            .Returns(new ValueTask<HttpRequestData?>(request));
        features.Set(httpFeature);
        context.Features.Returns(features);

        var middleware = new AuthorizationMiddleware(
            Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<AuthorizationMiddleware>>());

        bool nextCalled = false;
        FunctionExecutionDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await middleware.Invoke(context, next);

        nextCalled.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // End-to-end principal header encoding (the wire format Easy Auth emits)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EasyAuth_WireFormat_IsDecodable()
    {
        string json = """
            {
              "auth_typ": "aad",
              "name_typ": "name",
              "role_typ": "roles",
              "claims": [
                { "typ": "roles", "val": "Operator" },
                { "typ": "name", "val": "Local Dev" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);
        principal.ShouldNotBeNull();
        principal.Name.ShouldBe("Local Dev");
        principal.Roles.ShouldBe(["Operator"]);
        principal.IsAdmin.ShouldBeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Invoke — the boundary decision for a principal carrying ONLY Operator.
    // This is the case no deployed environment can demonstrate on dev, where
    // Auth__DevFallbackRole adds Admin to every parsed principal.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("POST", "/api/jobs/job-1/approve")]
    [InlineData("POST", "/api/jobs/job-1/publish")]
    [InlineData("POST", "/api/jobs/job-1/regenerate")]
    [InlineData("POST", "/api/jobs/job-1/retone")]
    [InlineData("POST", "/api/jobs/job-1/rewrite")]
    [InlineData("POST", "/api/jobs/job-1/audition")]
    [InlineData("POST", "/api/jobs/job-1/direct")]
    [InlineData("PUT", "/api/markets/sydney/priority-roads")]
    [InlineData("PUT", "/api/markets/sydney/verified-pronunciations")]
    [InlineData("POST", "/api/jobs/job-1/pronunciation-rules")]
    [InlineData("PUT", "/api/markets/sydney/filler-statements")]
    [InlineData("POST", "/api/bookends")]
    [InlineData("PUT", "/api/bookends/bk-1")]
    [InlineData("POST", "/api/bookends/preview")]
    [InlineData("POST", "/api/bookends/bk-1/archive")]
    [InlineData("POST", "/api/bookends/bk-1/versions/2/restore")]
    // The three shared libraries and the operator's own trigger, opened 2026-08-13 (second wave).
    [InlineData("POST", "/api/voices/profiles")]
    [InlineData("PUT", "/api/voices/profiles/vp-1")]
    [InlineData("POST", "/api/voices/profiles/vp-1/archive")]
    [InlineData("POST", "/api/voices/profiles/preview")]
    [InlineData("POST", "/api/pronunciation-dictionaries")]
    [InlineData("PUT", "/api/pronunciation-dictionaries/pd-1")]
    [InlineData("POST", "/api/pronunciation-dictionaries/pd-1/versions/2/restore")]
    [InlineData("POST", "/api/delivery-styles")]
    [InlineData("PUT", "/api/delivery-styles/ds-1")]
    [InlineData("POST", "/api/broadcast")]
    [InlineData("GET", "/api/activity")]
    public async Task Invoke_OperatorOnlyPrincipal_ReachesTheOperatorLoop(string method, string path)
    {
        // Reaching `next` IS the pass: the middleware short-circuits with a 403 response
        // instead of invoking the trigger whenever the caller lacks the route's role.
        (bool nextCalled, _) = await InvokeAsOperatorAsync(method, path);

        nextCalled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("PUT", "/api/style-rules")]
    [InlineData("PUT", "/api/safety/terms")]
    [InlineData("DELETE", "/api/jobs/job-1")]
    // Deleting any library item stays Admin: the one action with no undo.
    [InlineData("DELETE", "/api/voices/profiles/vp-1")]
    [InlineData("DELETE", "/api/pronunciation-dictionaries/pd-1")]
    [InlineData("DELETE", "/api/delivery-styles/ds-1")]
    [InlineData("POST", "/api/synthesize")]
    [InlineData("GET", "/api/config/export")]
    [InlineData("GET", "/api/operators")]
    [InlineData("DELETE", "/api/bookends/bk-1")]
    public async Task Invoke_OperatorOnlyPrincipal_ForbiddenOnAdminSurface(string method, string path)
    {
        (bool nextCalled, var context) = await InvokeAsOperatorAsync(method, path);

        nextCalled.ShouldBeFalse();
        context.GetInvocationResult().Value.ShouldBeOfType<FakeHttpResponseData>()
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    ///     Drives the real middleware with a SWA-shaped principal header carrying Operator and
    ///     nothing else. The dev fallback is cleared for the call so the decision under test is
    ///     the route matrix, never the bypass.
    /// </summary>
    private static async Task<(bool NextCalled, FunctionContext Context)> InvokeAsOperatorAsync(
        string method, string path)
    {
        string? savedFallback = Environment.GetEnvironmentVariable(ClientPrincipal.DevFallbackRoleSetting);
        Environment.SetEnvironmentVariable(ClientPrincipal.DevFallbackRoleSetting, null);
        try
        {
            var context = Substitute.For<FunctionContext>();
            context.CancellationToken.Returns(CancellationToken.None);

            var request = new FakeHttpRequestData(
                context, new Uri($"http://localhost{path}"), method, Stream.Null);
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(
                """
                {
                  "identityProvider": "aad",
                  "userId": "op-object-id",
                  "userDetails": "op@arn.com.au",
                  "userRoles": ["anonymous", "authenticated", "Operator"]
                }
                """));

            var features = new ArnBulletinGenerator.Functions.Tests.TestInvocationFeatures();
            var httpFeature = Substitute.For<IHttpRequestDataFeature>();
            httpFeature.GetHttpRequestDataAsync(Arg.Any<FunctionContext>())
                .Returns(new ValueTask<HttpRequestData?>(request));
            features.Set(httpFeature);

            var invocationResult = Substitute.For<InvocationResult>();
            var bindingsType = typeof(InvocationResult).Assembly.GetType(
                "Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature")!;
            object bindingsSub = Substitute.For([bindingsType], [])!;
            bindingsType.GetProperty("InvocationResult")!.SetValue(bindingsSub, invocationResult);
            features.Register(bindingsType, bindingsSub);

            context.Features.Returns(features);
            context.GetInvocationResult().Returns(invocationResult);

            var configuration = Substitute.For<IConfiguration>();
            configuration["AadClientId"].Returns("11111111-1111-1111-1111-111111111111");

            var middleware = new AuthorizationMiddleware(
                configuration, Substitute.For<ILogger<AuthorizationMiddleware>>());

            bool nextCalled = false;
            await middleware.Invoke(context, _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            return (nextCalled, context);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClientPrincipal.DevFallbackRoleSetting, savedFallback);
        }
    }

    private static string Encode(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}
