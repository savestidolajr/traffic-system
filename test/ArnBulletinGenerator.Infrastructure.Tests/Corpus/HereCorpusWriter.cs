using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Thread-safe sink for recorded HERE API exchanges. Classifies each exchange by
///     endpoint, redacts the API key, writes one JSON file per exchange under
///     <c>&lt;market&gt;/&lt;endpoint&gt;/&lt;sessionId&gt;-&lt;seq&gt;.json</c>, and maintains a
///     top-level manifest. Re-runs accumulate (existing files and manifest entries are preserved).
/// </summary>
internal sealed partial class HereCorpusWriter(string corpusRoot)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<(string Market, string Endpoint), int> _sequence = new();
    private readonly List<ManifestEntry> _entries = [];

    /// <summary>Market the in-flight exchanges belong to. Set before each market's capture pass.</summary>
    public string CurrentMarket { get; set; } = "unknown";

    /// <summary>
    ///     Identifier tying together one coherent cascade (incidents + its revgeocode/routing follow-ups).
    ///     Set before each capture pass.
    /// </summary>
    public string CurrentSessionId { get; set; } = "session";

    public int TotalRecorded { get; private set; }

    [GeneratedRegex(@"apiKey=[^&]+")]
    private static partial Regex ApiKeyPattern();

    public async Task RecordAsync(
        string method,
        Uri? uri,
        string? requestBody,
        int status,
        string? mediaType,
        byte[] responseBytes,
        CancellationToken cancellationToken)
    {
        string endpoint = Classify(uri);
        string market = CurrentMarket;
        string sessionId = CurrentSessionId;
        string sanitizedUri = uri is null
            ? string.Empty
            : ApiKeyPattern().Replace(uri.ToString(), "apiKey=***");

        int seq;
        lock (_gate)
        {
            var key = (market, endpoint);
            seq = _sequence.TryGetValue(key, out int current) ? current + 1 : 1;
            _sequence[key] = seq;
        }

        JsonElement? parsedBody = TryParseJson(responseBytes);
        var record = new CorpusRecord(
            market,
            endpoint,
            sessionId,
            DateTimeOffset.UtcNow,
            new CorpusRequest(method, sanitizedUri, requestBody),
            new CorpusResponse(
                status,
                mediaType,
                parsedBody,
                parsedBody is null ? Encoding.UTF8.GetString(responseBytes) : null));

        string directory = Path.Combine(corpusRoot, market, endpoint);
        Directory.CreateDirectory(directory);
        string fileName = $"{sessionId}-{seq:D3}.json";
        string fullPath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(record, WriteOptions), cancellationToken);

        lock (_gate)
        {
            _entries.Add(new ManifestEntry(
                market,
                endpoint,
                sessionId,
                record.CapturedAtUtc,
                method,
                sanitizedUri,
                status,
                Path.GetRelativePath(corpusRoot, fullPath).Replace('\\', '/')));
            TotalRecorded++;
        }
    }

    /// <summary>Merges this run's entries into <c>manifest.json</c>, preserving prior captures.</summary>
    public async Task FlushManifestAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(corpusRoot);
        string manifestPath = Path.Combine(corpusRoot, "manifest.json");

        var all = new List<ManifestEntry>();
        if (File.Exists(manifestPath))
            try
            {
                string existingJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var existing = JsonSerializer.Deserialize<List<ManifestEntry>>(existingJson);
                if (existing is not null)
                    all.AddRange(existing);
            }
            catch (JsonException)
            {
                // Corrupt or schema-changed manifest: rebuild from this run only.
            }

        lock (_gate)
        {
            all.AddRange(_entries);
        }

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(all, WriteOptions), cancellationToken);
    }

    private static string Classify(Uri? uri)
    {
        if (uri is null)
            return "other";

        string path = uri.AbsolutePath.ToLowerInvariant();
        string host = uri.Host.ToLowerInvariant();

        // Order matters: "multi-revgeocode" and "revgeocode" both contain "geocode".
        if (path.Contains("multi-revgeocode")) return "batch-revgeocode";
        if (path.Contains("revgeocode")) return "revgeocode";
        if (path.Contains("geocode")) return "geocode";
        if (path.Contains("/incidents") || host.Contains("traffic")) return "traffic-incidents";
        if (path.Contains("/routes") || host.Contains("router")) return "routing";
        return "other";
    }

    private static JsonElement? TryParseJson(byte[] bytes)
    {
        if (bytes.Length == 0)
            return null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
