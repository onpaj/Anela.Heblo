using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

internal static class GraphApiHelpers
{
    internal const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string EncodePath(string path) =>
        string.Join("/", path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));

    internal static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    internal static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Graph response deserialised to null for {typeof(T).Name}.");
    }
}

internal class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public GraphFileFacet? File { get; set; }
}

internal class GraphFileFacet
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";
}

internal class GraphDriveItemCollection
{
    [JsonPropertyName("value")]
    public List<GraphDriveItem> Value { get; set; } = [];
}
