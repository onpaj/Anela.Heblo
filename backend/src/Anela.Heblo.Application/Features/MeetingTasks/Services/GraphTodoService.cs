using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Common.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class GraphTodoService : IGraphTodoService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphTodoService> _logger;
    private readonly string _todoListName;

    public GraphTodoService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        IOptions<MeetingTasksOptions> options,
        ILogger<GraphTodoService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _todoListName = options.Value.TodoListName;
    }

    public async Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        try
        {
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            // OData v4 string-literal rule: single quotes inside the literal are doubled,
            // then the whole literal is URL-encoded. "O'Brien" → "O''Brien" → "O%27%27Brien".
            var doubledQuotes = email.Replace("'", "''");
            var filter = Uri.EscapeDataString($"mail eq '{doubledQuotes}'");
            var url = $"{GraphApiHelpers.GraphBaseUrl}/users?$filter={filter}&$select=id,displayName";

            var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Graph user lookup for '{Email}' returned {Status}", email, response.StatusCode);
                return null;
            }

            var result = await GraphApiHelpers.DeserializeAsync<GraphUserCollection>(response, ct);

            if (result.Value.Count == 0)
                return null;

            if (result.Value.Count > 1)
                _logger.LogInformation(
                    "Graph user lookup for '{Email}' matched {Count} users; returning first id",
                    email, result.Value.Count);

            return result.Value[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Graph user id for '{Email}'", email);
            return null;
        }
    }

    public async Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        try
        {
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var listId = await GetOrCreateTodoListAsync(client, userId, token, ct);

            var body = new Dictionary<string, object>
            {
                ["title"] = title,
                ["body"] = new Dictionary<string, string>
                {
                    ["contentType"] = "text",
                    ["content"] = description ?? string.Empty
                }
            };

            if (dueDate.HasValue)
            {
                body["dueDateTime"] = new Dictionary<string, string>
                {
                    ["dateTime"] = dueDate.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff"),
                    ["timeZone"] = "UTC"
                };
            }

            var taskUrl = $"{GraphApiHelpers.GraphBaseUrl}/users/{userId}/todo/lists/{listId}/tasks";
            var taskRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, taskUrl, token);
            taskRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(taskRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var snippet = await response.Content.ReadAsStringAsync(ct);
                var error = $"Graph POST /todo/tasks for user {userId} returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(snippet, 200)}";
                _logger.LogError("Failed to create TODO task for user {UserId}: {Status}", userId, response.StatusCode);
                return new TodoTaskResult(false, null, error);
            }

            var created = await GraphApiHelpers.DeserializeAsync<GraphTodoTask>(response, ct);
            return new TodoTaskResult(true, created.Id, null);
        }
        catch (Exception ex)
        {
            if (ex is GraphApiException gae)
                _logger.LogError(ex, "Exception while creating TODO task for user {UserId}, Status {StatusCode}", userId, gae.StatusCode);
            else
                _logger.LogError(ex, "Exception while creating TODO task for user {UserId}", userId);
            return new TodoTaskResult(false, null, ex.Message);
        }
    }

    private async Task<string> GetOrCreateTodoListAsync(
        HttpClient client,
        string userId,
        string token,
        CancellationToken ct)
    {
        var listsUrl = $"{GraphApiHelpers.GraphBaseUrl}/users/{userId}/todo/lists";

        var getRequest = GraphApiHelpers.CreateRequest(HttpMethod.Get, listsUrl, token);
        var getResponse = await client.SendAsync(getRequest, ct);
        await GraphApiHelpers.EnsureSuccessAsync(getResponse, "GET /todo/lists", ct);

        var lists = await GraphApiHelpers.DeserializeAsync<GraphTodoListCollection>(getResponse, ct);
        var existing = lists.Value.FirstOrDefault(
            l => string.Equals(l.DisplayName, _todoListName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.Id;

        var createRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, listsUrl, token);
        var createBody = JsonSerializer.Serialize(new Dictionary<string, string> { ["displayName"] = _todoListName });
        createRequest.Content = new StringContent(createBody, Encoding.UTF8, "application/json");

        var createResponse = await client.SendAsync(createRequest, ct);
        await GraphApiHelpers.EnsureSuccessAsync(createResponse, "POST /todo/lists", ct);

        var created = await GraphApiHelpers.DeserializeAsync<GraphTodoList>(createResponse, ct);
        return created.Id;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
