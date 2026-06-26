# Fix Meeting Task 401 + Migrate to Microsoft Planner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the silent 401 error in meeting-task submission immediately (Part A), then re-platform the export from Microsoft To Do to Microsoft Planner so it actually works with app-only tokens (Part B).

**Architecture:** Part A adds a `GraphApiException` + `EnsureSuccessAsync` helper to `GraphApiHelpers`, replacing the bare `EnsureSuccessStatusCode()` calls that swallowed Graph error bodies. Part B replaces the To Do service with a new `GraphPlannerService` behind a renamed `IMeetingTaskExporter` interface; the handler and DI wiring are updated to match.

**Tech Stack:** .NET 8, Microsoft.Identity.Web (`ITokenAcquisition`), `HttpClient` + `IHttpClientFactory`, xUnit, FluentAssertions, Moq, MediatR.

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs` |
| Modify (then delete) | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs` |
| Modify (then delete) | `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExporter.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphPlannerService.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpMeetingTaskExporter.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphPlannerServiceTests.cs` |
| Delete | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs` |
| Delete | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs` |
| Delete | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpGraphTodoService.cs` |
| Delete | `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs` |
| Modify | `backend/src/Anela.Heblo.API/appsettings.json` |

---

## Part A — Surface the 401

### Task 1: Write the failing test for 401 body surfacing

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`

Add a new test after the last `CreateTodoTaskAsync_*` test. This test currently FAILS because `EnsureSuccessStatusCode()` throws `HttpRequestException` whose message contains "401" but NOT the response body.

- [ ] **Step 1: Add the failing test**

Open `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs` and insert this test before the `RecordingHandler` nested class (line 289):

```csharp
[Fact]
public async Task CreateTodoTaskAsync_TodoListLookup401_ErrorIncludesStatusCodeAndBody()
{
    // GET /todo/lists returns 401 with a Graph error body.
    // After Part A the error must carry both the status code AND a snippet of the body.
    var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent(
            """{"error":{"code":"InvalidAuthenticationToken","message":"Access token is invalid."}}""",
            Encoding.UTF8, "application/json")
    });

    var result = await service.CreateTodoTaskAsync("user-1", "T", "desc", null);

    result.Success.Should().BeFalse();
    result.Error.Should().Contain("401");
    result.Error.Should().Contain("InvalidAuthenticationToken");
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test --filter "CreateTodoTaskAsync_TodoListLookup401_ErrorIncludesStatusCodeAndBody" -v minimal
```

Expected: **FAIL** — the assertion `result.Error.Should().Contain("InvalidAuthenticationToken")` fails because the current implementation does not read the response body.

---

### Task 2: Add GraphApiException and EnsureSuccessAsync to GraphApiHelpers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs`

- [ ] **Step 1: Replace the file content with the updated version**

```csharp
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
```

- [ ] **Step 2: Verify it compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj -v quiet
```

Expected: Build succeeded, 0 errors.

---

### Task 3: Update GraphTodoService to use EnsureSuccessAsync

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`

- [ ] **Step 1: Replace GetOrCreateTodoListAsync — swap both EnsureSuccessStatusCode calls**

In `GetOrCreateTodoListAsync` (around lines 139–153), replace:
```csharp
getResponse.EnsureSuccessStatusCode();
```
with:
```csharp
await GraphApiHelpers.EnsureSuccessAsync(getResponse, "GET /todo/lists", ct);
```

And replace:
```csharp
createResponse.EnsureSuccessStatusCode();
```
with:
```csharp
await GraphApiHelpers.EnsureSuccessAsync(createResponse, "POST /todo/lists", ct);
```

- [ ] **Step 2: Update the catch block in CreateTodoTaskAsync to log the status code**

Replace the existing `catch` block (lines 124–127):
```csharp
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while creating TODO task for user {UserId}", userId);
            return new TodoTaskResult(false, null, ex.Message);
        }
```
with:
```csharp
        catch (Exception ex)
        {
            if (ex is GraphApiException gae)
                _logger.LogError(ex, "Exception while creating TODO task for user {UserId}, Status {StatusCode}", userId, gae.StatusCode);
            else
                _logger.LogError(ex, "Exception while creating TODO task for user {UserId}", userId);
            return new TodoTaskResult(false, null, ex.Message);
        }
```

- [ ] **Step 3: Run the Part A test to confirm it now passes**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test --filter "CreateTodoTaskAsync_TodoListLookup401_ErrorIncludesStatusCodeAndBody" -v minimal
```

Expected: **PASS**.

- [ ] **Step 4: Run the full MeetingTasks test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test --filter MeetingTasks -v minimal
```

