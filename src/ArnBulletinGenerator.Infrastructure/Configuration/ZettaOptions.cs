using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Transport used to deliver approved audio to the playout ingest.
/// </summary>
public enum DeliveryTransport
{
    /// <summary>Write to an SFTP-enabled Azure Blob storage drop the playout side pulls from.</summary>
    BlobDrop,

    /// <summary>
    ///     Write to an Azure Files SMB share (AD DS-authenticated) on the drop account —
    ///     the playout side mounts the share directly over SMB using its own on-prem AD
    ///     identity, no SFTP client or vaulted credential required on either side.
    /// </summary>
    AzureFilesDrop,

    /// <summary>
    ///     Keep the bytes in memory and write nothing. The full delivery path still runs
    ///     (integrity seal, market-code resolution, file naming, tray probe), so delivery
    ///     behaviour stays testable in an environment that must never put audio on air.
    ///     <para>
    ///         The drop share is a live broadcast input — the playout side mounts it and
    ///         collects whatever appears — so any non-production environment aimed at a real
    ///         share can air a bulletin as a side effect of a test.
    ///     </para>
    /// </summary>
    Mock
}

public sealed class ZettaOptions
{
    public const string SectionName = "Zetta";

    public bool Enabled { get; set; }

    /// <summary>
    ///     Delivery transport. <see cref="DeliveryTransport.AzureFilesDrop" /> writes to an
    ///     AD DS-authenticated Azure Files share on the drop account (the live transport in
    ///     every environment); <see cref="DeliveryTransport.BlobDrop" /> writes to the
    ///     SFTP-enabled blob container on the same account. Read eagerly at DI registration
    ///     to select the writer implementation.
    ///     <para>
    ///         A third transport wrote SMB directly into Zetta's on-premises ingest share. It was
    ///         removed on 2026-07-31: it was never enabled in any environment, and the drop-share
    ///         model inverted the direction — the playout side is the SMB client now, so nothing
    ///         here speaks SMB, needs an on-premises network path, or holds a password.
    ///     </para>
    /// </summary>
    public DeliveryTransport Transport { get; set; } = DeliveryTransport.AzureFilesDrop;

    /// <summary>
    ///     Storage account name of the SFTP-enabled blob drop (BlobDrop transport only),
    ///     e.g. "ibpauetststaftp01". The Function App's managed identity needs
    ///     Storage Blob Data Contributor on this account.
    /// </summary>
    public string DropAccountName { get; set; } = "";

    /// <summary>
    ///     Container within the drop account that the playout SFTP user pulls from
    ///     (BlobDrop transport only).
    /// </summary>
    public string DropContainerName { get; set; } = "informationbulletins";

    /// <summary>
    ///     Azure Files share name within the drop account that the playout side mounts
    ///     over AD DS-authenticated SMB (AzureFilesDrop transport only). Distinct
    ///     namespace from <see cref="DropContainerName" /> even when the names match —
    ///     blob containers and file shares are separate services on the same account.
    /// </summary>
    public string DropFileShareName { get; set; } = "informationbulletins";

    /// <summary>Timeout in seconds for each delivery write. Default 30.</summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     File name pattern. {MarketId} is replaced with the market's configured Zetta code,
    ///     exactly as ARN supplied it. Default produces e.g. MEL_TRAIN_TRAFF.wav; ARN's 2026-07-30
    ///     convention uses "{MarketId}_TRAFF" with codes like "[SYD]KIIS90s".
    /// </summary>
    public string FileNamePattern { get; set; } = "{MarketId}_TRAIN_TRAFF";

    /// <summary>
    ///     Maps market IDs to Zetta market codes used in file names.
    ///     Keys are case-insensitive market IDs (e.g., "sydney"), values are
    ///     the short codes Zetta expects (e.g., "SYD").
    ///     <para>
    ///         A market with no entry has NO code, and delivery fails rather than inventing one.
    ///         This used to fall back to the market id, which meant an unmapped market silently
    ///         shipped a file Zetta was never told to expect: as of 2026-07-29 only 6 of the 13
    ///         configured markets are mapped, so the `cairns-4ca` pilot station would have
    ///         delivered `CAIRNS-4CA_TRAIN_TRAFF.wav` against a convention proven as
    ///         `SYD_TRAIN_TRAFF.wav`. A file under the wrong name does not air and nothing reports
    ///         it; a failed delivery does.
    ///     </para>
    /// </summary>
    public Dictionary<string, string> MarketCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     A market id that an app-setting name cannot express, merged over <see cref="MarketCodes" />.
    ///     Azure rejects app-setting names containing a hyphen on Linux apps, so the pilot market
    ///     `cairns-4ca` has no expressible `Zetta__MarketCodes__*` key at all. A single JSON object
    ///     of market id to code sidesteps the key-name rules and works for any future id.
    ///     Example: <c>{"cairns-4ca":"[CAI]STAR1027"}</c>. Malformed JSON is ignored with the
    ///     per-key map left intact, because a delivery that refuses is better than one that guesses.
    /// </summary>
    public string MarketCodesJson { get; set; } = "";

    /// <summary>
    ///     Resolves a market ID to its Zetta market code, or null when the market has no configured
    ///     code. Callers MUST treat null as a delivery failure: the code is ARN's playout-side
    ///     naming convention and cannot be derived from the market id.
    /// </summary>
    public string? ResolveMarketCode(string marketId)
    {
        if (JsonCodes().TryGetValue(marketId, out string? jsonCode) && !string.IsNullOrWhiteSpace(jsonCode))
            return jsonCode;

        return MarketCodes.TryGetValue(marketId, out string? code) && !string.IsNullOrWhiteSpace(code)
            ? code
            : null;
    }

    private Dictionary<string, string> JsonCodes()
    {
        if (string.IsNullOrWhiteSpace(MarketCodesJson))
            return _emptyCodes;
        if (_parsedJson is not null && ReferenceEquals(_parsedJsonSource, MarketCodesJson))
            return _parsedJson;

        Dictionary<string, string> parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(MarketCodesJson)
                is { } map
                ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase)
                : _emptyCodes;
        }
        catch (JsonException)
        {
            parsed = _emptyCodes;
        }

        _parsedJsonSource = MarketCodesJson;
        _parsedJson = parsed;
        return parsed;
    }

    private static readonly Dictionary<string, string> _emptyCodes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _parsedJson;
    private string? _parsedJsonSource;
}
