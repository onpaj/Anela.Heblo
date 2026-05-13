# Subtask 5: Graph TODO Integration & Controller Wiring

**Parent Epic:** Meeting Task Validation Checkpoint

CRITICAL - This is part of epic, you **MUST** use epic branch - feat/meeting-task-validation-epic as a source for this feature branch and create a PR back to this branch instead of main


## Task 7: GraphTodoService

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`

- [ ] **Step 1: Create IGraphTodoService interface**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record TodoTaskResult(bool Success, string? ExternalTaskId, string? Error);

public interface IGraphTodoService
{
    Task<string?> ResolveUserIdAsync(string assigneeName, CancellationToken ct = default);
    Task<TodoTaskResult> CreateTodoTaskAsync(string userId, string title, string description, DateTime? dueDate, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Abstractions;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GraphTodoServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<ITokenAcquisition> _tokenAcquisitionMock = new();
    private readonly Mock<ILogger<GraphTodoService>> _loggerMock = new();

    private GraphTodoService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com") };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(client);
        _tokenAcquisitionMock
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        return new GraphTodoService(_tokenAcquisitionMock.Object, _httpClientFactoryMock.Object, _loggerMock.Object, "Meeting Actions");
    }

    [Fact]
    public async Task ResolveUserIdAsync_FindsUserByDisplayName()
    {
        var responseJson = JsonSerializer.Serialize(new { value = new[] { new { id = "user-123", displayName = "Alice" } } });
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        });

        var service = CreateService(handler);
        var userId = await service.ResolveUserIdAsync("Alice");

        Assert.Equal("user-123", userId);
    }

    [Fact]
    public async Task ResolveUserIdAsync_ReturnsNullWhenNotFound()
    {
        var responseJson = JsonSerializer.Serialize(new { value = Array.Empty<object>() });
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        });

        var service = CreateService(handler);
        var userId = await service.ResolveUserIdAsync("NonExistent");

        Assert.Null(userId);
    }

    [Fact]
    public async Task CreateTodoTaskAsync_ReturnsSuccess()
    {
        // Two calls: first GET lists, then POST create task
        var listsJson = JsonSerializer.Serialize(new { value = new[] { new { id = "list-1", displayName = "Meeting Actions" } } });
        var taskJson = JsonSerializer.Serialize(new { id = "task-abc" });

        var handler = new MockHttpHandler(new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listsJson) },
            new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(taskJson) }
        }));

        var service = CreateService(handler);
        var result = await service.CreateTodoTaskAsync("user-123", "Do thing", "Details", new DateTime(2026, 5, 1));

        Assert.True(result.Success);
        Assert.Equal("task-abc", result.ExternalTaskId);
    }
}

// Simple mock HTTP handler for testing
internal class MockHttpHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public MockHttpHandler(HttpResponseMessage singleResponse)
    {
        _responses = new Queue<HttpResponseMessage>(new[] { singleResponse });
    }

    public MockHttpHandler(Queue<HttpResponseMessage> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responses.Dequeue());
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GraphTodoServiceTests"`
Expected: FAIL — `GraphTodoService` does not exist

