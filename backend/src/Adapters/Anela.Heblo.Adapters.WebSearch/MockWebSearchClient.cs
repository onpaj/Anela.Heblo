using Anela.Heblo.Application.Shared.WebSearch;

namespace Anela.Heblo.Adapters.WebSearch;

public class MockWebSearchClient : IWebSearchClient
{
    public Task<WebSearchResult> SearchAsync(string query, WebSearchOptions options, CancellationToken ct = default)
    {
        var result = new WebSearchResult
        {
            Query = query,
            Hits =
            [
                new WebSearchHit { Title = $"Mock result 1 for: {query}", Url = "https://example.com/1", Snippet = "Mock snippet for first result." },
                new WebSearchHit { Title = $"Mock result 2 for: {query}", Url = "https://example.com/2", Snippet = "Mock snippet for second result." }
            ]
        };
        return Task.FromResult(result);
    }
}
