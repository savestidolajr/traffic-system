namespace ArnBulletinGenerator.Functions.Contracts;

/// <summary>
///     Wire format for HTTP error responses: message, optional detail, and the numeric status
///     code echoed on the payload.
/// </summary>
internal sealed record ErrorResponse(string Error, string? Detail, int Status);
