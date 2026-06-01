using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Common.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class GraphPlannerService : IMeetingTaskExporter
{
    private const string DelegatedPlannerScope = "https://graph.microsoft.com/Tasks.ReadWrite";

    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphPlannerService> _logger;
    private readonly string _planId;
    private readonly string? _bucketId;

    // Single-flight delegated-token cache, scoped to one /submit request (service is Scoped).
    // Prevents MSAL's internal retry loop from firing once per approved task when consent is missing.
    private Task<string>? _delegatedTokenTask;

    public GraphPlannerService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        IOptions<MeetingTasksOptions> options,
        ILogger<GraphPlannerService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _planId = options.Value.PlannerPlanId;
        _bucketId = options.Value.PlannerBucketId;
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

    public async Task<MeetingTaskExportResult> ExportTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        try
        {
            var token = await GetDelegatedTokenAsync();
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var body = new Dictionary<string, object>
            {
                ["planId"] = _planId,
                ["title"] = title,
                ["assignments"] = new Dictionary<string, object>
                {
                    [userId] = new Dictionary<string, string>
                    {
                        ["@odata.type"] = "#microsoft.graph.plannerAssignment",
                        ["orderHint"] = " !"
                    }
                }
            };

            if (_bucketId is not null)
                body["bucketId"] = _bucketId;

            if (dueDate.HasValue)
                body["dueDateTime"] = dueDate.Value.ToUniversalTime().ToString("o");

            var taskUrl = $"{GraphApiHelpers.GraphBaseUrl}/planner/tasks";
            var taskRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, taskUrl, token);
            taskRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var taskResponse = await client.SendAsync(taskRequest, ct);
            await GraphApiHelpers.EnsureSuccessAsync(taskResponse, "POST /planner/tasks", ct);

            var created = await GraphApiHelpers.DeserializeAsync<GraphPlannerTask>(taskResponse, ct);

            if (!string.IsNullOrWhiteSpace(description))
            {
                try
                {
                    await PatchDescriptionAsync(client, created.Id, description, token, ct);
                }
                catch (Exception patchEx)
                {
                    // Task already created — swallow patch failure so ExternalTaskId is persisted on
                    // return. Without this, a retry would create a duplicate Planner task.
                    _logger.LogWarning(patchEx,
                        "Failed to patch description for Planner task {TaskId}; task created without description",
                        created.Id);
                }
            }

            return new MeetingTaskExportResult(true, created.Id, null);
        }
        catch (Exception ex)
        {
            if (ex is GraphApiException gae)
                _logger.LogError(ex, "Graph error exporting Planner task for user {UserId}, Status {StatusCode}", userId, gae.StatusCode);
            else
                _logger.LogError(ex, "Exception exporting Planner task for user {UserId}", userId);
            return new MeetingTaskExportResult(false, null, ex.Message);
        }
    }

    private Task<string> GetDelegatedTokenAsync()
    {
        return _delegatedTokenTask ??= AcquireDelegatedTokenAsync();
    }

    private async Task<string> AcquireDelegatedTokenAsync()
    {
        try
        {
            return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedPlannerScope });
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex,
                "User consent required for Graph scope {Scope}. Grant admin consent in Azure Portal.",
                DelegatedPlannerScope);
            throw new InvalidOperationException(
                $"Microsoft 365 consent required for scope {DelegatedPlannerScope}. An admin must grant consent in Azure Portal.", ex);
        }
    }

    private async Task PatchDescriptionAsync(
        HttpClient client,
        string taskId,
        string description,
        string token,
        CancellationToken ct)
    {
        var detailsUrl = $"{GraphApiHelpers.GraphBaseUrl}/planner/tasks/{taskId}/details";

        var getRequest = GraphApiHelpers.CreateRequest(HttpMethod.Get, detailsUrl, token);
        var getResponse = await client.SendAsync(getRequest, ct);
        await GraphApiHelpers.EnsureSuccessAsync(getResponse, $"GET /planner/tasks/{taskId}/details", ct);

        var etag = getResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException(
                $"Planner GET details for task {taskId} returned no ETag — cannot PATCH description.");

        var patchRequest = GraphApiHelpers.CreateRequest(HttpMethod.Patch, detailsUrl, token);
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        patchRequest.Content = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string> { ["description"] = description }),
            Encoding.UTF8,
            "application/json");

        var patchResponse = await client.SendAsync(patchRequest, ct);
        await GraphApiHelpers.EnsureSuccessAsync(patchResponse, $"PATCH /planner/tasks/{taskId}/details", ct);
    }
}
