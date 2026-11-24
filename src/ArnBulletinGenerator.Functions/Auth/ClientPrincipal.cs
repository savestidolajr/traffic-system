using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using ArnBulletinGenerator.Functions.Middleware;

namespace ArnBulletinGenerator.Functions.Auth;

/// <summary>
///     Parsed caller identity from the <c>X-MS-CLIENT-PRINCIPAL</c> blob: AAD object id,
///     display name, and app roles. The middleware has already authenticated the request;
///     this is for downstream decisions (market scoping, audit) that need more than a name.
///
///     Two wire formats are accepted because the deployed topology can inject either:
///
///     <list type="bullet">
///       <item>
///         <b>App Service Easy Auth</b> — the Function App's own AAD provider (direct
///         bearer-token calls) and the local-dev mock both emit:
///         <c>{ "auth_typ": "aad", "name_typ": "...", "claims": [ { "typ", "val" }, … ] }</c>.
///         Roles come from claims with <c>typ == "roles"</c> (or the legacy role-claim URI).
///       </item>
///       <item>
///         <b>Static Web Apps client principal</b> — forwarded by the SWA <i>linked backend</i>
///         when it proxies <c>/api/*</c> to this Function App:
///         <c>{ "identityProvider": "aad", "userId", "userDetails", "userRoles": […], "claims": […] }</c>.
///         Roles come from <c>userRoles</c> (minus the <c>anonymous</c>/<c>authenticated</c>
///         pseudo-roles SWA injects) plus any <c>claims</c> with <c>typ == "roles"</c>.
///       </item>
///     </list>
/// </summary>
/// <param name="HasDirectoryObjectId">
///     True only when <see cref="ObjectId" /> came from the AAD <c>oid</c> claim, i.e. it is a real
///     Entra directory object id that joins to <c>appRoleAssignedTo</c>. False when it was back-filled
///     from the SWA <c>userId</c> (a provider-specific id) or synthesised for the dev bypass — those
///     identify a caller but must never be registered as directory people.
/// </param>
public sealed record ClientPrincipal(
    string ObjectId,
    string Name,
    IReadOnlyList<string> Roles,
    bool HasDirectoryObjectId = false,
    string? UserPrincipalName = null)
{
    public const string HeaderName = "X-MS-CLIENT-PRINCIPAL";

    /// <summary>
    ///     App setting (env var) naming an app role to grant every authenticated AAD caller.
    ///     A dev-only authorization bypass so the operator console is usable before Entra
    ///     app-role assignments exist. MUST be empty in test/prod (see deployment bicepparams).
    /// </summary>
    public const string DevFallbackRoleSetting = "Auth__DevFallbackRole";

    private const string OidClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private static readonly string[] UpnClaimTypes =
    [
        "preferred_username",
        "upn",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn",
        "email",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"
    ];

    // SWA stamps these on every authenticated principal; they are membership markers, not app roles.
    private static readonly HashSet<string> SwaPseudoRoles =
        new(StringComparer.OrdinalIgnoreCase) { "anonymous", "authenticated" };

    public bool IsAdmin => Roles.Contains(AuthorizationMiddleware.RoleAdmin, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Parses the principal blob. Returns null when the header is absent, unparseable, or
    ///     not an AAD identity — callers treat that as an unscoped (but already role-authorized)
    ///     caller. Applies the dev authorization fallback (<see cref="DevFallbackRoleSetting"/>).
    /// </summary>
    public static ClientPrincipal? Parse(HttpRequestData req)
        => TryReadHeaderJson(req, out string? json) && json is not null
           && TryFromJson(json, ResolveDevFallbackRole(), out var principal, out _)
            ? principal
            : null;

    /// <summary>
    ///     Decodes the base64 <c>X-MS-CLIENT-PRINCIPAL</c> header to its raw JSON. Returns false
    ///     when the header is missing, empty, or not valid base64.
    /// </summary>
    internal static bool TryReadHeaderJson(HttpRequestData req, out string? json)
    {
        json = null;
        if (!req.Headers.TryGetValues(HeaderName, out var values))
            return false;

        string? encoded = values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Core dual-format parser. Returns false (with a reason for logging) on malformed JSON
    ///     or a non-AAD identity; true with a populated principal otherwise. When
    ///     <paramref name="devFallbackRole"/> is non-empty, that role is added to every parsed
    ///     principal — a dev-only authorization bypass.
    /// </summary>
    internal static bool TryFromJson(
        string principalJson,
        string? devFallbackRole,
        out ClientPrincipal? principal,
        out string? rejectionReason)
    {
        principal = null;
        rejectionReason = null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(principalJson);
        }
        catch (JsonException)
        {
            rejectionReason = "principal header is not valid JSON";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectionReason = "principal header is not a JSON object";
                return false;
            }

            // Easy Auth uses auth_typ; the SWA client principal uses identityProvider.
            if (!IsAad(root, "auth_typ") && !IsAad(root, "identityProvider"))
            {
                rejectionReason = "principal is not an AAD identity";
                return false;
            }

            string nameType = root.TryGetProperty("name_typ", out var nt) && nt.ValueKind == JsonValueKind.String
                ? nt.GetString()!
                : "name";

            string objectId = "";
            string name = "";
            string userPrincipalName = "";
            var roles = new List<string>();
            bool hasDirectoryObjectId = false;

            if (root.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
            {
                foreach (var claim in claims.EnumerateArray())
                {
                    if (!claim.TryGetProperty("typ", out var typEl) || typEl.ValueKind != JsonValueKind.String ||
                        !claim.TryGetProperty("val", out var valEl) || valEl.ValueKind != JsonValueKind.String)
                        continue;

                    string typ = typEl.GetString()!;
                    string val = valEl.GetString()!;
                    if (val.Length == 0)
                        continue;

                    if (typ.Equals("oid", StringComparison.OrdinalIgnoreCase) ||
                        typ.Equals(OidClaimUri, StringComparison.OrdinalIgnoreCase))
                    {
                        objectId = val;
                        hasDirectoryObjectId = true;
                    }
                    else if (typ.Equals(nameType, StringComparison.OrdinalIgnoreCase))
                        name = val;
                    else if (typ is "roles" || typ.Equals(RoleClaimUri, StringComparison.OrdinalIgnoreCase))
                        AddRole(roles, val);
                    else if (userPrincipalName.Length == 0 &&
                             UpnClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
                        userPrincipalName = val;
                }
            }

            // SWA top-level fields. userRoles carries app roles (when SWA is configured against
            // an app registration that defines them); userId/userDetails back-fill oid/name.
            if (root.TryGetProperty("userRoles", out var userRoles) && userRoles.ValueKind == JsonValueKind.Array)
            {
                foreach (var roleEl in userRoles.EnumerateArray())
                {
                    if (roleEl.ValueKind != JsonValueKind.String)
                        continue;
                    string val = roleEl.GetString()!;
                    if (val.Length == 0 || SwaPseudoRoles.Contains(val))
                        continue;
                    AddRole(roles, val);
                }
            }

            if (objectId.Length == 0 &&
                root.TryGetProperty("userId", out var uid) && uid.ValueKind == JsonValueKind.String)
                objectId = uid.GetString() ?? "";

            if (name.Length == 0 &&
                root.TryGetProperty("userDetails", out var ud) && ud.ValueKind == JsonValueKind.String)
                name = ud.GetString() ?? "";

            // userDetails is the sign-in name for both the SWA and Easy Auth shapes, so it is the
            // right last resort for a UPN — but only that: never invent one from a display name.
            if (userPrincipalName.Length == 0 &&
                root.TryGetProperty("userDetails", out var udUpn) && udUpn.ValueKind == JsonValueKind.String)
                userPrincipalName = udUpn.GetString() ?? "";

            if (!string.IsNullOrWhiteSpace(devFallbackRole))
                AddRole(roles, devFallbackRole.Trim());

            principal = new ClientPrincipal(
                objectId,
                name,
                roles,
                hasDirectoryObjectId,
                userPrincipalName.Length == 0 ? null : userPrincipalName);
            return true;
        }
    }

    /// <summary>Reads the configured dev authorization fallback role from the app settings.</summary>
    internal static string? ResolveDevFallbackRole()
        => Environment.GetEnvironmentVariable(DevFallbackRoleSetting);

    private static void AddRole(List<string> roles, string role)
    {
        if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            roles.Add(role);
    }

    private static bool IsAad(JsonElement root, string property)
        => root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
           && string.Equals(el.GetString(), "aad", StringComparison.OrdinalIgnoreCase);
}
