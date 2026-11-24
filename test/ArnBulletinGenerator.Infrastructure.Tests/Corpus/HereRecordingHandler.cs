namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Delegating handler that records each HERE HTTP exchange into the <see cref="HereCorpusWriter" />.
///     Registered as the innermost additional handler (beneath the Polly retry policy) so it observes
///     the real request sent to the network and the real response received — not retry intermediaries.
/// </summary>
internal sealed class HereRecordingHandler(HereCorpusWriter writer) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? requestBody = null;
        if (request.Content is not null)
            try
            {
                requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Non-buffered/streamed request content cannot be read twice; skip the request body.
            }

        var response = await base.SendAsync(request, cancellationToken);

        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType;

        // Re-attach a fresh buffered copy so downstream consumers can still read the body.
        var buffered = new ByteArrayContent(responseBytes);
        if (contentType is not null)
            buffered.Headers.ContentType = contentType;
        response.Content = buffered;

        await writer.RecordAsync(
            request.Method.Method,
            request.RequestUri,
            requestBody,
            (int)response.StatusCode,
            contentType?.MediaType,
            responseBytes,
            cancellationToken);

        return response;
    }
}