Expected: All tests pass.

---

### Task 4: Build, format, and commit Part A

**Files:** all modified so far

- [ ] **Step 1: Build + format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build && dotnet format
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja
git add backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
git commit -m "fix(meeting-tasks): surface Graph HTTP status and body on 401 instead of swallowing it"
```

---

## Part B — Migrate task export to Microsoft Planner

### Task 5: Update GraphTodoContracts.cs — remove To Do types, add Planner types

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs`

The file currently holds `GraphUser`, `GraphUserCollection`, `GraphTodoList`, `GraphTodoListCollection`, `GraphTodoTask`. The To Do list types are no longer needed. Replace the entire file:

- [ ] **Step 1: Replace file content**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

internal class GraphUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

internal class GraphUserCollection
{
    [JsonPropertyName("value")]
    public List<GraphUser> Value { get; set; } = [];
}

internal class GraphPlannerTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class GraphPlannerTaskDetails
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj -v quiet
```

Expected: Build succeeded. (GraphTodoService.cs references `GraphTodoList` / `GraphTodoListCollection` / `GraphTodoTask` — it will break here because those types are removed. That's expected and will be fixed in Task 10 when we delete GraphTodoService. For now, check only if _new_ errors appear.)

Actually — wait. `GraphTodoService.cs` still references `GraphTodoListCollection` and `GraphTodoTask`. The build will fail. The correct order is:

Create `IMeetingTaskExporter.cs` and `GraphPlannerService.cs` first, THEN delete `GraphTodoService.cs`, and THEN remove the old contract types. Reorder: **do Task 6 (create interface + update options), Task 7 (write tests), Task 8 (implement GraphPlannerService), Task 9 (NoOp), Task 10 (update module + handler), THEN come back to remove old contract types in Task 12 when deleting old files.**

Revise this task: only add the new types now; remove old ones in Task 12.

- [ ] **Step 1 (revised): Add new Planner types to the end of the file**

Append to `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs` after the last class:

```csharp
internal class GraphPlannerTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class GraphPlannerTaskDetails
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Verify the build still passes**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build -v quiet
```

Expected: Build succeeded.

---

### Task 6: Create IMeetingTaskExporter.cs and update MeetingTasksOptions.cs

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExporter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`

- [ ] **Step 1: Create IMeetingTaskExporter.cs**

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record MeetingTaskExportResult(bool Success, string? ExternalTaskId, string? Error);

public interface IMeetingTaskExporter
{
    Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default);

    Task<MeetingTaskExportResult> ExportTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Replace MeetingTasksOptions.cs**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    [Required]
    public string PlannerPlanId { get; set; } = string.Empty;

    public string? PlannerBucketId { get; set; }

    /// <summary>
    /// Path to the static user-directory JSON file. Relative paths are resolved
    /// against the application base directory.
    /// </summary>
    public string UserDirectoryPath { get; set; } = "meeting-users.json";

    /// <summary>
    /// How many days back the Plaud polling job looks for recordings to ingest.
    /// </summary>
    public int MaxRecordingAgeDays { get; set; } = 7;
}
```

- [ ] **Step 3: Verify build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build -v quiet
```

Expected: Build succeeded. (`MeetingTasksOptions` removed `TodoListName` which `GraphTodoService` references — build will break. That's expected. `GraphTodoService.cs` will be deleted in Task 12. Continue.)

Wait — `GraphTodoService.cs` reads `options.Value.TodoListName` in the constructor. Removing that property breaks the build now. To avoid a broken build during the migration, do this:

- [ ] **Step 2 (revised): Add the new properties but keep TodoListName temporarily**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    [Required]
    public string PlannerPlanId { get; set; } = string.Empty;

    public string? PlannerBucketId { get; set; }

    /// <summary>Kept temporarily while GraphTodoService still compiles. Removed in Task 12.</summary>
    public string TodoListName { get; set; } = "Meeting Actions";

    public string UserDirectoryPath { get; set; } = "meeting-users.json";

    public int MaxRecordingAgeDays { get; set; } = 7;
}
```

`TodoListName` will be removed in Task 12 when `GraphTodoService.cs` is deleted.

- [ ] **Step 3: Verify build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build -v quiet
```

Expected: Build succeeded.

---

