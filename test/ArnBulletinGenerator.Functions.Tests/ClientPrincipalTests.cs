using System.Text;
using Shouldly;
using ArnBulletinGenerator.Functions;
using ArnBulletinGenerator.Functions.Auth;

namespace ArnBulletinGenerator.Functions.Tests;

public class ClientPrincipalTests
{
    private static string Encode(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parse_ValidBlobWithOidNameRoles_ParsesAllFields()
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

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.ObjectId.ShouldBe("abc123");
        principal.Name.ShouldBe("John Doe");
        principal.Roles.ShouldBe(["Operator", "Admin"], ignoreOrder: true);
        principal.IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public void Parse_AdminRoleCaseInsensitive_IsAdmin()
    {
        string json = """
            {
              "auth_typ": "aad",
              "claims": [
                { "typ": "oid", "val": "1" },
                { "typ": "name", "val": "Admin User" },
                { "typ": "roles", "val": "admin" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public void Parse_NoAdminRole_IsAdminFalse()
    {
        string json = """
            {
              "auth_typ": "aad",
              "claims": [
                { "typ": "oid", "val": "1" },
                { "typ": "name", "val": "Operator User" },
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public void Parse_LongUriOidClaim_Recognized()
    {
        string json = """
            {
              "auth_typ": "aad",
              "claims": [
                { "typ": "http://schemas.microsoft.com/identity/claims/objectidentifier", "val": "xyz789" },
                { "typ": "name", "val": "Jane Smith" },
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.ObjectId.ShouldBe("xyz789");
        principal.Name.ShouldBe("Jane Smith");
    }

    [Fact]
    public void Parse_NameTypIndirection_UsesConfiguredNameType()
    {
        // name_typ overrides which claim typ supplies the Name field
        string json = """
            {
              "auth_typ": "aad",
              "name_typ": "displayName",
              "claims": [
                { "typ": "oid", "val": "oid1" },
                { "typ": "displayName", "val": "Display Name" },
                { "typ": "name", "val": "Ignored Name" },
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.Name.ShouldBe("Display Name");
    }

    [Fact]
    public void Parse_MissingHeader_ReturnsNull()
    {
        var request = MockHttpRequestData.CreateGet();

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldBeNull();
    }

    [Fact]
    public void Parse_InvalidBase64_ReturnsNull()
    {
        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", "!!! not-base64 !!!");

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldBeNull();
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode("{ not valid json }"));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldBeNull();
    }

    [Fact]
    public void Parse_EmptyHeaderValue_ReturnsNull()
    {
        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", "");

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldBeNull();
    }
    // ─────────────────────────────────────────────────────────────────────────
    // SWA client principal — the format the linked backend forwards to /api/*
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SwaClientPrincipal_ParsesRolesNameAndOid()
    {
        // SWA uses identityProvider (not auth_typ), userRoles, userDetails, userId.
        string json = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "userDetails": "operator@arn.com.au",
              "userRoles": ["anonymous", "authenticated", "Operator"],
              "claims": [
                { "typ": "http://schemas.microsoft.com/identity/claims/objectidentifier", "val": "oid-789" }
              ]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.ObjectId.ShouldBe("oid-789");            // oid claim wins over userId
        principal.Name.ShouldBe("operator@arn.com.au");
        principal.Roles.ShouldBe(["Operator"]);            // anonymous/authenticated excluded
        principal.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public void Parse_SwaPrincipalWithoutOidClaim_FallsBackToUserId()
    {
        string json = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "userDetails": "op@arn.com.au",
              "userRoles": ["authenticated"]
            }
            """;

        var request = MockHttpRequestData.CreateGet();
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Encode(json));

        var principal = ClientPrincipal.Parse(request);

        principal.ShouldNotBeNull();
        principal.ObjectId.ShouldBe("swa-user-123");
        principal.Name.ShouldBe("op@arn.com.au");
        principal.Roles.ShouldBeEmpty();                   // only pseudo-roles → no app roles
    }

    [Fact]
    public void TryFromJson_NonAadIdentityProvider_Rejected()
    {
        string json = """{ "identityProvider": "github", "userRoles": ["authenticated"] }""";

        bool ok = ClientPrincipal.TryFromJson(json, devFallbackRole: null, out var principal, out var reason);

        ok.ShouldBeFalse();
        principal.ShouldBeNull();
        reason.ShouldBe("principal is not an AAD identity");
    }

    [Fact]
    public void TryFromJson_DevFallbackRole_GrantedToAuthenticatedCaller()
    {
        // Mirrors the deployed dev environment: SWA built-in provider yields no app roles,
        // Auth__DevFallbackRole grants Admin so the console is usable.
        string json = """{ "identityProvider": "aad", "userRoles": ["authenticated"] }""";

        bool ok = ClientPrincipal.TryFromJson(json, devFallbackRole: "Admin", out var principal, out _);

        ok.ShouldBeTrue();
        principal.ShouldNotBeNull();
        principal.Roles.ShouldBe(["Admin"]);
        principal.IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public void TryFromJson_DevFallbackRole_NotDuplicatedWhenAlreadyPresent()
    {
        string json = """{ "auth_typ": "aad", "claims": [ { "typ": "roles", "val": "Admin" } ] }""";

        bool ok = ClientPrincipal.TryFromJson(json, devFallbackRole: "admin", out var principal, out _);

        ok.ShouldBeTrue();
        principal.ShouldNotBeNull();
        principal.Roles.Count.ShouldBe(1);                 // case-insensitive de-dupe
    }
}
