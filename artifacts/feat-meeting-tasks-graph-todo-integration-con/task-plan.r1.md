# Meeting Tasks — Graph TODO Integration & Controller Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the Meeting Task Validation Checkpoint feature end-to-end: implement a Microsoft Graph TODO client, a MediatR `SubmitToTodo` use case that pushes approved tasks per assignee, the `MeetingTasksOptions` configuration, and a single authenticated `MeetingTasksController` REST surface that exposes the entire review workflow.

**Architecture:** New `GraphTodoService` resolves assignee → Graph user id via filtered `/users` query, then ensures a configured shared TODO list exists per user and POSTs a `todo/task`. The existing `MeetingTasksModule` is extended (not recreated) with options, a defensive `MicrosoftGraph` `HttpClient` registration and the new service. The `SubmitToTodoHandler` iterates approved tasks, persists `ExternalTaskId` per success (per-task `SaveChangesAsync` to bound the duplicate-create blast radius), and recomputes transcript status. A new `MeetingTasksController : BaseApiController` with class-level `[Authorize]` exposes 6 endpoints under `/api/meeting-tasks`. The shared `GraphApiHelpers` are relocated from `Features/KnowledgeBase/Services/` to `Application/Common/Graph/` and promoted to `public` to remove a cross-feature internal reach.

**Tech Stack:** .NET 8, ASP.NET Core MVC controllers, MediatR, EF Core via `IMeetingTranscriptRepository`, `Microsoft.Identity.Web.ITokenAcquisition` (app-only client credentials), `IHttpClientFactory` named client `"MicrosoftGraph"`, `System.Text.Json`. Tests use xUnit + Moq + `FluentAssertions` + custom `HttpMessageHandler` stubs (mirroring `GraphOneDriveServiceTests`).

---

## Authoritative deviations from the spec

The architecture review (`artifacts/.../arch-review.r1.md`) lists 10 spec amendments. The most material — embedded into this plan — are:

