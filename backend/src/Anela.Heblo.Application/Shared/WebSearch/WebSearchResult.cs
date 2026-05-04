namespace Anela.Heblo.Application.Shared.WebSearch;

public class WebSearchResult
{
    public string Query { get; set; } = "";
    public List<WebSearchHit> Hits { get; set; } = new();
}