### Task 7: Write GraphPlannerServiceTests (all failing)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphPlannerServiceTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Anela.Heblo.Application.Common.Graph;
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GraphPlannerServiceTests
{
    private static (GraphPlannerService Service, RecordingHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string planId = "plan-123",
        string? bucketId = null)
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync("fake-token");

        var recordingHandler = new RecordingHandler(handler);
        var httpClient = new HttpClient(recordingHandler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

        var options = Options.Create(new MeetingTasksOptions
        {
            PlannerPlanId = planId,
            PlannerBucketId = bucketId
        });

        var service = new GraphPlannerService(
            tokenAcquisition.Object,
            factory.Object,
            options,
            NullLogger<GraphPlannerService>.Instance);

        return (service, recordingHandler);
    }

    // ─── ResolveUserIdByEmailAsync ────────────────────────────────────────────

    [Fact]
    public async Task ResolveUserIdByEmailAsync_SingleMatch_ReturnsUserId()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"user-123","displayName":"Ondra Pajgrt"}]}""",
                Encoding.UTF8, "application/json")
        });

        var result = await service.ResolveUserIdByEmailAsync("ondra@anela.cz");

        result.Should().Be("user-123");
    }

    [Fact]
    public async Task ResolveUserIdByEmailAsync_NoMatch_ReturnsNull()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
        });

        var result = await service.ResolveUserIdByEmailAsync("nobody@anela.cz");

        result.Should().BeNull();
    }

    // ─── ExportTaskAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExportTaskAsync_Basic_PostsPlannerTaskAndReturnsId()
    {
        var (service, handler) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"planner-task-1"}""", Encoding.UTF8, "application/json")
        });

        var result = await service.ExportTaskAsync("user-abc", "Write spec", "", null);

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("planner-task-1");
        result.Error.Should().BeNull();

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/planner/tasks");

        handler.RequestBodies[0].Should().Contain("\"planId\":\"plan-123\"");
        handler.RequestBodies[0].Should().Contain("\"title\":\"Write spec\"");
        handler.RequestBodies[0].Should().Contain("\"user-abc\"");
        handler.RequestBodies[0].Should().Contain("\"#microsoft.graph.plannerAssignment\"");
        handler.RequestBodies[0].Should().Contain("\" !\"");
    }

    [Fact]
    public async Task ExportTaskAsync_WithDescription_PatchesDetailsAfterCreate()
    {
        var calls = new Queue<HttpResponseMessage>();

        // 1. POST /planner/tasks
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
        });

        // 2. GET /planner/tasks/t1/details
        var detailsGet = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"description":""}""", Encoding.UTF8, "application/json")
        };
        detailsGet.Headers.ETag = new EntityTagHeaderValue("\"etag-abc\"");
        calls.Enqueue(detailsGet);

        // 3. PATCH /planner/tasks/t1/details
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        var (service, handler) = CreateService(_ => calls.Dequeue());

        var result = await service.ExportTaskAsync("user-abc", "Write spec", "Some description", null);

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("t1");

        handler.Requests.Should().HaveCount(3);

        // GET details
        handler.Requests[1].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/planner/tasks/t1/details");

        // PATCH details
        handler.Requests[2].Method.Should().Be(HttpMethod.Patch);
        handler.Requests[2].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/planner/tasks/t1/details");
        handler.Requests[2].Headers.TryGetValues("If-Match", out var ifMatchValues).Should().BeTrue();
        ifMatchValues!.Should().ContainSingle().Which.Should().Be("\"etag-abc\"");
        handler.RequestBodies[2].Should().Contain("\"description\":\"Some description\"");
    }

    [Fact]
    public async Task ExportTaskAsync_EmptyDescription_SkipsDetailsPatch()
    {
        var (service, handler) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
        });

        var result = await service.ExportTaskAsync("user-abc", "Title", "", null);

        result.Success.Should().BeTrue();
        handler.Requests.Should().HaveCount(1, "empty description must not trigger GET/PATCH details");
    }

    [Fact]
    public async Task ExportTaskAsync_WithBucketId_IncludesBucketIdInPostBody()
    {
        var (service, handler) = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
            },
            bucketId: "bucket-99");

        await service.ExportTaskAsync("user-abc", "Title", "", null);

        handler.RequestBodies[0].Should().Contain("\"bucketId\":\"bucket-99\"");
    }

    [Fact]
    public async Task ExportTaskAsync_NoBucketId_OmitsBucketIdFromPostBody()
    {
        var (service, handler) = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
            });

        await service.ExportTaskAsync("user-abc", "Title", "", null);

        handler.RequestBodies[0].Should().NotContain("bucketId");
    }

    [Fact]
    public async Task ExportTaskAsync_WithDueDate_IncludesDueDateAsIso8601String()
    {
        var (service, handler) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
        });

        var due = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);
        await service.ExportTaskAsync("user-abc", "Title", "", due);

        var expected = due.ToString("o");
        handler.RequestBodies[0].Should().Contain(expected,
            "Planner dueDateTime is a plain ISO-8601 string, not a To Do date-time object");
        // Must NOT use the To Do object form
        handler.RequestBodies[0].Should().NotContain("\"timeZone\"");
    }

    [Fact]
    public async Task ExportTaskAsync_PlannerPostReturns401_ErrorContainsStatusCodeAndBody()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"code":"InvalidAuthenticationToken","message":"Access token is invalid."}}""",
                Encoding.UTF8, "application/json")
        });

        var result = await service.ExportTaskAsync("user-abc", "Title", "", null);

        result.Success.Should().BeFalse();
        result.ExternalTaskId.Should().BeNull();
        result.Error.Should().Contain("401");
        result.Error.Should().Contain("InvalidAuthenticationToken");
    }

    [Fact]
    public async Task ExportTaskAsync_TransportException_ReturnsFailureWithMessage()
    {
        var (service, _) = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.ExportTaskAsync("user-abc", "Title", "", null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("network down");
    }

    // ─── Recording infrastructure ─────────────────────────────────────────────

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }
}
```

- [ ] **Step 2: Run these tests to confirm they fail (GraphPlannerService does not exist yet)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build -v quiet 2>&1 | grep -i error
```