1. Use `ErrorCodes.ResourceNotFound`, never the non-existent `ErrorCodes.NotFound`.
2. `MeetingTasksModule` already exists — extend it. Do **not** re-register `IMeetingTranscriptRepository` (lives in `PersistenceModule`) or `PlaudPollingJob` (auto-registered as Scoped by `AddRecurringJobs`).
3. `IGraphTodoService` is registered with `AddScoped<IGraphTodoService, GraphTodoService>()` and consumes `IOptions<MeetingTasksOptions>` via constructor injection — no factory.
4. Register `services.AddHttpClient("MicrosoftGraph")` defensively in the module (`KnowledgeBaseModule`'s registration is conditional on SharePoint config).
5. Use `Microsoft.Identity.Web.ITokenAcquisition`. Its `GetAccessTokenForAppAsync` signature is `(string scope, string? tenant = null, TokenAcquisitionOptions? options = null)` — three args, no `CancellationToken`.
6. Move `GraphApiHelpers` to `Anela.Heblo.Application.Common.Graph`, make `public`. Update `GraphOneDriveService` and `GraphFolderResolver` imports.
7. Persist `task.ExternalTaskId` with `SaveChangesAsync` **immediately after each successful Graph POST**, not only at end of loop, so a mid-batch crash does not lose externally-visible state.
8. OData filter escaping: double `'` → `''` **before** `Uri.EscapeDataString` (OData v4 string-literal rule).
9. Class-level `[Authorize]` only on the controller — no per-endpoint `[Authorize]`.
10. The n8n webhook endpoint and `ApiKeyAuthAttribute` are explicitly out of scope (ingestion is `PlaudPollingJob` from a previous subtask).

## Hard prerequisites (verified before Task 1 below)

- Branch is rebased on `origin/feat/meeting-task-validation-epic` so the epic's domain entities, persistence, existing handlers, and minimal `MeetingTasksModule` are present.
- **Subtask 4 (Write Handlers)** is merged into `feat/meeting-task-validation-epic`. The controller imports types delivered there:
  - `UpdateProposedTaskRequest`, `UpdateProposedTaskResponse`
  - `UpdateProposedTaskStatusRequest`, `UpdateProposedTaskStatusResponse`
  - `AddProposedTaskRequest`, `AddProposedTaskResponse`

  These currently exist only as spec artifacts on `feat-meeting-tasks-write-handlers-edit-approv`. Task 0 below fails the plan early if they are missing — this plan does **not** implement subtask 4.
- Microsoft Graph app registration holds admin-consented application permissions `Tasks.ReadWrite.All` and `User.Read.All`.
- Configuration `MeetingTasks:TodoListName` is present in target environments (default `"Meeting Actions"` is acceptable).

---

## File Structure

**New files:**

```
backend/src/Anela.Heblo.Application/
  Common/Graph/
    GraphApiHelpers.cs                                   # relocated + made public
  Features/MeetingTasks/
    MeetingTasksOptions.cs                               # config record
    Services/
      GraphTodoContracts.cs                              # Graph API DTOs (User, TodoList, TodoTask, *Collection)
      IGraphTodoService.cs                               # interface + TodoTaskResult record
      GraphTodoService.cs                                # HTTP-backed implementation
    UseCases/SubmitToTodo/
      SubmitToTodoRequest.cs
      SubmitToTodoResponse.cs
      SubmitToTodoHandler.cs

backend/src/Anela.Heblo.API/
  Controllers/MeetingTasksController.cs                  # full 6-endpoint REST surface

backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
  GraphTodoServiceTests.cs
  SubmitToTodoHandlerTests.cs
```

**Modified files:**

```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/
  GraphApiHelpers.cs                                     # delete (file moved)
  GraphOneDriveService.cs                                # update using directive
  GraphFolderResolver.cs                                 # update using directive (if present)

backend/src/Anela.Heblo.Application/Features/MeetingTasks/
  MeetingTasksModule.cs                                  # add options, HTTP client, IGraphTodoService

backend/src/Anela.Heblo.API/appsettings.json             # add "MeetingTasks" section
```

`ApplicationModule.cs` does **not** change — `services.AddMeetingTasksModule(configuration);` already exists between `AddPhotobankModule` and `AddSmartsuppModule`.

---

## Task 0: Prerequisites — verify epic base & subtask-4 contracts present

**Files:**
- Read-only verification — no files changed in this task.

- [ ] **Step 1: Verify branch is rebased on the epic**

Run from worktree root:

```bash
git fetch origin feat/meeting-task-validation-epic
git merge-base --is-ancestor origin/feat/meeting-task-validation-epic HEAD && echo "OK" || echo "NEEDS REBASE"
```

Expected: `OK`. If `NEEDS REBASE`, run:

```bash
git rebase origin/feat/meeting-task-validation-epic
```

Resolve conflicts. The current branch was created from `main` before the epic landed and currently contains no MeetingTasks files — rebase is mandatory.

- [ ] **Step 2: Verify epic-supplied files exist locally**

```bash
test -f backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
  && test -f backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs \
  && test -f backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs \
  && echo "EPIC OK"
```

Expected: `EPIC OK`.

- [ ] **Step 3: Verify subtask 4 write-handler contracts exist**

```bash
for f in \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskResponse.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusResponse.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskResponse.cs ; do
  test -f "$f" || { echo "MISSING: $f"; exit 1; }
done && echo "SUBTASK4 OK"
```

Expected: `SUBTASK4 OK`. If any file is missing, **stop the plan** and surface a blocker — subtask 4 must be merged into `feat/meeting-task-validation-epic` first; this plan does not implement those handlers.

- [ ] **Step 4: Sanity baseline build**

```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: build succeeds. Any failure here is a base-state problem, not introduced by this plan.

- [ ] **Step 5: Commit (no code change — empty commit to anchor the plan)**

Skip — no files have been modified.

---

## Task 1: Relocate `GraphApiHelpers` to shared `Common/Graph` namespace

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs` (reduce to KB-specific DTOs only)
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` (update using)
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs` (update using)

Rationale: `GraphApiHelpers` is referenced from a new feature; cross-feature `internal` reach violates vertical-slice boundaries. We extract the **generic** helpers (`GraphBaseUrl`, `GraphScope`, `JsonOptions`, `EncodePath`, `CreateRequest`, `DeserializeAsync`) and leave the KB-specific DTOs (`GraphDriveItem`, `GraphFileFacet`, `GraphDriveItemCollection`) in KnowledgeBase where they're used.

- [ ] **Step 1: Create the relocated helpers file**

Create `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;

namespace Anela.Heblo.Application.Common.Graph;

/// <summary>
/// Generic helpers for talking to Microsoft Graph v1.0 with app-only tokens.
/// Feature-specific DTOs live alongside their consumer (e.g. KnowledgeBase, MeetingTasks).
/// </summary>
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
}
```

- [ ] **Step 2: Trim the old KnowledgeBase file to its DTOs only**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs` with:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

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
```

- [ ] **Step 3: Update `GraphOneDriveService` using directives**

Open `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` and add at the top with the other `using`s:

```csharp
using Anela.Heblo.Application.Common.Graph;
```

The file already references `GraphApiHelpers.*` and `GraphDriveItemCollection` unqualified — the new `using` brings the generic helpers into scope; the DTOs remain in the same namespace as the consumer. No other code in this file changes.

If `GraphScope` is currently a `private const string` in `GraphOneDriveService` (existing pattern), leave it alone — the new public `GraphApiHelpers.GraphScope` is a synonym used by `GraphTodoService`. **Do not refactor** other Graph consumers in this task; that's tech debt out of scope.

- [ ] **Step 4: Update `GraphFolderResolver` using directive**

Open `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs` and add (if not already present):

```csharp
using Anela.Heblo.Application.Common.Graph;
```

- [ ] **Step 5: Build to verify the move compiles**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo --verbosity minimal
```

Expected: build succeeds, zero warnings about `GraphApiHelpers`.

- [ ] **Step 6: Run the existing KnowledgeBase Graph tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphOneDriveServiceTests" --nologo --verbosity minimal
```

Expected: all green. This confirms the relocation did not break existing consumers.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs
git commit -m "refactor: relocate GraphApiHelpers to Application.Common.Graph"
```

---

## Task 2: `MeetingTasksOptions` configuration class

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`

- [ ] **Step 1: Create the options class**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    /// <summary>
    /// Display name of the per-user shared Microsoft TODO list approved tasks are pushed into.
    /// Created on the user's account on first submission if it does not exist.
    /// </summary>
    public string TodoListName { get; set; } = "Meeting Actions";
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo --verbosity minimal
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs
git commit -m "feat: add MeetingTasksOptions for TodoListName configuration"
```

---

## Task 3: Graph TODO contracts (DTOs)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs`

- [ ] **Step 1: Create the contracts file**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs`:

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

internal class GraphTodoList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

internal class GraphTodoListCollection
{
    [JsonPropertyName("value")]
    public List<GraphTodoList> Value { get; set; } = [];
}

internal class GraphTodoTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo --verbosity minimal
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs
git commit -m "feat: add Graph TODO DTOs for user/list/task responses"
```

---

## Task 4: `IGraphTodoService` interface + `TodoTaskResult` record

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs`

- [ ] **Step 1: Create the interface and result record**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Result of a Graph TODO task creation attempt. Non-throwing: failure is encoded in <c>Success=false</c>
/// so the caller (<c>SubmitToTodoHandler</c>) can record per-task errors without aborting the batch.
/// </summary>
public record TodoTaskResult(bool Success, string? ExternalTaskId, string? Error);

public interface IGraphTodoService
{
    /// <summary>
    /// Resolves a free-text assignee display name to a Microsoft Graph user id via
    /// <c>/users?$filter=displayName eq '...'</c>. Returns <c>null</c> when no user matches.
    /// Returns the first match if multiple users share the display name.
    /// Network/HTTP/deserialisation failures are logged and surfaced as <c>null</c>.
    /// </summary>
    Task<string?> ResolveUserIdAsync(string assigneeName, CancellationToken ct = default);

    /// <summary>
    /// Creates a Microsoft TODO task in the configured list under the given user account.
    /// If the configured list does not exist on the user, it is created first.
    /// Never throws — failures are encoded in the returned <see cref="TodoTaskResult"/>.
    /// </summary>
    Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo --verbosity minimal
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs
git commit -m "feat: add IGraphTodoService interface and TodoTaskResult"
```

---

## Task 5: `GraphTodoService.ResolveUserIdAsync` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GraphTodoServiceTests
{
    private static (GraphTodoService Service, RecordingHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string todoListName = "Meeting Actions")
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync("fake-token");

        var recordingHandler = new RecordingHandler(handler);
        var httpClient = new HttpClient(recordingHandler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

        var options = Options.Create(new MeetingTasksOptions { TodoListName = todoListName });

        var service = new GraphTodoService(
            tokenAcquisition.Object,
            factory.Object,
            options,
            NullLogger<GraphTodoService>.Instance);

        return (service, recordingHandler);
    }

    [Fact]
    public async Task ResolveUserIdAsync_SingleMatch_ReturnsUserId()
    {
        // Arrange
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"user-123","displayName":"Ondra Pajgrt"}]}""",
                Encoding.UTF8, "application/json")
        });

        // Act
        var result = await service.ResolveUserIdAsync("Ondra Pajgrt");

        // Assert
        result.Should().Be("user-123");
    }

    [Fact]
    public async Task ResolveUserIdAsync_NoMatch_ReturnsNull()
    {
        // Arrange
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
        });

        // Act
        var result = await service.ResolveUserIdAsync("Nobody");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_MultipleMatches_ReturnsFirst()
    {
        // Arrange
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"a","displayName":"John"},{"id":"b","displayName":"John"}]}""",
                Encoding.UTF8, "application/json")
        });

        // Act
        var result = await service.ResolveUserIdAsync("John");

        // Assert
        result.Should().Be("a");
    }

    [Fact]
    public async Task ResolveUserIdAsync_HttpFailure_ReturnsNull()
    {
        // Arrange
        var (service, _) = CreateService(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Act
        var result = await service.ResolveUserIdAsync("Anyone");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_ExceptionInTransport_ReturnsNull()
    {
        // Arrange
        var (service, _) = CreateService(_ => throw new HttpRequestException("boom"));

        // Act
        var result = await service.ResolveUserIdAsync("Anyone");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_DisplayNameWithSingleQuote_DoublesQuoteBeforeEscape()
    {
        // Arrange — capture the outgoing URL and assert OData escape rules.
        string? capturedUrl = null;
        var (service, _) = CreateService(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"x","displayName":"O'Brien"}]}""",
                    Encoding.UTF8, "application/json")
            };
        });

        // Act
        var result = await service.ResolveUserIdAsync("O'Brien");

        // Assert
        result.Should().Be("x");
        // Single quote MUST be doubled per OData v4, then percent-encoded by Uri.EscapeDataString.
        // %27 = ' (single quote). Doubled becomes %27%27.
        capturedUrl.Should().Contain("displayName%20eq%20%27O%27%27Brien%27");
    }

    [Fact]
    public async Task ResolveUserIdAsync_UsesAppToken_AndCallsGraphUsersEndpoint()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var (service, _) = CreateService(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
            };
        });

        // Act
        await service.ResolveUserIdAsync("Anyone");

        // Assert
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsoluteUri.Should().StartWith("https://graph.microsoft.com/v1.0/users?");
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization!.Parameter.Should().Be("fake-token");
    }

    /// <summary>
    /// Test message handler that delegates each call to a function and records every request
    /// for assertion (URL, method, body).
    /// </summary>
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
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (class does not exist yet)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests" --nologo --verbosity minimal
```

Expected: build error — `GraphTodoService` not defined.

- [ ] **Step 3: Implement `GraphTodoService` (resolve method only)**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`:

```csharp
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

    public async Task<string?> ResolveUserIdAsync(string assigneeName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assigneeName))
            return null;

        try
        {
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            // OData v4 string-literal rule: single quotes inside the literal are doubled,
            // then the whole literal is URL-encoded. "O'Brien" → "O''Brien" → "O%27%27Brien".
            var escaped = Uri.EscapeDataString(assigneeName.Replace("'", "''"));
            var url = $"{GraphApiHelpers.GraphBaseUrl}/users?$filter=displayName%20eq%20%27{escaped}%27&$select=id,displayName";

            var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Graph user lookup for '{Name}' returned {Status}", assigneeName, response.StatusCode);
                return null;
            }

            var result = await GraphApiHelpers.DeserializeAsync<GraphUserCollection>(response, ct);

            if (result.Value.Count == 0)
                return null;

            if (result.Value.Count > 1)
                _logger.LogInformation(
                    "Graph user lookup for '{Name}' matched {Count} users; returning first id",
                    assigneeName, result.Value.Count);

            return result.Value[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Graph user id for '{Name}'", assigneeName);
            return null;
        }
    }

    public Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("Implemented in Task 6.");
    }
}
```

- [ ] **Step 4: Run resolve tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests.ResolveUserIdAsync" --nologo --verbosity minimal
```

Expected: all 7 `ResolveUserIdAsync*` tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
git commit -m "feat: GraphTodoService.ResolveUserIdAsync with OData escape"
```

---

## Task 6: `GraphTodoService.CreateTodoTaskAsync` (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`

The method must:
1. `GET /users/{userId}/todo/lists` and find an entry whose `displayName` matches the configured list (case-insensitive).
2. If absent, `POST /users/{userId}/todo/lists` body `{ "displayName": "<TodoListName>" }` and capture the id.
3. `POST /users/{userId}/todo/lists/{listId}/tasks` with body including `title`, `body { contentType:"text", content }`, optional `dueDateTime { dateTime, timeZone:"UTC" }`.
4. Return `TodoTaskResult(true, id, null)` on success; `TodoTaskResult(false, null, message)` otherwise. Never throws.

- [ ] **Step 1: Add tests for `CreateTodoTaskAsync`**

Append the following test methods to `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs` inside the `GraphTodoServiceTests` class (before the `RecordingHandler` nested class):

```csharp
    [Fact]
    public async Task CreateTodoTaskAsync_ExistingList_PostsTaskAndReturnsId()
    {
        // Arrange — two HTTP calls expected: GET lists, POST task.
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"list-1","displayName":"Meeting Actions"}]}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"task-42","title":"Write spec"}""", Encoding.UTF8, "application/json")
        });

        var (service, handler) = CreateService(_ => calls.Dequeue());

        // Act
        var result = await service.CreateTodoTaskAsync(
            userId: "user-1",
            title: "Write spec",
            description: "Draft RFC",
            dueDate: new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc));

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("task-42");
        result.Error.Should().BeNull();

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-1/todo/lists");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-1/todo/lists/list-1/tasks");
        handler.RequestBodies[1].Should().Contain("\"title\":\"Write spec\"");
        handler.RequestBodies[1].Should().Contain("\"contentType\":\"text\"");
        handler.RequestBodies[1].Should().Contain("\"content\":\"Draft RFC\"");
        handler.RequestBodies[1].Should().Contain("\"dueDateTime\"");
        handler.RequestBodies[1].Should().Contain("\"timeZone\":\"UTC\"");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_MissingList_CreatesListThenTask()
    {
        // Arrange — three HTTP calls expected: GET lists (empty), POST list, POST task.
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                """{"id":"list-new","displayName":"Meeting Actions"}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"task-99","title":"Do X"}""", Encoding.UTF8, "application/json")
        });

        var (service, handler) = CreateService(_ => calls.Dequeue());

        // Act
        var result = await service.CreateTodoTaskAsync(
            userId: "user-2",
            title: "Do X",
            description: "",
            dueDate: null);

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("task-99");

        handler.Requests.Should().HaveCount(3);
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-2/todo/lists");
        handler.RequestBodies[1].Should().Contain("\"displayName\":\"Meeting Actions\"");

        handler.Requests[2].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-2/todo/lists/list-new/tasks");
        // dueDateTime must be omitted when dueDate is null.
        handler.RequestBodies[2].Should().NotContain("dueDateTime");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_ListLookupCaseInsensitive_MatchesByDisplayName()
    {
        // Arrange
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"list-1","displayName":"meeting ACTIONS"}]}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"task-1","title":"t"}""", Encoding.UTF8, "application/json")
        });

        var (service, _) = CreateService(_ => calls.Dequeue());

        // Act
        var result = await service.CreateTodoTaskAsync("u", "t", "d", null);

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("task-1");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_GraphReturnsError_ReturnsFailureResultWithMessage()
    {
        // Arrange — GET lists succeeds, POST task fails with 500.
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"list-1","displayName":"Meeting Actions"}]}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("graph went boom", Encoding.UTF8, "text/plain")
        });

        var (service, _) = CreateService(_ => calls.Dequeue());

        // Act
        var result = await service.CreateTodoTaskAsync("u", "t", "d", null);

        // Assert
        result.Success.Should().BeFalse();
        result.ExternalTaskId.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateTodoTaskAsync_TransportException_ReturnsFailureResultWithMessage()
    {
        // Arrange
        var (service, _) = CreateService(_ => throw new HttpRequestException("network down"));

        // Act
        var result = await service.CreateTodoTaskAsync("u", "t", "d", null);

        // Assert
        result.Success.Should().BeFalse();
        result.ExternalTaskId.Should().BeNull();
        result.Error.Should().Be("network down");
    }
```

- [ ] **Step 2: Run tests to verify they fail (`NotImplementedException`)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests.CreateTodoTaskAsync" --nologo --verbosity minimal
```

Expected: failures because `CreateTodoTaskAsync` throws.

- [ ] **Step 3: Implement `CreateTodoTaskAsync`**

Replace the placeholder `CreateTodoTaskAsync` in `GraphTodoService` with:

```csharp
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
            var request = GraphApiHelpers.CreateRequest(HttpMethod.Post, taskUrl, token);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

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
        getResponse.EnsureSuccessStatusCode();

        var lists = await GraphApiHelpers.DeserializeAsync<GraphTodoListCollection>(getResponse, ct);
        var existing = lists.Value.FirstOrDefault(
            l => string.Equals(l.DisplayName, _todoListName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.Id;

        var createRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, listsUrl, token);
        var createBody = JsonSerializer.Serialize(new Dictionary<string, string> { ["displayName"] = _todoListName });
        createRequest.Content = new StringContent(createBody, Encoding.UTF8, "application/json");

        var createResponse = await client.SendAsync(createRequest, ct);
        createResponse.EnsureSuccessStatusCode();

        var created = await GraphApiHelpers.DeserializeAsync<GraphTodoList>(createResponse, ct);
        return created.Id;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);
```

- [ ] **Step 4: Run create-task tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests.CreateTodoTaskAsync" --nologo --verbosity minimal
```

Expected: all 5 `CreateTodoTaskAsync*` tests pass.

- [ ] **Step 5: Run the full Graph TODO test class for regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests" --nologo --verbosity minimal
```

Expected: all 12 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
git commit -m "feat: GraphTodoService.CreateTodoTaskAsync with list autocreate"
```

---

## Task 7: `SubmitToTodoRequest` / `SubmitToTodoResponse`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoResponse.cs`

- [ ] **Step 1: Create the request**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoRequest : IRequest<SubmitToTodoResponse>
{
    public Guid TranscriptId { get; set; }
}
```

- [ ] **Step 2: Create the response**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoResponse : BaseResponse
{
    public SubmitToTodoResponse() { }
    public SubmitToTodoResponse(ErrorCodes errorCode) : base(errorCode) { }

    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo --verbosity minimal
```

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo
git commit -m "feat: SubmitToTodo MediatR contracts"
```

---

## Task 8: `SubmitToTodoHandler` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs`

Behaviour (consolidated from spec FR-3 + arch-review Decisions 3):

1. Load transcript via repo `GetByIdAsync`. Null → `ResourceNotFound`.
2. Select `Status == Approved && ExternalTaskId == null` tasks.
3. For each: `ResolveUserIdAsync(Assignee)`. Null → append error, `FailedCount++`.
4. Otherwise call `CreateTodoTaskAsync`. On success → set `task.ExternalTaskId`, **call `SaveChangesAsync` immediately**, `SuccessCount++`. On failure → append error, `FailedCount++`.
5. After loop, recompute status:
   - `allDone = Tasks.All(t => t.Status == Rejected || t.ExternalTaskId != null)`
   - `hasRejected = Tasks.Any(t => t.Status == Rejected)`
   - `Status = allDone ? (hasRejected ? PartiallyApproved : Approved) : PendingReview`
6. Set `ReviewedAt = DateTime.UtcNow`. Final `SaveChangesAsync`.
7. Log info summary.

Note: re-submission idempotency requires the per-task save in step 4; if the process crashes between the Graph POST and `SaveChangesAsync`, the next `/submit` would re-create the Graph task. Per-task save bounds this to one task.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs`:

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
    private readonly Mock<IGraphTodoService> _graph = new();

    private SubmitToTodoHandler CreateHandler() => new(
        _repo.Object,
        _graph.Object,
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
        string? externalId = null,
        string title = "Do thing")
    {
        return new ProposedTask
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = "desc",
            Assignee = assignee,
            Status = status,
            ExternalTaskId = externalId,
            IsManuallyAdded = false
        };
    }

    [Fact]
    public async Task Handle_TranscriptNotFound_ReturnsResourceNotFound()
    {
        // Arrange
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _graph.Verify(g => g.ResolveUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllTasksApprovedAndSubmitSucceed_TranscriptApproved()
    {
        // Arrange
        var t1 = NewTask(ProposedTaskStatus.Approved, assignee: "A", title: "T1");
        var t2 = NewTask(ProposedTaskStatus.Approved, assignee: "B", title: "T2");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync("user-a");
        _graph.Setup(g => g.ResolveUserIdAsync("B", It.IsAny<CancellationToken>())).ReturnsAsync("user-b");
        _graph.Setup(g => g.CreateTodoTaskAsync("user-a", "T1", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));
        _graph.Setup(g => g.CreateTodoTaskAsync("user-b", "T2", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-2", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        t1.ExternalTaskId.Should().Be("ext-1");
        t2.ExternalTaskId.Should().Be("ext-2");
        transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
        transcript.ReviewedAt.Should().NotBeNull();

        // Per-task save (Decision 3) + final save = 3 saves total.
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_MixedApprovedAndRejected_ResultsInPartiallyApproved()
    {
        // Arrange
        var approved = NewTask(ProposedTaskStatus.Approved, title: "OK");
        var rejected = NewTask(ProposedTaskStatus.Rejected, title: "NO");
        var transcript = NewTranscript(approved, rejected);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync(approved.Assignee, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "OK", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-ok", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        transcript.Status.Should().Be(MeetingTranscriptStatus.PartiallyApproved);

        // Rejected task must not be submitted.
        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), "NO", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingTask_StaysPendingReview()
    {
        // Arrange — one approved, one pending. Pending blocks the "allDone" condition.
        var approved = NewTask(ProposedTaskStatus.Approved, title: "Yes");
        var pending = NewTask(ProposedTaskStatus.Pending, title: "Maybe");
        var transcript = NewTranscript(approved, pending);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync(approved.Assignee, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "Yes", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-yes", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(1);
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
    }

    [Fact]
    public async Task Handle_AlreadySubmittedTask_IsSkipped()
    {
        // Arrange
        var alreadyDone = NewTask(ProposedTaskStatus.Approved, externalId: "ext-old", title: "Old");
        var newOne = NewTask(ProposedTaskStatus.Approved, title: "New");
        var transcript = NewTranscript(alreadyDone, newOne);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync(newOne.Assignee, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "New", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-new", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(1);
        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), "Old", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
    }

    [Fact]
    public async Task Handle_AssigneeNotResolved_CountsAsFailure()
    {
        // Arrange
        var task = NewTask(ProposedTaskStatus.Approved, assignee: "Ghost", title: "Spook");
        var transcript = NewTranscript(task);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync("Ghost", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Ghost").And.Contain("Spook");
        task.ExternalTaskId.Should().BeNull();
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);

        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GraphCreateFails_RecordsErrorAndContinues()
    {
        // Arrange — first task fails, second succeeds. Batch must not abort.
        var bad = NewTask(ProposedTaskStatus.Approved, assignee: "A", title: "Bad");
        var good = NewTask(ProposedTaskStatus.Approved, assignee: "B", title: "Good");
        var transcript = NewTranscript(bad, good);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync("user-a");
        _graph.Setup(g => g.ResolveUserIdAsync("B", It.IsAny<CancellationToken>())).ReturnsAsync("user-b");
        _graph.Setup(g => g.CreateTodoTaskAsync("user-a", "Bad", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(false, null, "Graph 500"));
        _graph.Setup(g => g.CreateTodoTaskAsync("user-b", "Good", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-good", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Bad");

        bad.ExternalTaskId.Should().BeNull();
        good.ExternalTaskId.Should().Be("ext-good");

        // Not all non-rejected tasks completed → status stays PendingReview.
        transcript.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
    }

    [Fact]
    public async Task Handle_PersistsExternalTaskIdImmediatelyAfterEachSuccess()
    {
        // Arrange — Decision 3: per-task SaveChangesAsync between Graph calls.
        var t1 = NewTask(ProposedTaskStatus.Approved, assignee: "A", title: "T1");
        var t2 = NewTask(ProposedTaskStatus.Approved, assignee: "B", title: "T2");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "T1", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "T2", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-2", null));

        var sequence = new MockSequence();
        _repo.InSequence(sequence)
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => t1.ExternalTaskId.Should().Be("ext-1"));
        _repo.InSequence(sequence)
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => t2.ExternalTaskId.Should().Be("ext-2"));
        // Final save after status recompute.
        _repo.InSequence(sequence)
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                transcript.Status.Should().Be(MeetingTranscriptStatus.Approved);
                transcript.ReviewedAt.Should().NotBeNull();
            });

        var handler = CreateHandler();

        // Act
        await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_ReRunSafelySkipsAlreadyProcessedTasks()
    {
        // Arrange — first call processed t1; rerunning must not call CreateTodoTaskAsync for t1 again.
        var t1 = NewTask(ProposedTaskStatus.Approved, externalId: "ext-1", title: "Done");
        var t2 = NewTask(ProposedTaskStatus.Approved, title: "Todo");
        var transcript = NewTranscript(t1, t2);

        _repo.Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);
        _graph.Setup(g => g.ResolveUserIdAsync(t2.Assignee, It.IsAny<CancellationToken>())).ReturnsAsync("u");
        _graph.Setup(g => g.CreateTodoTaskAsync("u", "Todo", "desc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-2", null));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(1);
        _graph.Verify(g => g.CreateTodoTaskAsync(It.IsAny<string>(), "Done", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (handler not implemented)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitToTodoHandlerTests" --nologo --verbosity minimal
```

Expected: build error — `SubmitToTodoHandler` not defined.

- [ ] **Step 3: Implement the handler**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs`:

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
        if (transcript is null)
        {
            _logger.LogWarning("SubmitToTodo: transcript {Id} not found", request.TranscriptId);
            return new SubmitToTodoResponse(ErrorCodes.ResourceNotFound);
        }

        var response = new SubmitToTodoResponse();

        var toSubmit = transcript.Tasks
            .Where(t => t.Status == ProposedTaskStatus.Approved && t.ExternalTaskId is null)
            .ToList();

        foreach (var task in toSubmit)
        {
            var userId = await _todoService.ResolveUserIdAsync(task.Assignee, cancellationToken);
            if (userId is null)
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Could not resolve assignee '{task.Assignee}' for task '{task.Title}'.");
                continue;
            }

            var result = await _todoService.CreateTodoTaskAsync(
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
                // recreate the Graph task — breaking idempotency.
                await _repository.SaveChangesAsync(cancellationToken);
                response.SuccessCount++;
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Failed to create TODO task '{task.Title}' for '{task.Assignee}': {result.Error}");
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
            "Submitted {SuccessCount} tasks to TODO for transcript {Id}, {FailedCount} failed",
            response.SuccessCount, transcript.Id, response.FailedCount);

        return response;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitToTodoHandlerTests" --nologo --verbosity minimal
```

Expected: all 9 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs
git commit -m "feat: SubmitToTodoHandler orchestrating per-task Graph submission"
```

---

## Task 9: Extend `MeetingTasksModule` with options, HTTP client, and `IGraphTodoService`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`

- [ ] **Step 1: Replace the module body**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` with:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public static class MeetingTasksModule
{
    public static IServiceCollection AddMeetingTasksModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MeetingTasksOptions>()
            .Bind(configuration.GetSection(MeetingTasksOptions.SectionName))
            .ValidateOnStart();

        // KnowledgeBaseModule only registers "MicrosoftGraph" when SharePoint is configured.
        // Re-register defensively here so GraphTodoService always finds a client at runtime.
        // AddHttpClient with the same name is idempotent.
        services.AddHttpClient("MicrosoftGraph");

        services.AddScoped<IGraphTodoService, GraphTodoService>();
        services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();

        // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
        // IMeetingTranscriptRepository is registered in PersistenceModule (subtask 1).
        // MediatR handlers (Ingest, GetList, GetDetail, Update*, Add*, SubmitToTodo) are
        // auto-registered by the MediatR assembly scan in ApplicationModule.
        return services;
    }
}
```

- [ ] **Step 2: Build the API to verify wiring**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo --verbosity minimal
```

Expected: success.

- [ ] **Step 3: Smoke-run the test suite for the feature**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.MeetingTasks" --nologo --verbosity minimal
```

Expected: all meeting-tasks tests still green (no regressions).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs
git commit -m "feat: wire MeetingTasksOptions and IGraphTodoService in module"
```

---

## Task 10: `MeetingTasksController` — all 6 REST endpoints

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`

Endpoints (FR-6):

| Method | Route                                                        | Body                                | Response |
|--------|--------------------------------------------------------------|-------------------------------------|----------|
| GET    | `/api/meeting-tasks`                                         | (query)                             | `GetTranscriptListResponse` |
| GET    | `/api/meeting-tasks/{id:guid}`                               | —                                   | `GetTranscriptDetailResponse` |
| PUT    | `/api/meeting-tasks/{transcriptId:guid}/tasks/{taskId:guid}` | `UpdateProposedTaskRequest`         | `UpdateProposedTaskResponse` |
| PUT    | `/api/meeting-tasks/{transcriptId:guid}/tasks/{taskId:guid}/status` | `UpdateProposedTaskStatusRequest` | `UpdateProposedTaskStatusResponse` |
| POST   | `/api/meeting-tasks/{transcriptId:guid}/tasks`               | `AddProposedTaskRequest`            | `AddProposedTaskResponse` |
| POST   | `/api/meeting-tasks/{transcriptId:guid}/submit`              | —                                   | `SubmitToTodoResponse` |

Route-bound ids (`TranscriptId`, `TaskId`) are assigned onto the body before dispatch to MediatR. The PUT routes assume subtask 4 named the request property `TaskId` and `TranscriptId`; if the actual property names differ, adjust the assignment to match.

- [ ] **Step 1: Inspect subtask-4 request types to confirm property names**

```bash
grep -nE 'public Guid [A-Z][A-Za-z]*Id' \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusRequest.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs
```

Confirm both `TranscriptId` and `TaskId` are settable `Guid` properties on the Update requests. If subtask 4 used different names (e.g., `Id` instead of `TaskId`), the controller's route binding code in Step 2 must be updated accordingly. The names assumed below are the canonical ones from spec FR-6.

- [ ] **Step 2: Create the controller**

Create `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`:

```csharp
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

[Authorize]
[ApiController]
[Route("api/meeting-tasks")]
public sealed class MeetingTasksController : BaseApiController
{
    private readonly IMediator _mediator;

    public MeetingTasksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetTranscriptListResponse>> List(
        [FromQuery] GetTranscriptListRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetTranscriptDetailResponse>> Detail(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetTranscriptDetailRequest { Id = id }, ct);
        return HandleResponse(result);
    }

    [HttpPut("{transcriptId:guid}/tasks/{taskId:guid}")]
    public async Task<ActionResult<UpdateProposedTaskResponse>> UpdateTask(
        Guid transcriptId,
        Guid taskId,
        [FromBody] UpdateProposedTaskRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        request.TaskId = taskId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPut("{transcriptId:guid}/tasks/{taskId:guid}/status")]
    public async Task<ActionResult<UpdateProposedTaskStatusResponse>> UpdateTaskStatus(
        Guid transcriptId,
        Guid taskId,
        [FromBody] UpdateProposedTaskStatusRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        request.TaskId = taskId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("{transcriptId:guid}/tasks")]
    public async Task<ActionResult<AddProposedTaskResponse>> AddTask(
        Guid transcriptId,
        [FromBody] AddProposedTaskRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("{transcriptId:guid}/submit")]
    public async Task<ActionResult<SubmitToTodoResponse>> Submit(
        Guid transcriptId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SubmitToTodoRequest { TranscriptId = transcriptId }, ct);
        return HandleResponse(result);
    }
}
```

- [ ] **Step 3: Build the API**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo --verbosity minimal
```

Expected: success. If a compile error references `TranscriptId` or `TaskId` not being settable, the subtask-4 request types use different property names — adjust assignments to match the actual names. **Do not silently rename or add properties to subtask-4 types.**

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs
git commit -m "feat: MeetingTasksController with 6 authenticated endpoints"
```

---

## Task 11: `appsettings.json` configuration entry

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add the `MeetingTasks` section**

Open `backend/src/Anela.Heblo.API/appsettings.json` and add the following top-level section (alphabetical placement among other feature sections is fine — pick a position consistent with neighbouring keys):

```json
  "MeetingTasks": {
    "TodoListName": "Meeting Actions"
  },
```

Place it near other feature sections such as `"Marketing"`, `"Smartsupp"`, or `"Photobank"` — keep JSON valid (commas, no trailing comma).

- [ ] **Step 2: Build the API once more**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo --verbosity minimal
```

Expected: success. If `ValidateOnStart()` complains at startup later, it means the section is missing — confirm the JSON parses.

- [ ] **Step 3: Validate JSON parses correctly**

```bash
python3 -m json.tool backend/src/Anela.Heblo.API/appsettings.json > /dev/null && echo "OK"
```

Expected: `OK`.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "chore: add MeetingTasks.TodoListName default config"
```

---

## Task 12: Final validation pass

**Files:**
- No code changes — verification only.

- [ ] **Step 1: Full backend build**

```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: solution builds clean. Zero warnings introduced by this feature (existing repo warnings unchanged).

- [ ] **Step 2: Formatter clean check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes --verbosity minimal
```

Expected: exit 0 (no diffs). If diffs are reported, run `dotnet format backend/Anela.Heblo.sln`, commit them as `chore: dotnet format`, and re-verify.

- [ ] **Step 3: Run all new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphTodoServiceTests|FullyQualifiedName~SubmitToTodoHandlerTests" \
  --nologo --verbosity minimal
```

Expected: 12 (Graph) + 9 (Handler) = 21 tests pass.

- [ ] **Step 4: Run all MeetingTasks regression tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.MeetingTasks|FullyQualifiedName~MeetingTasks" \
  --nologo --verbosity minimal
```

Expected: all green. This covers the existing `IngestPlaudRecording`, `GetTranscriptList`, `GetTranscriptDetail`, `MeetingTranscriptRepository`, `ClaudeMeetingTaskExtractor` tests plus the two new test classes.

- [ ] **Step 5: Run KnowledgeBase Graph tests for refactor regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBase" --nologo --verbosity minimal
```

Expected: all green. Confirms the `GraphApiHelpers` relocation did not regress consumers.

- [ ] **Step 6: Verify nothing accidentally got into the controller surface**

```bash
grep -nE 'ApiKeyAuth|/webhook' backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs && \
  echo "UNEXPECTED — webhook/api-key surfaces are out of scope" || echo "Controller scope OK"
```

Expected: `Controller scope OK`. The spec explicitly removes the n8n webhook and `ApiKeyAuthAttribute`.

- [ ] **Step 7: Final push (manual, by user)**

This step is left to the operator. Once they're happy with local validation, they push the branch and open a PR targeting `feat/meeting-task-validation-epic` (not `main`), per spec dependency.

---

## Self-Review

**Spec coverage**

| Spec section / requirement | Covered by |
|----------------------------|------------|
| FR-1: Resolve user id via `/users?$filter=displayName eq '...'` | Task 5 (tests + implementation; OData escape via doubled `'` + `Uri.EscapeDataString`) |
| FR-2: Create TODO task in configured list, autocreate list if missing | Task 6 (tests + implementation; `GetOrCreateTodoListAsync`; `dueDateTime` UTC) |
| FR-3: `SubmitToTodoRequest/Response`; per-task processing, status recompute, `ReviewedAt`, summary log, `ResourceNotFound` | Tasks 7 + 8 |
| FR-4: `MeetingTasksModule.AddMeetingTasksModule` — extended (not recreated), no double registrations | Task 9 (per arch-review amendments 2–5) |
| FR-5: `MeetingTasksOptions.TodoListName` default `"Meeting Actions"`, bound from `"MeetingTasks"` section | Tasks 2 + 9 + 11 |
| FR-6: 6-endpoint controller, class-level `[Authorize]`, route ids bound onto body, no webhook/api-key | Task 10 |
| NFR-1 Performance (≤ 30 s for 20 tasks) | Architectural — sequential calls, no retries. Verified manually post-merge. |
| NFR-2 Security (auth on every endpoint; app-only token; OData escape; redacted errors) | Task 10 (`[Authorize]`), Task 5/6 (token via `ITokenAcquisition`), Task 5 (escape), Task 6 (`ex.Message` only) |
| NFR-3 Reliability (single failure doesn't abort, idempotent retry) | Task 8 (`Handle_GraphCreateFails_RecordsErrorAndContinues`, `Handle_ReRunSafelySkipsAlreadyProcessedTasks`) — per-task save (Decision 3) |
| NFR-4 Testability (mocked HTTP, mocked service+repo) | Tasks 5, 6, 8 |
| NFR-5 Build & format | Task 12 |
| Amendment 1 (`ResourceNotFound`) | Task 8 |
| Amendment 2 (extend existing module) | Task 9 |
| Amendment 3 (drop repo/job re-registration) | Task 9 |
| Amendment 4 (`AddScoped<IGraphTodoService, GraphTodoService>` + `IOptions<MeetingTasksOptions>`) | Tasks 5, 9 |
| Amendment 5 (`AddHttpClient("MicrosoftGraph")` defensive) | Task 9 |
| Amendment 6 (`Microsoft.Identity.Web.ITokenAcquisition`, 3-arg signature) | Task 5 (test mock + impl) |
| Amendment 7 (relocate `GraphApiHelpers`) | Task 1 |
| Amendment 8 (per-task `SaveChangesAsync`) | Task 8 |
| Amendment 9 (double `'` → `''` before escape) | Task 5 (`Handle_DisplayNameWithSingleQuote_*` test) |
| Amendment 10 (no per-endpoint `[Authorize]`) | Task 10 |

**Placeholder scan:** No TBDs, no "implement later," every code step shows the actual code. The only conditional is Task 10 Step 1: if subtask-4 property names differ, the engineer adjusts the route-binding lines accordingly — but the property name to assume (`TranscriptId`, `TaskId`) is stated, and the failure mode (compile error) is explicit and easy to act on.

**Type consistency:**
- `TodoTaskResult(bool Success, string? ExternalTaskId, string? Error)` is used identically in Task 4 (interface), Task 6 (impl), and Task 8 (handler tests).
- `IGraphTodoService.ResolveUserIdAsync(string, CancellationToken)` and `CreateTodoTaskAsync(string, string, string, DateTime?, CancellationToken)` signatures match across declaration, implementation, and tests.
- `SubmitToTodoRequest.TranscriptId`, `SubmitToTodoResponse.SuccessCount/FailedCount/Errors` match across Tasks 7, 8, and 10.
- `MeetingTasksOptions.TodoListName` and `SectionName` match between Task 2, Task 5 test, Task 9 binding, and Task 11 appsettings.
- `IMeetingTranscriptRepository.GetByIdAsync(Guid, CancellationToken)` and `SaveChangesAsync(CancellationToken)` match the verified interface on the epic branch.
- `ProposedTask` fields used (`Title`, `Description`, `Assignee`, `Status`, `DueDate`, `ExternalTaskId`) match the verified domain class.
- `MeetingTranscriptStatus` values used (`PendingReview`, `Approved`, `PartiallyApproved`) match the verified enum.

No gaps identified.