- [ ] **Step 4: Implement GraphTodoService**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Abstractions;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class GraphTodoService : IGraphTodoService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphTodoService> _logger;
    private readonly string _todoListName;

    private const string GraphScope = "https://graph.microsoft.com/.default";

    public GraphTodoService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        ILogger<GraphTodoService> logger,
        string todoListName)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _todoListName = todoListName;
    }

    public async Task<string?> ResolveUserIdAsync(string assigneeName, CancellationToken ct = default)
    {
        try
        {
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope, cancellationToken: ct);
            var client = _httpClientFactory.CreateClient("MicrosoftGraph");
            var encodedName = Uri.EscapeDataString(assigneeName);
            var url = $"{GraphApiHelpers.GraphBaseUrl}/users?$filter=displayName eq '{encodedName}'&$select=id,displayName";
            var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await GraphApiHelpers.DeserializeAsync<GraphUserCollection>(response, ct);
            return result.Value.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve user ID for {Assignee}", assigneeName);
            return null;
        }
    }

    public async Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId, string title, string description, DateTime? dueDate, CancellationToken ct = default)
    {
        try
        {
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope, cancellationToken: ct);
            var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var listId = await GetOrCreateTodoListAsync(client, token, userId, ct);

            var taskBody = new Dictionary<string, object>
            {
                ["title"] = title,
                ["body"] = new { contentType = "text", content = description }
            };
            if (dueDate.HasValue)
            {
                taskBody["dueDateTime"] = new
                {
                    dateTime = dueDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = "UTC"
                };
            }

            var url = $"{GraphApiHelpers.GraphBaseUrl}/users/{userId}/todo/lists/{listId}/tasks";
            var request = GraphApiHelpers.CreateRequest(HttpMethod.Post, url, token);
            request.Content = new StringContent(JsonSerializer.Serialize(taskBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var created = await GraphApiHelpers.DeserializeAsync<GraphTodoTask>(response, ct);
            return new TodoTaskResult(true, created.Id, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TODO task for user {UserId}: {Title}", userId, title);
            return new TodoTaskResult(false, null, ex.Message);
        }
    }

    private async Task<string> GetOrCreateTodoListAsync(
        HttpClient client, string token, string userId, CancellationToken ct)
    {
        var url = $"{GraphApiHelpers.GraphBaseUrl}/users/{userId}/todo/lists";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var lists = await GraphApiHelpers.DeserializeAsync<GraphTodoListCollection>(response, ct);
        var existing = lists.Value.FirstOrDefault(l =>
            string.Equals(l.DisplayName, _todoListName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing.Id;

        // Create the list
        var createRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, url, token);
        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { displayName = _todoListName }),
            Encoding.UTF8, "application/json");
        var createResponse = await client.SendAsync(createRequest, ct);
        createResponse.EnsureSuccessStatusCode();

        var created = await GraphApiHelpers.DeserializeAsync<GraphTodoList>(createResponse, ct);
        return created.Id;
    }
}

internal class GraphUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
}

internal class GraphUserCollection
{
    [JsonPropertyName("value")] public List<GraphUser> Value { get; set; } = new();
}

internal class GraphTodoList
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
}

internal class GraphTodoListCollection
{
    [JsonPropertyName("value")] public List<GraphTodoList> Value { get; set; } = new();
}

internal class GraphTodoTask
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GraphTodoServiceTests"`
Expected: All 3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
git commit -m "feat(meeting-tasks): add GraphTodoService for creating tasks in assignees' Microsoft TODO"
```

---

## Task 8: SubmitToTodo Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs`