Expected: Build errors mentioning `GraphPlannerService` not found. That's correct — the service is not yet implemented.

---

### Task 8: Implement GraphPlannerService

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphPlannerService.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Common.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class GraphPlannerService : IMeetingTaskExporter
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphPlannerService> _logger;
    private readonly string _planId;
    private readonly string? _bucketId;

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
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
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
                await PatchDescriptionAsync(client, created.Id, description, token, ct);

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
```

- [ ] **Step 2: Run the Planner service tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test --filter "GraphPlannerServiceTests" -v minimal
```

Expected: All `GraphPlannerServiceTests` pass.

---

### Task 9: Create NoOpMeetingTaskExporter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpMeetingTaskExporter.cs`

- [ ] **Step 1: Create the file**

```csharp
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// No-op implementation of IMeetingTaskExporter used when mock authentication is active
/// or BypassJwtValidation is set. Returns null/failure results so the application starts
/// cleanly without Azure AD token acquisition.
/// </summary>
public sealed class NoOpMeetingTaskExporter : IMeetingTaskExporter
{
    private readonly ILogger<NoOpMeetingTaskExporter> _logger;

    public NoOpMeetingTaskExporter(ILogger<NoOpMeetingTaskExporter> logger)
    {
        _logger = logger;
    }

    public Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        _logger.LogWarning("Planner export disabled (mock auth active) — skipping ResolveUserIdByEmail for '{Email}'", email);
        return Task.FromResult<string?>(null);
    }

    public Task<MeetingTaskExportResult> ExportTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Planner export disabled (mock auth active) — skipping ExportTask for user {UserId}", userId);
        return Task.FromResult(new MeetingTaskExportResult(false, null, "Planner export disabled in mock auth mode."));
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build -v quiet
```

Expected: Build succeeded.

---

### Task 10: Update MeetingTasksModule and SubmitToTodoHandler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs`

#### MeetingTasksModule.cs

The module currently registers `IGraphTodoService`. Update it to register `IMeetingTaskExporter` and add `.ValidateDataAnnotations()` for the `[Required]` attribute on `PlannerPlanId`.

