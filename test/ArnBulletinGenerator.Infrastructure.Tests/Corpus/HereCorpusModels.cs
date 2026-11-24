using System.Text.Json;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Shared on-disk schema for the recorded external-API corpus. Written by the capture harness
///     (<see cref="HereCorpusWriter" />) and read by the replay layer (<see cref="HereCorpusReplay" />)
///     so the two cannot drift.
/// </summary>
internal sealed record CorpusRecord(
    string Market,
    string Endpoint,
    string SessionId,
    DateTimeOffset CapturedAtUtc,
    CorpusRequest Request,
    CorpusResponse Response);

internal sealed record CorpusRequest(string Method, string Uri, string? Body);

internal sealed record CorpusResponse(int Status, string? MediaType, JsonElement? Body, string? RawBody);

internal sealed record ManifestEntry(
    string Market,
    string Endpoint,
    string SessionId,
    DateTimeOffset CapturedAtUtc,
    string Method,
    string Uri,
    int Status,
    string File);
