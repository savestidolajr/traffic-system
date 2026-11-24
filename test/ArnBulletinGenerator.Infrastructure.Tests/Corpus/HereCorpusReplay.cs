using System.Text.Json;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     In-memory index over the recorded HERE corpus. Resolves recorded responses by a normalized
///     request signature — method + path + query, excluding the volatile <c>apiKey</c> and
///     <c>departureTime</c> parameters — enabling deterministic, offline, quota-free replay of the
///     production providers against real recorded data.
/// </summary>
internal sealed class HereCorpusReplay
{
    private readonly Dictionary<string, ReplayEntry> _byKey = new(StringComparer.Ordinal);

    private HereCorpusReplay()
    {
    }

    /// <summary>Number of distinct recorded exchanges indexed.</summary>
    public int Count => _byKey.Count;

    /// <summary>Recorded exchanges served so far.</summary>
    public int Hits { get; private set; }

    /// <summary>Requests with no matching recorded exchange.</summary>
    public int Misses { get; private set; }

    /// <summary>
    ///     Loads the corpus for a single market (or all markets when <paramref name="market" /> is null).
    ///     Files are indexed in filename order so duplicate signatures resolve to the most recent session
    ///     (filenames are timestamp-prefixed).
    /// </summary>
    public static HereCorpusReplay Load(string? market = null, string? corpusRoot = null)
    {
        corpusRoot ??= CorpusPaths.HereCorpusRoot();
        var replay = new HereCorpusReplay();
        if (!Directory.Exists(corpusRoot))
            return replay;

        IEnumerable<string> marketDirs = market is null
            ? Directory.EnumerateDirectories(corpusRoot)
            : [Path.Combine(corpusRoot, market)];

        var files = marketDirs
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            .OrderBy(file => file, StringComparer.Ordinal);

        foreach (string file in files)
        {
            CorpusRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<CorpusRecord>(File.ReadAllText(file));
            }
            catch (JsonException)
            {
                continue;
            }

            if (record is null || !Uri.TryCreate(record.Request.Uri, UriKind.Absolute, out var uri))
                continue;

            string key = Signature(record.Request.Method, uri);
            string body = record.Response.Body is { } element
                ? element.GetRawText()
                : record.Response.RawBody ?? string.Empty;

            replay._byKey[key] = new ReplayEntry(record.Response.Status, record.Response.MediaType ?? "application/json", body);
        }

        return replay;
    }

    /// <summary>Resolves the recorded response for an outgoing request, if one was captured.</summary>
    public bool TryResolve(HttpRequestMessage request, out ReplayEntry entry)
    {
        if (request.RequestUri is not null &&
            _byKey.TryGetValue(Signature(request.Method.Method, request.RequestUri), out entry))
        {
            Hits++;
            return true;
        }

        Misses++;
        entry = default;
        return false;
    }

    /// <summary>
    ///     Normalizes a request to a stable key: <c>METHOD path?sortedQuery</c>, with <c>apiKey</c> and
    ///     <c>departureTime</c> removed. Host and scheme are ignored so replay is independent of base-URL config.
    /// </summary>
    internal static string Signature(string method, Uri uri)
    {
        var kept = new List<string>();
        string query = uri.Query.TrimStart('?');
        if (query.Length > 0)
            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                string name = equals < 0 ? part : part[..equals];
                // `apiKey` is a secret, `departureTime` is wall-clock, and `lang` pins HERE's English
                // register without changing which records come back — none may take part in the key, or
                // every recorded fixture misses the moment one of them changes.
                if (name.Equals("apiKey", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("departureTime", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("lang", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.Equals("locationReferencing", StringComparison.OrdinalIgnoreCase) &&
                    part.Contains("segmentRef", StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add("locationReferencing=shape");
                    continue;
                }
                kept.Add(part);
            }

        kept.Sort(StringComparer.Ordinal);
        return $"{method.ToUpperInvariant()} {uri.AbsolutePath}?{string.Join('&', kept)}";
    }

    internal readonly record struct ReplayEntry(int Status, string MediaType, string Body);
}
