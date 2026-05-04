namespace Anela.Heblo.Application.Shared.WebSearch;

public class WebSearchResult
{
    public string Query { get; set; } = "";
    public IReadOnlyList<WebSearchHit> Hits { get; set; } = [];
}
