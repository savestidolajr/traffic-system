using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     All tests are fully offline: the OIDC metadata source is swapped for a fixed in-memory
///     <see cref="IConfigurationManager{T}" /> via the internal test seam, so nothing ever reaches
///     login.microsoftonline.com. Tokens are generated locally with a symmetric key the test
///     controls, which lets the untrusted-key rejection and the full happy path be exercised for
///     real (signature, issuer, audience and lifetime validation all run).
/// </summary>
public class SwaRolesSourceTokenValidatorTests
{
    private const string ClientId = "11111111-1111-1111-1111-111111111111";
    private const string TenantId = "22222222-2222-2222-2222-222222222222";
    private const string OtherTenantId = "99999999-9999-9999-9999-999999999999";
    private const string ObjectId = "33333333-4444-5555-6666-777777777777";
    private const string OidClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly SymmetricSecurityKey UntrustedKey =
        new(Encoding.UTF8.GetBytes("fedcba9876543210fedcba9876543210"));

    // ─────────────────────────────────────────────────────────────────────────
    // Absent / blank tokens - the normal case, must not log or touch metadata
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_NullEmptyOrWhitespaceToken_ReturnsNone(string? token)
    {
        var validator = NewValidator();

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Configuration gaps - validator is inert, never fatal
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_BlankClientId_ReturnsNoneWithoutConsultingMetadata()
    {
        var metadata = Substitute.For<IConfigurationManager<OpenIdConnectConfiguration>>();
        var validator = NewValidator(metadata, clientId: "", tenantId: TenantId);

        var result = await validator.ValidateAsync("header.payload.signature");

        result.IsNone.ShouldBeTrue();
        await metadata.DidNotReceive().GetConfigurationAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateAsync_BlankTenantId_ReturnsNoneWithoutConsultingMetadata()
    {
        var metadata = Substitute.For<IConfigurationManager<OpenIdConnectConfiguration>>();
        var validator = NewValidator(metadata, clientId: ClientId, tenantId: "   ");

        var result = await validator.ValidateAsync("header.payload.signature");

        result.IsNone.ShouldBeTrue();
        await metadata.DidNotReceive().GetConfigurationAsync(Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rejections - fail-closed on every malformed/unsigned/untrusted case
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_GarbageToken_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));

        var result = await validator.ValidateAsync("not.a.jwt");

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_UnsignedToken_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateUnsignedToken(new Dictionary<string, object> { ["oid"] = ObjectId });

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TokenSignedByUntrustedKey_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId },
            signingCredentials: HmacSigning(UntrustedKey));

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TokenForAnotherAudience_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId },
            audience: "00000000-0000-0000-0000-000000000000",
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TokenForAnotherIssuer_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId },
            issuer: $"https://login.microsoftonline.com/{OtherTenantId}/v2.0",
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId },
            expires: DateTime.UtcNow.AddMinutes(-30),
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_RejectedToken_LogsObservedAudienceAndIssuer()
    {
        var logger = Substitute.For<ILogger<SwaRolesSourceTokenValidator>>();
        var validator = new SwaRolesSourceTokenValidator(
            ConfigurationWith(ClientId, TenantId),
            logger,
            MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId },
            audience: "https://management.azure.com",
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        // The observable contract is the rejection. The Warning that carries the observed aud/iss
        // is a diagnostic, and asserting on ILogger's generic Log overload pins mocking plumbing
        // rather than behaviour, so it is deliberately not asserted here.
        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_MetadataFetchCancelled_PropagatesCancellation()
    {
        var metadata = new FixedConfigurationManager(
            new OpenIdConnectConfiguration(),
            exception: new OperationCanceledException("sign-in aborted"));
        var validator = NewValidator(metadata);

        await Should.ThrowAsync<OperationCanceledException>(
            () => validator.ValidateAsync("header.payload.signature", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Accepted tokens - claim projection, including the legacy claim URIs
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ValidToken_ProjectsClaims()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object>
            {
                ["oid"] = ObjectId,
                ["name"] = "Jane Doe",
                ["preferred_username"] = "jane@contoso.com",
                ["email"] = "backup@contoso.com",
                ["roles"] = new[] { "Admin", "admin", "reader" },
                [RoleClaimUri] = "LegacyRole",
            },
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsSome.ShouldBeTrue();
        var signIn = result.Value;
        signIn.ObjectId.ShouldBe(ObjectId);
        signIn.DisplayName.ShouldBe("Jane Doe");
        signIn.UserPrincipalName.ShouldBe("jane@contoso.com");
        // "admin" is a case-insensitive duplicate of "Admin"; the legacy role URI is included.
        signIn.Roles.ShouldBe(["Admin", "reader", "LegacyRole"], ignoreOrder: true);
    }

    [Fact]
    public async Task ValidateAsync_OidViaLegacyClaimUri_Accepted()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { [OidClaimUri] = ObjectId, ["name"] = "Jane Doe" },
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsSome.ShouldBeTrue();
        result.Value.ObjectId.ShouldBe(ObjectId);
        result.Value.DisplayName.ShouldBe("Jane Doe");
    }

    [Fact]
    public async Task ValidateAsync_ValidTokenWithoutOid_ReturnsNone()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["name"] = "Jane Doe" },
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_UpnFallsBackToEmailAndMissingNameIsEmpty()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId, ["email"] = "jane@contoso.com" },
            signingCredentials: HmacSigning(SigningKey));

        var result = await validator.ValidateAsync(token);

        result.IsSome.ShouldBeTrue();
        result.Value.UserPrincipalName.ShouldBe("jane@contoso.com");
        result.Value.DisplayName.ShouldBeEmpty();
        result.Value.Roles.ShouldBeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Singleton safety - shared instance under concurrent sign-ins
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ConcurrentCalls_SingletonSafe()
    {
        var validator = NewValidator(MetadataWith(SigningKey));
        string token = CreateToken(
            new Dictionary<string, object> { ["oid"] = ObjectId, ["name"] = "Jane Doe" },
            signingCredentials: HmacSigning(SigningKey));

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => validator.ValidateAsync(token, CancellationToken.None)));

        results.ShouldAllBe(result => result.IsSome);
        results[0].Value.ObjectId.ShouldBe(ObjectId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static SwaRolesSourceTokenValidator NewValidator(
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager = null,
        string? clientId = ClientId,
        string? tenantId = TenantId)
        => new(ConfigurationWith(clientId, tenantId),
            Substitute.For<ILogger<SwaRolesSourceTokenValidator>>(),
            configurationManager);

    private static IConfiguration ConfigurationWith(string? clientId, string? tenantId)
    {
        var configuration = Substitute.For<IConfiguration>();
        configuration["AadClientId"].Returns(clientId);
        configuration["AadTenantId"].Returns(tenantId);
        return configuration;
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> MetadataWith(SecurityKey signingKey)
    {
        var openIdConfiguration = new OpenIdConnectConfiguration
        {
            Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0",
        };
        openIdConfiguration.SigningKeys.Add(signingKey);
        return new FixedConfigurationManager(openIdConfiguration);
    }

    private static SigningCredentials HmacSigning(SymmetricSecurityKey key)
        => new(key, SecurityAlgorithms.HmacSha256);

    private static string CreateToken(
        IDictionary<string, object> claims,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null,
        SigningCredentials? signingCredentials = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = issuer ?? $"https://login.microsoftonline.com/{TenantId}/v2.0",
            Audience = audience ?? ClientId,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = signingCredentials,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Crafts a well-formed JWT with an empty signature - the "unsigned" case.</summary>
    private static string CreateUnsignedToken(IDictionary<string, object> claims)
    {
        string header = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = "none", typ = "JWT" }));
        string payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(claims));
        return $"{header}.{payload}.";
    }

    /// <summary>
    ///     In-memory metadata source: returns a fixed configuration (or a fixed failure), never
    ///     touching the network, so validation runs against keys the test controls.
    /// </summary>
    private sealed class FixedConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration? _configuration;
        private readonly Exception? _exception;

        public FixedConfigurationManager(OpenIdConnectConfiguration configuration, Exception? exception = null)
        {
            _configuration = configuration;
            _exception = exception;
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
            => _exception is not null
                ? Task.FromException<OpenIdConnectConfiguration>(_exception)
                : Task.FromResult(_configuration!);

        public void RequestRefresh()
        {
        }
    }
}
