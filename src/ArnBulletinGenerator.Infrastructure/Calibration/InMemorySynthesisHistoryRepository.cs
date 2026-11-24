using System.Collections.Concurrent;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Calibration;

/// <summary>
///     In-memory synthesis history store with optional JSON file persistence.
///     Records survive process restarts when a file path is configured.
///     Thread-safe for concurrent read/write from the synthesis pipeline.
/// </summary>
public sealed class InMemorySynthesisHistoryRepository : ISynthesisHistoryRepository, IDisposable
{
    private readonly ConcurrentBag<SynthesisRecord> _records = new();
    private readonly string? _persistencePath;
    private readonly ILogger<InMemorySynthesisHistoryRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // CamelCase on write, case-INSENSITIVE on read. Without the read flag this loader silently
    // returned zero records for any PascalCase history file — including the committed corpus at
    // test/corpus/fallback-replay/synthesis-history.json — and the predictor then degraded to
    // static rates with no signal. Round-tripping its own output always worked, which is why it
    // went unnoticed; the replay simulator reads the same file with PropertyNameCaseInsensitive.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <param name="options">
    ///     Configuration with <c>JsonFilePath</c>.
    ///     If the path is absolute, use it as-is.
    ///     If relative, combine with <c>AppDomain.CurrentDomain.BaseDirectory</c>.
    ///     If empty or null, records are ephemeral (lost on restart).
    /// </param>
    public InMemorySynthesisHistoryRepository(
        IOptions<SynthesisHistoryOptions> options,
        ILogger<InMemorySynthesisHistoryRepository> logger) : this(
        ResolvePath(options.Value.JsonFilePath),
        logger)
    {
    }

    /// <param name="persistencePath">
    ///     Optional path to a JSON file for persistence across restarts.
    ///     If null, records are ephemeral (lost on restart).
    /// </param>
    public InMemorySynthesisHistoryRepository(
        string? persistencePath,
        ILogger<InMemorySynthesisHistoryRepository> logger)
    {
        _persistencePath = persistencePath;
        _logger = logger;

        if (persistencePath is not null && File.Exists(persistencePath))
            LoadFromFile(persistencePath);
    }

    public int Count => _records.Count;

    public void Record(SynthesisRecord record)
    {
        _records.Add(record);
        PersistIfConfigured();
    }

    public IReadOnlyList<SynthesisRecord> FindClosestBySyllables(
        string voiceId,
        string modelId,
        double speed,
        int syllableCount,
        int maxResults = 5)
    {
        var matches = _records
            .Where(r => r.VoiceId == voiceId && r.ModelId == modelId && Math.Abs(r.Speed - speed) < 0.01)
            .OrderBy(r => Math.Abs(r.SyllableCount - syllableCount))
            .Take(maxResults)
            .ToList();

        return matches;
    }

    public IReadOnlyList<SynthesisRecord> FindClosestByDuration(
        string voiceId,
        string modelId,
        double speed,
        double targetDuration,
        int maxResults = 5)
    {
        var matches = _records
            .Where(r => r.VoiceId == voiceId && r.ModelId == modelId && Math.Abs(r.Speed - speed) < 0.01)
            .OrderBy(r => Math.Abs(r.AchievedDuration - targetDuration))
            .Take(maxResults)
            .ToList();

        return matches;
    }

    public IReadOnlyList<SynthesisRecord> GetHistory(string voiceId, string modelId)
    {
        return _records
            .Where(r => r.VoiceId == voiceId && r.ModelId == modelId)
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }

    private void PersistIfConfigured()
    {
        if (_persistencePath is null) return;

        try
        {
            _writeLock.Wait();
            string dir = Path.GetDirectoryName(_persistencePath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = _records.ToList();
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(_persistencePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist synthesis history to {Path}", _persistencePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LoadFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var records = JsonSerializer.Deserialize<List<SynthesisRecord>>(json, JsonOptions);
            if (records is not null)
                foreach (var record in records)
                    _records.Add(record);

            _logger.LogInformation("Loaded {Count} synthesis history records from {Path}", _records.Count, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load synthesis history from {Path}, starting fresh", path);
        }
    }

    private static string? ResolvePath(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        if (Path.IsPathRooted(configured))
            return configured;

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
    }
}
