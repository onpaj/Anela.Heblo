using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

/// <summary>
/// Answers a search from Fixtures/ReadSource/{query.Name}.json. Like Google, it returns only rows
/// whose segments.date matches the date the query filtered on.
/// </summary>
internal sealed class FixtureGoogleAdsApiClient : IGoogleAdsApiClient
{
    private static readonly Regex DateFilter = new(@"segments\.date = '(\d{4}-\d{2}-\d{2})'");
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReadSource");
    private readonly Dictionary<string, string> _overrides = new();

    public List<(string CustomerId, GoogleAdsQuery Query)> Calls { get; } = new();

    public FixtureGoogleAdsApiClient WithOverride(string queryName, string json)
    {
        _overrides[queryName] = json;
        return this;
    }

    public Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct)
    {
        Calls.Add((customerId, query));
        var json = _overrides.TryGetValue(query.Name, out var overridden) ? overridden : ReadFixture(query.Name);
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.TryGetProperty("results", out var results)
            ? results.EnumerateArray().Select(r => r.Clone()).ToList()
            : new List<JsonElement>();

        var date = DateFilter.Match(query.Gaql);
        if (date.Success)
            rows = rows.Where(r => r.GetProperty("segments").GetProperty("date").GetString() == date.Groups[1].Value).ToList();
        return Task.FromResult<IReadOnlyList<JsonElement>>(rows);
    }

    private string ReadFixture(string name)
    {
        var path = Path.Combine(_directory, name + ".json");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"No fixture for query '{name}'.", path);
    }
}
