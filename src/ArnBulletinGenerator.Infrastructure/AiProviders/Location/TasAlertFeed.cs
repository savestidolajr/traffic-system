using System.Xml.Linq;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Tasmania Police TasALERT road alerts feed (RSS / XML).
/// </summary>
public sealed class TasAlertFeed(
    HttpClient httpClient,
    IOptions<OfficialFeedsOptions> options,
    ILogger<TasAlertFeed> logger) : IOfficialTrafficFeed
{
    private readonly OfficialFeedsOptions _options = options.Value;

    public string SourceId => "tasalert";
    /// <summary>
    ///     Intentionally empty: TasALERT is police-media RSS with no coordinates and no
    ///     structured traffic fields. Its prose is retained on the event for audit, but this
    ///     adapter does not expose a usable canonical field for extraction.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        "tasalert",
        "rss-no-structured-fields",
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    public async Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        using var feedBudget = OfficialFeedCallBudget.Begin(OfficialFeedCallBudget.MaxFeedTimeout);
        try
        {
            using var requestCts = OfficialFeedCallBudget.Current!
                .CreateRequestCancellation(cancellationToken);
            using var response = await httpClient.GetAsync(
                _options.TasAlertUrl, requestCts.Token);
            response.EnsureSuccessStatusCode();
            string xml = await response.Content.ReadAsStringAsync(requestCts.Token);
            var doc = XDocument.Parse(xml);
            var events = new List<OfficialTrafficEvent>();

            foreach (var item in doc.Descendants("item"))
            {
                string title = item.Element("title")?.Value ?? "";
                string description = item.Element("description")?.Value ?? "";
                string link = item.Element("link")?.Value ?? "";
                string? id = item.Element("guid")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    id = string.IsNullOrWhiteSpace(link) ? null : link.Trim();

                // TasALERT posts road closure / crash alerts
                bool isCrash = title.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                               description.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                               title.Contains("accident", StringComparison.OrdinalIgnoreCase);

                // The RSS items carry no geocode. NEVER fabricate one: stamping the
                // requesting market's own centre made a Devonport crash read as being AT
                // Sydney's centroid, pass every distance filter, and sit in Sydney's
                // in-play evidence (caught live 2026-08-18 via the Desk's payload view).
                events.Add(new OfficialTrafficEvent(
                    SourceId: SourceId,
                    Headline: title,
                    IsIncidentClass: isCrash,
                    Shape: [],
                    Id: id,
                    Description: description));
            }

            return Result<IReadOnlyList<OfficialTrafficEvent>>.Success(events);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[OfficialFeed] TasALERT fetch failed");
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Failure($"TasALERT feed unavailable: {ex.Message}");
        }
    }
}