- [ ] **Step 1: Create request/response**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoRequest : IRequest<SubmitToTodoResponse>
{
    public Guid TranscriptId { get; set; }
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoResponse : BaseResponse
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

- [ ] **Step 2: Write tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class SubmitToTodoHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();
    private readonly Mock<IGraphTodoService> _todoServiceMock = new();
    private readonly Mock<ILogger<SubmitToTodoHandler>> _loggerMock = new();

    private SubmitToTodoHandler CreateHandler() =>
        new(_repoMock.Object, _todoServiceMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_SubmitsApprovedTasks_SkipsRejected()
    {
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            Subject = "Test",
            Summary = "Summary",
            SourceEmail = "test@example.com",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new() { Id = Guid.NewGuid(), Title = "Approved Task", Assignee = "Alice", Description = "Do it", Status = ProposedTaskStatus.Approved },
                new() { Id = Guid.NewGuid(), Title = "Rejected Task", Assignee = "Bob", Description = "Skip", Status = ProposedTaskStatus.Rejected },
                new() { Id = Guid.NewGuid(), Title = "Pending Task", Assignee = "Charlie", Description = "Ignored", Status = ProposedTaskStatus.Pending }
            }
        };
        _repoMock.Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _todoServiceMock.Setup(s => s.ResolveUserIdAsync("Alice", It.IsAny<CancellationToken>())).ReturnsAsync("user-alice");
        _todoServiceMock.Setup(s => s.CreateTodoTaskAsync("user-alice", "Approved Task", "Do it", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));

        var result = await CreateHandler().Handle(new SubmitToTodoRequest { TranscriptId = transcriptId }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal("ext-1", transcript.Tasks[0].ExternalTaskId);
        Assert.Equal(MeetingTranscriptStatus.PartiallyApproved, transcript.Status);
    }

    [Fact]
    public async Task Handle_AllApproved_SetsStatusToApproved()
    {
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            Subject = "Test",
            Summary = "Summary",
            SourceEmail = "test@example.com",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new() { Id = Guid.NewGuid(), Title = "Task 1", Assignee = "Alice", Description = "D", Status = ProposedTaskStatus.Approved }
            }
        };
        _repoMock.Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _todoServiceMock.Setup(s => s.ResolveUserIdAsync("Alice", It.IsAny<CancellationToken>())).ReturnsAsync("user-alice");
        _todoServiceMock.Setup(s => s.CreateTodoTaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));

        var result = await CreateHandler().Handle(new SubmitToTodoRequest { TranscriptId = transcriptId }, CancellationToken.None);

        Assert.Equal(MeetingTranscriptStatus.Approved, transcript.Status);
    }

    [Fact]
    public async Task Handle_UnresolvableAssignee_ReportsError()
    {
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            Subject = "Test",
            Summary = "S",
            SourceEmail = "t@e.com",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new() { Id = Guid.NewGuid(), Title = "Task", Assignee = "Unknown", Description = "D", Status = ProposedTaskStatus.Approved }
            }
        };
        _repoMock.Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _todoServiceMock.Setup(s => s.ResolveUserIdAsync("Unknown", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await CreateHandler().Handle(new SubmitToTodoRequest { TranscriptId = transcriptId }, CancellationToken.None);

        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SubmitToTodoHandlerTests"`
Expected: FAIL

- [ ] **Step 4: Implement handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoHandler : IRequestHandler<SubmitToTodoRequest, SubmitToTodoResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IGraphTodoService _todoService;
    private readonly ILogger<SubmitToTodoHandler> _logger;

    public SubmitToTodoHandler(
        IMeetingTranscriptRepository repository,
        IGraphTodoService todoService,
        ILogger<SubmitToTodoHandler> logger)
    {
        _repository = repository;
        _todoService = todoService;
        _logger = logger;
    }

    public async Task<SubmitToTodoResponse> Handle(SubmitToTodoRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript == null)
            return new SubmitToTodoResponse { Success = false, ErrorCode = Shared.ErrorCodes.NotFound };

        var approvedTasks = transcript.Tasks.Where(t => t.Status == ProposedTaskStatus.Approved && t.ExternalTaskId == null).ToList();

        var successCount = 0;
        var errors = new List<string>();

        foreach (var task in approvedTasks)
        {
            var userId = await _todoService.ResolveUserIdAsync(task.Assignee, cancellationToken);
            if (userId == null)
            {
                errors.Add($"Could not resolve user '{task.Assignee}' for task '{task.Title}'");
                continue;
            }

            var result = await _todoService.CreateTodoTaskAsync(userId, task.Title, task.Description, task.DueDate, cancellationToken);
            if (result.Success)
            {
                task.ExternalTaskId = result.ExternalTaskId;
                successCount++;
            }
            else
            {
                errors.Add($"Failed to create task '{task.Title}' for {task.Assignee}: {result.Error}");
            }
        }

        // Update transcript status
        var allTasksProcessed = transcript.Tasks.All(t =>
            t.Status == ProposedTaskStatus.Rejected || t.ExternalTaskId != null);
        var hasRejected = transcript.Tasks.Any(t => t.Status == ProposedTaskStatus.Rejected);

        transcript.Status = allTasksProcessed
            ? (hasRejected ? MeetingTranscriptStatus.PartiallyApproved : MeetingTranscriptStatus.Approved)
            : MeetingTranscriptStatus.PendingReview;

        transcript.ReviewedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Submitted {SuccessCount} tasks to TODO for transcript {Id}, {FailedCount} failed",
            successCount, transcript.Id, errors.Count);

        return new SubmitToTodoResponse
        {
            SuccessCount = successCount,
            FailedCount = errors.Count,
            Errors = errors
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SubmitToTodoHandlerTests"`
Expected: All 3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs
git commit -m "feat(meeting-tasks): add SubmitToTodo handler — pushes approved tasks to MS TODO via Graph API"
```

---

## Task 9: Module Registration & Controller

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — add module registration

> **Architecture change:** `ApiKeyAuthAttribute.cs` is **not** created here — the n8n webhook endpoint has been removed. Transcripts are now ingested automatically by the Plaud polling job (see #647).

- [ ] **Step 1: Create MeetingTasksOptions**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs
namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public string TodoListName { get; set; } = "Meeting Actions";
}
```

- [ ] **Step 2: Create MeetingTasksModule**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs
using Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.MeetingTasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Abstractions;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public static class MeetingTasksModule
{
    public static IServiceCollection AddMeetingTasksModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MeetingTasksOptions>(configuration.GetSection("MeetingTasks"));

        services.AddScoped<IMeetingTranscriptRepository, MeetingTranscriptRepository>();
        services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
        services.AddTransient<PlaudPollingJob>();

        services.AddScoped<IGraphTodoService>(sp =>
        {
            var options = configuration.GetSection("MeetingTasks").Get<MeetingTasksOptions>() ?? new MeetingTasksOptions();
            return new GraphTodoService(
                sp.GetRequiredService<ITokenAcquisition>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GraphTodoService>>(),
                options.TodoListName);
        });

        return services;
    }
}
```

- [ ] **Step 3: Register module in ApplicationModule.cs**

Add `using Anela.Heblo.Application.Features.MeetingTasks;` to the usings and add this line after `services.AddMarketingInvoicesModule();` (line 72) in `backend/src/Anela.Heblo.Application/ApplicationModule.cs`:

```csharp
services.AddMeetingTasksModule(configuration);
```

- [ ] **Step 4: Create MeetingTasksController**

```csharp
// backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

// Note: no n8n webhook endpoint — transcripts are ingested automatically by PlaudPollingJob (see #647)
[ApiController]
[Route("api/meeting-tasks")]
[Authorize]
public class MeetingTasksController : BaseApiController
{
    private readonly IMediator _mediator;

    public MeetingTasksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// List meeting transcripts
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<GetTranscriptListResponse>> GetList(
        [FromQuery] GetTranscriptListRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(request, ct);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get transcript detail with tasks
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<GetTranscriptDetailResponse>> GetDetail(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var response = await _mediator.Send(new GetTranscriptDetailRequest { Id = id }, ct);
        return HandleResponse(response);
    }

    /// <summary>
    /// Edit a proposed task
    /// </summary>
    [HttpPut("{transcriptId:guid}/tasks/{taskId:guid}")]
    [Authorize]
    public async Task<ActionResult> UpdateTask(
        [FromRoute] Guid transcriptId, [FromRoute] Guid taskId,
        [FromBody] UpdateProposedTaskRequest request, CancellationToken ct)
    {
        request.TranscriptId = transcriptId;
        request.TaskId = taskId;
        var response = await _mediator.Send(request, ct);
        return HandleResponse(response);
    }

    /// <summary>
    /// Approve or reject a proposed task
    /// </summary>
    [HttpPut("{transcriptId:guid}/tasks/{taskId:guid}/status")]
    [Authorize]
    public async Task<ActionResult> UpdateTaskStatus(
        [FromRoute] Guid transcriptId, [FromRoute] Guid taskId,
        [FromBody] UpdateProposedTaskStatusRequest request, CancellationToken ct)
    {
        request.TranscriptId = transcriptId;
        request.TaskId = taskId;
        var response = await _mediator.Send(request, ct);
        return HandleResponse(response);
    }

    /// <summary>
    /// Add a new task manually
    /// </summary>
    [HttpPost("{transcriptId:guid}/tasks")]
    [Authorize]
    public async Task<ActionResult<AddProposedTaskResponse>> AddTask(
        [FromRoute] Guid transcriptId,
        [FromBody] AddProposedTaskRequest request, CancellationToken ct)
    {
        request.TranscriptId = transcriptId;
        var response = await _mediator.Send(request, ct);
        return HandleResponse(response);
    }

    /// <summary>
    /// Submit approved tasks to Microsoft TODO
    /// </summary>
    [HttpPost("{transcriptId:guid}/submit")]
    [Authorize]
    public async Task<ActionResult<SubmitToTodoResponse>> SubmitToTodo(
        [FromRoute] Guid transcriptId, CancellationToken ct)
    {
        var response = await _mediator.Send(new SubmitToTodoRequest { TranscriptId = transcriptId }, ct);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API/`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git add backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs
git commit -m "feat(meeting-tasks): add MeetingTasksController, module registration, and config (Plaud pull architecture)"
```

---


---

> **Integration:** Create your feature branch from `feat/meeting-task-validation-epic`. When done, open a PR targeting `feat/meeting-task-validation-epic` (not `main`).


