using System.Globalization;
using System.Web;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>Parses optional list summary/paging query parameters without changing legacy defaults.</summary>
internal static class VersionedLibraryListQueryParser
{
    internal static Result<VersionedLibraryListQuery> Parse(HttpRequestData request)
    {
        var values = HttpUtility.ParseQueryString(request.Url.Query);
        bool includeHistory = true;
        string? rawHistory = values.Get("includeHistory");
        if (rawHistory is not null && !bool.TryParse(rawHistory, out includeHistory))
            return Result<VersionedLibraryListQuery>.Failure("includeHistory must be true or false.");

        int? pageSize = null;
        string? rawPageSize = values.Get("pageSize");
        if (rawPageSize is not null)
        {
            if (!int.TryParse(rawPageSize, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                return Result<VersionedLibraryListQuery>.Failure("pageSize must be a positive integer.");
            pageSize = parsed;
        }

        string? cursor = values.Get("cursor");
        if (string.IsNullOrWhiteSpace(cursor))
            cursor = null;
        else
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                if (!int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
                    return Result<VersionedLibraryListQuery>.Failure("cursor is invalid.");
            }
            catch (FormatException)
            {
                return Result<VersionedLibraryListQuery>.Failure("cursor is invalid.");
            }
        }

        return Result<VersionedLibraryListQuery>.Success(new VersionedLibraryListQuery(includeHistory, pageSize, cursor));
    }
}
