using System.Net.Http.Headers;
using System.Text.Json;

namespace Anela.Heblo.Application.Common.Graph;

public sealed class GraphApiException : Exception
{
    public int StatusCode { get; }

    public GraphApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}

public static class GraphApiHelpers
{
    public const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    public const string GraphScope = "https://graph.microsoft.com/.default";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string EncodePath(string path) =>
        string.Join("/", path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));

    public static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Graph response deserialised to null for {typeof(T).Name}.");
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var snippet = body.Length <= 300 ? body : body[..300];
        throw new GraphApiException(
            $"Graph {context} returned {(int)response.StatusCode} {response.StatusCode}: {snippet}",
            (int)response.StatusCode);
    }
}