- [ ] **Step 1: Replace MeetingTasksModule.cs content**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public static class MeetingTasksModule
{
    public static IServiceCollection AddMeetingTasksModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MeetingTasksOptions>()
            .Bind(configuration.GetSection(MeetingTasksOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>("BypassJwtValidation", false);

        if (!useMockAuth && !bypassJwt)
        {
            // KnowledgeBaseModule only registers "MicrosoftGraph" when SharePoint is configured.
            // Re-register defensively here so GraphPlannerService always finds a client at runtime.
            // AddHttpClient with the same name is idempotent.
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IMeetingTaskExporter, GraphPlannerService>();
        }
        else
        {
            services.AddScoped<IMeetingTaskExporter, NoOpMeetingTaskExporter>();
        }
        services.AddScoped<IMeetingTaskExtractor>(sp =>
            new ClaudeMeetingTaskExtractor(
                sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey),
                sp.GetRequiredService<IMeetingUserDirectory>(),
                sp.GetRequiredService<ILogger<ClaudeMeetingTaskExtractor>>()));
        services.AddScoped<IMeetingSummaryExplainer, ClaudeMeetingSummaryExplainer>();
        services.AddSingleton<IMeetingUserDirectory, MeetingUserDirectory>();
        services.AddScoped<IMeetingAccessGuard, MeetingAccessGuard>();

        // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
        // IMeetingTranscriptRepository is registered in PersistenceModule (subtask 1).
        // MediatR handlers are auto-registered by the MediatR assembly scan in ApplicationModule.
        return services;
    }
}
```

#### SubmitToTodoHandler.cs

- [ ] **Step 2: Replace SubmitToTodoHandler.cs content**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoHandler : IRequestHandler<SubmitToTodoRequest, SubmitToTodoResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingTaskExporter _taskExporter;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<SubmitToTodoHandler> _logger;

    public SubmitToTodoHandler(
        IMeetingTranscriptRepository repository,
        IMeetingTaskExporter taskExporter,
        IMeetingAccessGuard accessGuard,
        ILogger<SubmitToTodoHandler> logger)
    {
        _repository = repository;
        _taskExporter = taskExporter;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<SubmitToTodoResponse> Handle(SubmitToTodoRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("SubmitToTodo: transcript {Id} not found", request.TranscriptId);
            return new SubmitToTodoResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new SubmitToTodoResponse(ErrorCodes.ResourceNotFound);
        }

        var response = new SubmitToTodoResponse();

        var toSubmit = transcript.Tasks
            .Where(t => t.Status == ProposedTaskStatus.Approved && t.ExternalTaskId is null)
            .ToList();

        foreach (var task in toSubmit)
        {
            if (string.IsNullOrWhiteSpace(task.AssigneeEmail))
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Task '{task.Title}' has no resolved user — assign a known user before submitting.");
                continue;
            }

            var userId = await _taskExporter.ResolveUserIdByEmailAsync(task.AssigneeEmail, cancellationToken);
            if (userId is null)
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Could not resolve user '{task.AssigneeEmail}' for task '{task.Title}'.");
                continue;
            }

            var result = await _taskExporter.ExportTaskAsync(
                userId,
                task.Title,
                task.Description,
                task.DueDate,
                cancellationToken);

            if (result.Success && result.ExternalTaskId is not null)
            {
                task.ExternalTaskId = result.ExternalTaskId;
                // Per-task save: bounds blast radius if the process crashes mid-loop.
                // Without this, ExternalTaskId would be lost and the next /submit call would
                // recreate the Planner task — breaking idempotency.
                await _repository.SaveChangesAsync(cancellationToken);
                response.SuccessCount++;
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add($"Failed to export Planner task '{task.Title}' for '{task.AssigneeEmail}': {result.Error}");
            }
        }

        // Status recompute. Tasks are "done" if Rejected (intentionally skipped) or have an ExternalTaskId.
        // Empty/all-rejected collections satisfy allDone vacuously — matches spec FR-3.
        var allDone = transcript.Tasks.All(t =>
            t.Status == ProposedTaskStatus.Rejected || t.ExternalTaskId is not null);
        var hasRejected = transcript.Tasks.Any(t => t.Status == ProposedTaskStatus.Rejected);

        transcript.Status = allDone
            ? (hasRejected ? MeetingTranscriptStatus.PartiallyApproved : MeetingTranscriptStatus.Approved)
            : MeetingTranscriptStatus.PendingReview;
        transcript.ReviewedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Exported {SuccessCount} Planner tasks for transcript {Id}, {FailedCount} failed",
            response.SuccessCount, transcript.Id, response.FailedCount);

        return response;
    }
}
```

- [ ] **Step 3: Verify build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build -v quiet
```

Expected: Build succeeded. (The old `GraphTodoService.cs` may have compile errors now because `MeetingTasksModule` no longer registers `IGraphTodoService`. That's fine — it will be deleted next.)

---

### Task 11: Update SubmitToTodoHandlerTests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs`

The handler tests mock `IGraphTodoService` and call `CreateTodoTaskAsync`/`TodoTaskResult`. All three need updating to `IMeetingTaskExporter`, `ExportTaskAsync`, and `MeetingTaskExportResult`.

