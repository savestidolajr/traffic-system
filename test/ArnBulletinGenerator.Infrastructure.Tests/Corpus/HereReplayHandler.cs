using System.Net;
using System.Text;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Delegating handler that serves recorded HERE responses from a <see cref="HereCorpusReplay" />
///     without touching the network — the exact mirror of <see cref="HereRecordingHandler" />.
///     Unmatched requests return <c>404</c> so the providers degrade exactly as they would for
///     genuinely missing upstream data, surfacing corpus coverage gaps rather than hiding them.
/// </summary>
internal sealed class HereReplayHandler(HereCorpusReplay replay) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (replay.TryResolve(request, out var entry))
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)entry.Status)
            {
                RequestMessage = request,
                Content = new StringContent(entry.Body, Encoding.UTF8, entry.MediaType)
            });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = request,
            Content = new StringContent(
                $"{{\"error\":\"no recorded HERE exchange for {request.Method.Method} {request.RequestUri?.AbsolutePath}\"}}",
                Encoding.UTF8,
                "application/json")
        });
    }
}
