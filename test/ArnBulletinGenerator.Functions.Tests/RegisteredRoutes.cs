using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     The ACTUAL deployed HTTP surface, read from <c>functions.metadata</c> (copied to the test
///     output by the Functions SDK). The compiled methods retain only the [Function] attribute —
///     the [HttpTrigger] attribute is consumed by the source generator and re-emitted there as
///     JSON bindings, so that file, not reflection, is the real surface.
///     <para>
///         Shared by the two whole-surface invariants that must never disagree about what routes
///         exist: authorization coverage (<see cref="AuthorizationMiddlewareTests" />) and audit
///         coverage (<see cref="ActivityPathResolverTests" />). A hand-copied list in either would
///         drift exactly the way the role matrix once did.
///     </para>
/// </summary>
internal static class RegisteredRoutes
{
    internal static IReadOnlyList<(string Method, string Path)> All()
    {
        var routes = new List<(string, string)>();
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "functions.metadata");
        using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
        foreach (var function in doc.RootElement.EnumerateArray())
        {
            if (!function.TryGetProperty("bindings", out var bindings))
                continue;
            foreach (var binding in bindings.EnumerateArray())
            {
                if (!binding.TryGetProperty("type", out var type) ||
                    !string.Equals(type.GetString(), "httpTrigger", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? route = binding.TryGetProperty("route", out var routeProp)
                    ? routeProp.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(route))
                    continue;

                IEnumerable<string> methods = binding.TryGetProperty("methods", out var methodsProp)
                    ? methodsProp.EnumerateArray().Select(m => m.GetString() ?? string.Empty).Where(m => m.Length > 0)
                    : [];
                foreach (var httpMethod in methods)
                {
                    string concrete = route.ToLowerInvariant();
                    concrete = Regex.Replace(concrete, @"\{[a-z_]+\?\}", "");
                    concrete = Regex.Replace(concrete, @"\{[a-z_]+(?::[a-z]+)?\}", "abc");
                    string path = "/api/" + concrete.Trim('/');
                    routes.Add((httpMethod.ToUpperInvariant(), path));
                }
            }
        }

        return routes;
    }
}