- [ ] **Step 1: Replace SubmitToTodoHandlerTests.cs content**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class SubmitToTodoHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repo = new();
    private readonly Mock<IMeetingTaskExporter> _taskExporter = new();
    private readonly Mock<IMeetingAccessGuard> _guard = new();

    public SubmitToTodoHandlerTests()
    {
        _guard.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);
    }

    private SubmitToTodoHandler CreateHandler() => new(
        _repo.Object,
        _taskExporter.Object,
        _guard.Object,
        NullLogger<SubmitToTodoHandler>.Instance);

    private static MeetingTranscript NewTranscript(params ProposedTask[] tasks)
    {
        return new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = "rec-1",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test meeting",
            Summary = "",
            RawTranscript = "",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = tasks.ToList()
        };
    }

    private static ProposedTask NewTask(
        ProposedTaskStatus status,
        string assignee = "Ondra Pajgrt",
        string? assigneeEmail = "ondra@anela.cz",
        string? externalId = null,
        string title = "Do thing")
    {
        return new ProposedTask
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = "desc",
            Assignee = assignee,
            AssigneeEmail = assigneeEmail,
            Status = status,
            ExternalTaskId = externalId,
            IsManuallyAdded = false
        };
    }

    private static MeetingTranscript BuildTranscriptWithApprovedTask(string? assigneeEmail)
    {
        var id = Guid.NewGuid();
        return new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "S",
            Summary = "Sum",
            RawTranscript = "raw",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = id,
                    Title = "Task", Description = "Desc",
                    Assignee = "Andrea Nováková",
                    AssigneeEmail = assigneeEmail,
                    Status = ProposedTaskStatus.Approved,
                    ExternalTaskId = null
                }
            }
        };
    }

    [Fact]
    public async Task Handle_TranscriptNotFound_ReturnsResourceNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = Guid.NewGuid() }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _taskExporter.Verify(t => t.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllTasksApprovedAndSubmitSucceed_TranscriptApproved()
    {
        var t1 = NewTask(ProposedTaskStatus.Approved, assignee: "A", assigneeEmail: "a@anela.cz", title: "T1");
        var t2 = NewTask(ProposedTaskStatus.Approved, assignee: "B", assigneeEmail: "b@anela.cz", title: "T2");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync("a@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-a");
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync("b@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-b");
        _taskExporter.Setup(t => t.ExportTaskAsync("user-a", "T1", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-1", null));
        _taskExporter.Setup(t => t.ExportTaskAsync("user-b", "T2", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-2", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        t1.ExternalTaskId.Should().Be("ext-1");
        t2.ExternalTaskId.Should().Be("ext-2");
        transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
        transcript.ReviewedAt.Should().NotBeNull();

        // Per-task save (2 tasks) + final save = 3 saves total.
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_MixedApprovedAndRejected_ResultsInPartiallyApproved()
    {
        var approved = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "approved@anela.cz", title: "OK");
        var rejected = NewTask(ProposedTaskStatus.Rejected, title: "NO");
        var transcript = NewTranscript(approved, rejected);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync(approved.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _taskExporter.Setup(t => t.ExportTaskAsync("u", "OK", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-ok", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        transcript.Status.Should().Be(MeetingTranscriptStatus.PartiallyApproved);

        _taskExporter.Verify(t => t.ExportTaskAsync(It.IsAny<string>(), "NO", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingTask_StaysPendingReview()
    {
        var approved = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "approved@anela.cz", title: "Yes");
        var pending = NewTask(ProposedTaskStatus.Pending, title: "Maybe");
        var transcript = NewTranscript(approved, pending);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync(approved.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _taskExporter.Setup(t => t.ExportTaskAsync("u", "Yes", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-yes", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
    }

    [Fact]
    public async Task Handle_AlreadySubmittedTask_IsSkipped()
    {
        var alreadyDone = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "done@anela.cz", externalId: "ext-old", title: "Old");
        var newOne = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "new@anela.cz", title: "New");
        var transcript = NewTranscript(alreadyDone, newOne);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync(newOne.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _taskExporter.Setup(t => t.ExportTaskAsync("u", "New", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-new", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        _taskExporter.Verify(t => t.ExportTaskAsync(It.IsAny<string>(), "Old", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
    }

    [Fact]
    public async Task Handle_AssigneeEmailNotResolved_CountsAsFailure()
    {
        var task = NewTask(ProposedTaskStatus.Approved, assignee: "Ghost", assigneeEmail: "ghost@anela.cz", title: "Spook");
        var transcript = NewTranscript(task);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync("ghost@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("ghost@anela.cz").And.Contain("Spook");
        task.ExternalTaskId.Should().BeNull();
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);

        _taskExporter.Verify(t => t.ExportTaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GraphCreateFails_RecordsErrorAndContinues()
    {
        var bad = NewTask(ProposedTaskStatus.Approved, assignee: "A", assigneeEmail: "a@anela.cz", title: "Bad");
        var good = NewTask(ProposedTaskStatus.Approved, assignee: "B", assigneeEmail: "b@anela.cz", title: "Good");
        var transcript = NewTranscript(bad, good);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync("a@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-a");
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync("b@anela.cz", It.IsAny<CancellationToken>())).ReturnsAsync("user-b");
        _taskExporter.Setup(t => t.ExportTaskAsync("user-a", "Bad", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(false, null, "Graph 500"));
        _taskExporter.Setup(t => t.ExportTaskAsync("user-b", "Good", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-good", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Bad");

        bad.ExternalTaskId.Should().BeNull();
        good.ExternalTaskId.Should().Be("ext-good");

        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
    }

    [Fact]
    public async Task Handle_PersistsExternalTaskIdImmediatelyAfterEachSuccess()
    {
        var t1 = NewTask(ProposedTaskStatus.Approved, assignee: "A", assigneeEmail: "a@anela.cz", title: "T1");
        var t2 = NewTask(ProposedTaskStatus.Approved, assignee: "B", assigneeEmail: "b@anela.cz", title: "T2");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _taskExporter.Setup(t => t.ExportTaskAsync("u", "T1", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-1", null));
        _taskExporter.Setup(t => t.ExportTaskAsync("u", "T2", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-2", null));

        var saveCount = 0;
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                saveCount++;
                if (saveCount == 1) t1.ExternalTaskId.Should().Be("ext-1");
                if (saveCount == 2) t2.ExternalTaskId.Should().Be("ext-2");
                if (saveCount == 3)
                {
                    transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
                    transcript.ReviewedAt.Should().NotBeNull();
                }
            });

        var handler = CreateHandler();

        await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_ReRunSafelySkipsAlreadyProcessedTasks()
    {
        var t1 = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "done@anela.cz", externalId: "ext-1", title: "Done");
        var t2 = NewTask(ProposedTaskStatus.Approved, assigneeEmail: "todo@anela.cz", title: "Todo");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _taskExporter.Setup(t => t.ResolveUserIdByEmailAsync(t2.AssigneeEmail!, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _taskExporter.Setup(t => t.ExportTaskAsync("u", "Todo", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-2", null));

        var handler = CreateHandler();

        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        _taskExporter.Verify(t => t.ExportTaskAsync(It.IsAny<string>(), "Done", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsAndReportsTaskWithNoAssigneeEmail()
    {
        var transcript = BuildTranscriptWithApprovedTask(assigneeEmail: null);
        _repo
            .Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var handler = CreateHandler();

        var result = await handler.Handle(
            new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("no resolved user");
        _taskExporter.Verify(
            t => t.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SubmitsTaskWithResolvedAssigneeEmail()
    {
        var transcript = BuildTranscriptWithApprovedTask(assigneeEmail: "andrea@anela.cz");
        _repo
            .Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _taskExporter
            .Setup(t => t.ResolveUserIdByEmailAsync("andrea@anela.cz", It.IsAny<CancellationToken>()))
            .ReturnsAsync("graph-user-id");
        _taskExporter
            .Setup(t => t.ExportTaskAsync(
                "graph-user-id", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingTaskExportResult(true, "ext-1", null));

        var handler = CreateHandler();

        var result = await handler.Handle(
            new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run handler tests to confirm they pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test --filter "SubmitToTodoHandlerTests" -v minimal
```

Expected: All tests pass.

---

### Task 12: Update appsettings.json and delete old files

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` (remove `TodoListName`)
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs` (remove old To Do types)
- Delete: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpGraphTodoService.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`

- [ ] **Step 1: Update MeetingTasks section in appsettings.json**

Find and replace the existing `MeetingTasks` block (currently lines 518–522):
```json
  "MeetingTasks": {
    "TodoListName": "Meeting Actions",
    "UserDirectoryPath": "meeting-users.json",
    "MaxRecordingAgeDays": 7
  },
```
Replace with:
```json
  "MeetingTasks": {
    "PlannerPlanId": "CONFIGURE_IN_USER_SECRETS",
    "UserDirectoryPath": "meeting-users.json",
    "MaxRecordingAgeDays": 7
  },
```

- [ ] **Step 2: Remove TodoListName from MeetingTasksOptions.cs**

Replace the entire file (now that GraphTodoService.cs is about to be deleted, `TodoListName` can go):

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    [Required]
    public string PlannerPlanId { get; set; } = string.Empty;

    public string? PlannerBucketId { get; set; }

    /// <summary>
    /// Path to the static user-directory JSON file. Relative paths are resolved
    /// against the application base directory.
    /// </summary>
    public string UserDirectoryPath { get; set; } = "meeting-users.json";

    /// <summary>
    /// How many days back the Plaud polling job looks for recordings to ingest.
    /// </summary>
    public int MaxRecordingAgeDays { get; set; } = 7;
}
```

- [ ] **Step 3: Remove old To Do types from GraphTodoContracts.cs**

Replace file content (keep only User + new Planner types):

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

internal class GraphUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

internal class GraphUserCollection
{
    [JsonPropertyName("value")]
    public List<GraphUser> Value { get; set; } = [];
}

internal class GraphPlannerTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class GraphPlannerTaskDetails
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Delete the old To Do service files**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja
rm backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs
rm backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs
rm backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpGraphTodoService.cs
rm backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
```

---

### Task 13: Final verification and commit Part B

- [ ] **Step 1: Build + format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet build && dotnet format
```

Expected: Build succeeded, 0 errors, 0 warnings about missing types.

- [ ] **Step 2: Run all MeetingTasks tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test --filter MeetingTasks -v normal
```

Expected: All tests pass. You should see:
- `GraphPlannerServiceTests` — all 9 pass
- `SubmitToTodoHandlerTests` — all 11 pass
- No `GraphTodoServiceTests` (file deleted)

- [ ] **Step 3: Run full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja/backend
dotnet test -v quiet
```

Expected: All tests pass.

- [ ] **Step 4: Commit Part B**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/abuja
git add \
  backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExporter.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphPlannerService.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpMeetingTaskExporter.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs \
  backend/src/Anela.Heblo.API/appsettings.json \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphPlannerServiceTests.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs
git rm \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpGraphTodoService.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
git commit -m "feat(meeting-tasks): migrate task export from To Do to Microsoft Planner

App-only tokens cannot create To Do tasks (application permissions not supported).
Planner's POST /planner/tasks supports Tasks.ReadWrite.All application permission.

- Replace IGraphTodoService / GraphTodoService with IMeetingTaskExporter / GraphPlannerService
- POST /planner/tasks with assignments map; PATCH details for description (ETag required)
- NoOpMeetingTaskExporter for mock-auth mode
- MeetingTasksOptions: PlannerPlanId (required) + optional PlannerBucketId; remove TodoListName
- SubmitToTodoHandler: wired to IMeetingTaskExporter / ExportTaskAsync
- All tests updated to match new interface and return type"
```

---

## Part C — Azure Setup (manual steps for the user)

No code changes. These are the steps you (the developer) must perform in Azure Portal before the Planner export will work in production:

1. **Add application permission** — Azure Portal → App registrations → `8b34be89-f86f-422f-af40-7dbcd30cb66a` → API permissions → Add → Microsoft Graph → Application permissions → check `Tasks.ReadWrite.All`. Confirm `User.Read.All` is already present. Click **Grant admin consent for [tenant]**.

2. **Client secret** — Ensure a valid secret exists. Edit `secrets.json` directly and set `AzureAd:ClientSecret` to the active secret value.

3. **Create a Planner plan** — In Microsoft Teams or Planner (tasks.office.com), create a plan called e.g. "Meeting Actions" inside a Microsoft 365 Group. Every assignee (every user whose tasks will be exported) must be a **member of that group** — Planner only allows assigning tasks to group members.

4. **Get the planId** — From the Planner web URL (`?planId=<id>`) or via Graph:
   ```
   GET https://graph.microsoft.com/v1.0/groups/{groupId}/planner/plans
   ```
   Optionally get a `bucketId` via:
   ```
   GET https://graph.microsoft.com/v1.0/planner/plans/{planId}/buckets
   ```

5. **Configure** — Set `MeetingTasks:PlannerPlanId` (and optionally `MeetingTasks:PlannerBucketId`) in `secrets.json` or environment config. Never commit real IDs to `appsettings.json`.

6. **Verify** — If `POST /planner/tasks` returns 403 after the permission grant, add the app's service principal as a group member in the Microsoft 365 group that owns the plan.
