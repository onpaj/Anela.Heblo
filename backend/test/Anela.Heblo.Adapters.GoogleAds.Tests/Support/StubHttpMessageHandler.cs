using System.Net;
using System.Text;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed record RecordedRequest(
    HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

/// <summary>Replays queued responses in order and records every request it saw.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string body) =>
        Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var headers = request.Headers.ToDictionary(
            h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));

        if (_responses.Count == 0)
            throw new InvalidOperationException($"No stubbed response left for {request.RequestUri}.");
        return _responses.Dequeue()(request);
    }
}
