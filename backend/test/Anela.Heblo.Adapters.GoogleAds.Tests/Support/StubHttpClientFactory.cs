namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpMessageHandler> _handlers = new();

    public StubHttpClientFactory With(string name, HttpMessageHandler handler)
    {
        _handlers[name] = handler;
        return this;
    }

    public HttpClient CreateClient(string name) =>
        _handlers.TryGetValue(name, out var handler)
            ? new HttpClient(handler, disposeHandler: false)
            : throw new InvalidOperationException($"No stub handler for client '{name}'.");
}
