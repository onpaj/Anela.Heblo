namespace Anela.Heblo.Application.Shared.WebSearch;

public interface IWebSearchClient
{
    Task<WebSearchResult> SearchAsync(string query, WebSearchOptions options, CancellationToken ct = default);
}
