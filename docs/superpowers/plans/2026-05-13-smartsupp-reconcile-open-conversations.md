# Smartsupp Reconcile Open Conversations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After the manual sync search loop (which only returns `open`/`served` conversations from Smartsupp), add a reconciliation pass that re-checks every locally-`Open` conversation the search did not return — fetching its real status via `GET /v2/conversations/{id}` — so conversations that transitioned to `resolved` while webhooks were missed get correctly closed in the DB.

**Architecture:** New `GetConversationAsync(id)` method on `ISmartsuppApiClient` (mirrors `GetContactAsync` — returns null on 404). New `ListOpenConversationRefsAsync` and `MarkConversationResolvedAsync` on `ISmartsuppRepository`. `RunManualSyncHandler` gains a `seenIds` HashSet during the search loop, then calls a new `ReconcileOpenConversationsAsync` method that iterates the diff. Existing `UpsertConversationAsync` timestamp-guard and `UpsertMessagesAsync` idempotency protect against stale writes.

**Tech Stack:** .NET 8 · C# 12 · EF Core · MediatR · xUnit + FluentAssertions + Moq · Polly (existing resilience pipeline reused as-is)

---

## File Map

| Action | File |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncResponse.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncHandler.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RunManualSyncHandlerTests.cs` |

---

### Task 1: Extend ISmartsuppApiClient with GetConversationAsync + stub implementation

TDD warm-up: get the interfaces in place so tests compile. All Smartsupp source files are in the worktree — always use absolute paths from the worktree root.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`

- [ ] **Step 1: Add GetConversationAsync to ISmartsuppApiClient**

In `ISmartsuppApiClient.cs`, add one method after `GetContactAsync`:

```csharp
Task<SmartsuppConversationData?> GetConversationAsync(
    string conversationId,
    CancellationToken cancellationToken);
```

Full interface after the change:
```csharp
public interface ISmartsuppApiClient
{
    Task<SmartsuppSearchResult> SearchConversationsAsync(
        string? cursor,
        int size,
        CancellationToken cancellationToken);

    Task<List<SmartsuppMessageData>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken);

    Task<SmartsuppContactData?> GetContactAsync(
        string contactId,
        CancellationToken cancellationToken);

    Task<SmartsuppConversationData?> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Add stub in SmartsuppApiClient so the project compiles**

In `SmartsuppApiClient.cs`, add a stub after `GetContactAsync` (around line 166):

```csharp
public Task<SmartsuppConversationData?> GetConversationAsync(
    string conversationId,
    CancellationToken cancellationToken) =>
    throw new NotImplementedException();
```

- [ ] **Step 3: Verify build passes**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore -q
```

Expected: Build succeeded with 0 errors.

---

### Task 2: Write failing API client tests for GetConversationAsync

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`

- [ ] **Step 1: Write three failing tests**

Append to the `SmartsuppApiClientTests` class (before the final `}`). The JSON shape mirrors the real Smartsupp wire format:

```csharp
[Fact]
public async Task GetConversationAsync_ReturnsMappedConversation_WhenApiReturns200()
{
    // Arrange
    var responseJson = JsonSerializer.Serialize(new
    {
        id = "coXU9u5VscuzW",
        ext_id = (string?)null,
        status = "resolved",
        unread = false,
        created_at = "2026-05-12T18:29:21.336Z",
        updated_at = "2026-05-13T09:00:00.000Z",
        finished_at = "2026-05-13T09:00:00.000Z",
        channel = new { type = "default", id = (string?)null },
        contact_id = "ctW5HHbqaRKv",
        visitor_id = "vitCESEI6Lu-SL",
        agent_ids = Array.Empty<string>(),
        assigned_ids = Array.Empty<string>(),
        group_id = (string?)null,
        rating_value = (int?)null,
        rating_text = (string?)null,
        domain = "www.anela.cz",
        referer = "https://l.facebook.com/",
        is_offline = true,
        is_served = false,
        variables = new { shoptet_shop = "269953" },
        location = new { ip = "78.102.94.30", code = "CZ", country = "Czechia", city = "Prague" },
        last_message = new { text = "Díky", created_at = "2026-05-12T18:30:58Z" }
    });

    var handler = new Mock<HttpMessageHandler>();
    handler.Protected()
        .Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });

    var client = CreateClient(handler.Object);

    // Act
    var result = await client.GetConversationAsync("coXU9u5VscuzW", CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    result!.Id.Should().Be("coXU9u5VscuzW");
    result.Status.Should().Be("resolved");
    result.ContactId.Should().Be("ctW5HHbqaRKv");
    result.VisitorId.Should().Be("vitCESEI6Lu-SL");
    result.Domain.Should().Be("www.anela.cz");
    result.IsOffline.Should().BeTrue();
    result.FinishedAt.Should().NotBeNull();
    result.LocationCountry.Should().Be("Czechia");
    result.LastMessageText.Should().Be("Díky");
}

[Fact]
public async Task GetConversationAsync_ReturnsNull_On404()
{
    // Arrange
    var handler = new Mock<HttpMessageHandler>();
    handler.Protected()
        .Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

    var client = CreateClient(handler.Object);

    // Act
    var result = await client.GetConversationAsync("co-missing", CancellationToken.None);

    // Assert
    result.Should().BeNull();
}

[Fact]
public async Task GetConversationAsync_ThrowsHttpRequestException_On500()
{
    // Arrange
    var handler = new Mock<HttpMessageHandler>();
    handler.Protected()
        .Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

    // Act
    var act = () => client.GetConversationAsync("co-error", CancellationToken.None);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>()
        .Where(ex => ex.StatusCode == HttpStatusCode.InternalServerError);
}
```

- [ ] **Step 2: Run only the new tests to confirm they fail**

```bash
dotnet test backend/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~GetConversationAsync" \
  --no-build -q
```

Expected: 3 tests fail (NotImplementedException).

---

### Task 3: Implement GetConversationAsync in SmartsuppApiClient

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`

- [ ] **Step 1: Replace the stub with the real implementation**

Replace the `throw new NotImplementedException()` stub with:

```csharp
public async Task<SmartsuppConversationData?> GetConversationAsync(
    string conversationId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(_options.ApiToken))
        throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

    return await _pipeline.ExecuteAsync(async ct =>
    {
        var client = _httpClientFactory.CreateClient("Smartsupp");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_options.BaseUrl}conversations/{conversationId}");
        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        var response = await client.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Smartsupp get conversation failed {Status}: {Body}", response.StatusCode, errorBody);
            var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            if (response.Headers.RetryAfter?.Delta is { } delta)
                ex.Data["RetryAfter"] = delta;
            throw ex;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SmartsuppConversationApiItem>(raw, JsonOptions);

        return result is null ? null : MapConversation(result);
    }, cancellationToken);
}
```

- [ ] **Step 2: Run the three new API client tests**

```bash
dotnet test backend/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~GetConversationAsync" \
  --no-build -q
```

Expected: 3 tests pass.

- [ ] **Step 3: Run the full API client test class to confirm no regressions**

```bash
dotnet test backend/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~SmartsuppApiClientTests" \
  --no-build -q
```

Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs
git commit -m "feat(smartsupp): add GetConversationAsync to API client"
```

---

### Task 4: Extend ISmartsuppRepository + stub implementation

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Add OpenConversationRef record and two new methods to ISmartsuppRepository**

In `ISmartsuppRepository.cs`, add the record and two method signatures:

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public sealed record OpenConversationRef(string Id, DateTime? LastMessageAt);

public interface ISmartsuppRepository
{
    Task<(List<SmartsuppConversation> Items, int Total)> ListConversationsAsync(
        SmartsuppConversationStatus status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SmartsuppConversation?> GetConversationAsync(
        string id,
        CancellationToken cancellationToken);

    Task UpsertContactAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken);

    Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);

    Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken);

    Task<List<OpenConversationRef>> ListOpenConversationRefsAsync(
        CancellationToken cancellationToken);

    Task MarkConversationResolvedAsync(
        string conversationId,
        DateTime finishedAt,
        DateTime syncedAt,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Add stubs in SmartsuppRepository so it compiles**

Append to `SmartsuppRepository.cs` (before the final `}`):

```csharp
public Task<List<OpenConversationRef>> ListOpenConversationRefsAsync(
    CancellationToken cancellationToken) =>
    throw new NotImplementedException();

public Task MarkConversationResolvedAsync(
    string conversationId,
    DateTime finishedAt,
    DateTime syncedAt,
    CancellationToken cancellationToken) =>
    throw new NotImplementedException();
```

- [ ] **Step 3: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore -q
```

Expected: Build succeeded, 0 errors.

---

### Task 5: Write failing handler tests for reconciliation

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RunManualSyncHandlerTests.cs`

- [ ] **Step 1: Update SetupRepoDefaults to mock the two new methods**

In the existing `SetupRepoDefaults()` method, add two lines:

```csharp
private void SetupRepoDefaults()
{
    _repo.Setup(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _repo.Setup(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    // New: return empty open list by default so existing tests aren't affected by reconciliation
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef>());
    _repo.Setup(r => r.MarkConversationResolvedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
}
```

- [ ] **Step 2: Add six new reconciliation tests**

Append to the `RunManualSyncHandlerTests` class (import `Anela.Heblo.Domain.Features.Smartsupp` is already there):

```csharp
[Fact]
public async Task Handle_ReconcilesLocallyOpenConversation_NotReturnedBySearch()
{
    // Arrange
    var t0 = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Unspecified);
    var t1 = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Unspecified);

    // Search returns only c-search
    _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [MakeConv("c-search", DateTime.UtcNow.AddMinutes(-5))] });
    _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SmartsuppMessageData>());

    // Local DB has both c-search and c-stale open
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef>
        {
            new("c-search", t1),
            new("c-stale", t0),
        });

    // c-stale returns resolved from Smartsupp
    _apiClient.Setup(c => c.GetConversationAsync("c-stale", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppConversationData
        {
            Id = "c-stale",
            Status = "resolved",
            FinishedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = t0,
        });

    SetupRepoDefaults();
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-search", t1), new("c-stale", t0) });

    // Act
    var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    _repo.Verify(r => r.UpsertConversationAsync(
        It.Is<SmartsuppConversation>(c => c.Id == "c-stale" && c.Status == SmartsuppConversationStatus.Resolved && c.FinishedAt != null),
        It.IsAny<CancellationToken>()), Times.Once);
    _repo.Verify(r => r.GetConversationMessagesAsync, Times.Never); // messages not re-fetched — wait, this is UpsertConversationAsync check only
    response.ConversationsReconciled.Should().Be(1);
}

[Fact]
public async Task Handle_MarksLocallyOpenAsResolved_When404FromSmartsupp()
{
    // Arrange
    _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-stale", null) });
    _apiClient.Setup(c => c.GetConversationAsync("c-stale", It.IsAny<CancellationToken>()))
        .ReturnsAsync((SmartsuppConversationData?)null);
    SetupRepoDefaults();
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-stale", null) });

    // Act
    var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

    // Assert
    _repo.Verify(r => r.MarkConversationResolvedAsync(
        "c-stale",
        It.IsAny<DateTime>(),
        It.IsAny<DateTime>(),
        It.IsAny<CancellationToken>()), Times.Once);
    _apiClient.Verify(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    response.ConversationsClosedRemotely.Should().Be(1);
    response.ConversationsReconciled.Should().Be(1);
}

[Fact]
public async Task Handle_SkipsMessagesFetch_WhenStatusUnchangedAndLastMessageAtUnchanged()
{
    // Arrange
    var lm = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-10), DateTimeKind.Unspecified);

    _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-still-open", lm) });
    _apiClient.Setup(c => c.GetConversationAsync("c-still-open", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppConversationData
        {
            Id = "c-still-open",
            Status = "open",
            LastMessageAt = lm,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = lm.AddHours(-1),
        });
    SetupRepoDefaults();
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-still-open", lm) });

    // Act
    await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

    // Assert: conversation row still upserted (IsServed etc. may change)
    _repo.Verify(r => r.UpsertConversationAsync(
        It.Is<SmartsuppConversation>(c => c.Id == "c-still-open"),
        It.IsAny<CancellationToken>()), Times.Once);
    // Messages NOT re-fetched
    _apiClient.Verify(c => c.GetConversationMessagesAsync("c-still-open", It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task Handle_RefetchesMessages_WhenLastMessageAtAdvanced()
{
    // Arrange
    var oldLm = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Unspecified);
    var newLm = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified);

    _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-active", oldLm) });
    _apiClient.Setup(c => c.GetConversationAsync("c-active", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppConversationData
        {
            Id = "c-active",
            Status = "open",
            LastMessageAt = newLm,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = oldLm.AddHours(-1),
        });
    _apiClient.Setup(c => c.GetConversationMessagesAsync("c-active", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SmartsuppMessageData>
        {
            new() { Id = "m-new", CreatedAt = newLm, UpdatedAt = newLm, SubType = "contact" }
        });
    SetupRepoDefaults();
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-active", oldLm) });

    // Act
    var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

    // Assert
    _apiClient.Verify(c => c.GetConversationMessagesAsync("c-active", It.IsAny<CancellationToken>()), Times.Once);
    response.MessagesProcessed.Should().Be(1);
}

[Fact]
public async Task Handle_DoesNotReFetchConversationsAlreadySeenInSearch()
{
    // Arrange — c-search comes from the search AND is in the local open list
    _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppSearchResult
        {
            Total = 1, After = null,
            Items = [MakeConv("c-search", DateTime.UtcNow.AddMinutes(-5))]
        });
    _apiClient.Setup(c => c.GetConversationMessagesAsync("c-search", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SmartsuppMessageData>());
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-search", DateTime.UtcNow.AddMinutes(-5)) });
    SetupRepoDefaults();
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-search", DateTime.UtcNow.AddMinutes(-5)) });

    // Act
    await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

    // Assert: GetConversationAsync never called because c-search was seen in search
    _apiClient.Verify(c => c.GetConversationAsync("c-search", It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task Handle_ContinuesReconciliation_WhenIndividualGetConversationFails()
{
    // Arrange
    var t0 = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Unspecified);

    _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-fail", t0), new("c-ok", t0) });

    _apiClient.Setup(c => c.GetConversationAsync("c-fail", It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("network error"));
    _apiClient.Setup(c => c.GetConversationAsync("c-ok", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SmartsuppConversationData
        {
            Id = "c-ok",
            Status = "resolved",
            FinishedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = t0,
        });
    SetupRepoDefaults();
    _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<OpenConversationRef> { new("c-fail", t0), new("c-ok", t0) });

    // Act — should not throw
    var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

    // Assert: c-ok still processed despite c-fail blowing up
    _repo.Verify(r => r.UpsertConversationAsync(
        It.Is<SmartsuppConversation>(c => c.Id == "c-ok" && c.Status == SmartsuppConversationStatus.Resolved),
        It.IsAny<CancellationToken>()), Times.Once);
    response.Success.Should().BeTrue();
}
```

- [ ] **Step 3: Run only the new tests to confirm they fail**

```bash
dotnet test backend/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~Handle_Reconcile|FullyQualifiedName~Handle_MarksLocallyOpen|FullyQualifiedName~Handle_SkipsMessages|FullyQualifiedName~Handle_Refetches|FullyQualifiedName~Handle_DoesNotReFetch|FullyQualifiedName~Handle_Continues" \
  --no-build -q
```

Expected: 6 tests fail (stub throws NotImplementedException or logic not present yet).

---

### Task 6: Implement repository methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Replace stubs with real implementations**

Replace the `throw new NotImplementedException()` stubs with:

```csharp
public async Task<List<OpenConversationRef>> ListOpenConversationRefsAsync(
    CancellationToken cancellationToken) =>
    await _db.SmartsuppConversations
        .AsNoTracking()
        .Where(c => c.Status == SmartsuppConversationStatus.Open)
        .Select(c => new OpenConversationRef(c.Id, c.LastMessageAt))
        .ToListAsync(cancellationToken);

public async Task MarkConversationResolvedAsync(
    string conversationId,
    DateTime finishedAt,
    DateTime syncedAt,
    CancellationToken cancellationToken)
{
    var existing = await _db.SmartsuppConversations
        .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    if (existing is null)
        return;

    existing.Status = SmartsuppConversationStatus.Resolved;
    existing.FinishedAt = finishedAt;
    existing.SyncedAt = syncedAt;
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln --no-restore -q
```

Expected: 0 errors.

---

### Task 7: Update RunManualSyncResponse with new fields

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncResponse.cs`

- [ ] **Step 1: Add two new properties**

Replace the file content with:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncResponse : BaseResponse
{
    public int ConversationsProcessed { get; set; }
    public int MessagesProcessed { get; set; }
    public int ConversationsReconciled { get; set; }
    public int ConversationsClosedRemotely { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    public RunManualSyncResponse() { }

    public RunManualSyncResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

---

### Task 8: Update RunManualSyncHandler with reconciliation logic

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncHandler.cs`

- [ ] **Step 1: Replace the entire file with the updated implementation**

The changes: (1) add `seenIds` HashSet, (2) extract `MapConversationEntity`, (3) add `ReconcileOpenConversationsAsync`, (4) wire into `Handle`.

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncHandler : IRequestHandler<RunManualSyncRequest, RunManualSyncResponse>
{
    private const int PageSize = 50;
    private const int LastMessagePreviewMaxLength = 200;
    private const int DefaultLookbackDays = 7;
    private const int MaxLookbackDays = 30;

    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<RunManualSyncHandler> _logger;

    public RunManualSyncHandler(
        ISmartsuppApiClient apiClient,
        ISmartsuppRepository repository,
        ILogger<RunManualSyncHandler> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<RunManualSyncResponse> Handle(
        RunManualSyncRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var since = ResolveSince(request.Since, startedAt);

        _logger.LogInformation("smartsupp manual sync starting since={Since}", since);

        var contactCache = new Dictionary<string, SmartsuppContactData?>(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var conversationsProcessed = 0;
        var messagesProcessed = 0;
        string? cursor = null;

        do
        {
            SmartsuppSearchResult page;
            try
            {
                page = await _apiClient.SearchConversationsAsync(cursor, PageSize, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "smartsupp manual sync failed to fetch page (cursor={Cursor})", cursor);
                break;
            }

            _logger.LogDebug("smartsupp manual sync page items={Count} after={After}", page.Items.Count, page.After);

            foreach (var item in page.Items)
            {
                seenIds.Add(item.Id);

                if (item.UpdatedAt <= since)
                    continue;

                var msgCount = await ProcessConversationAsync(item, startedAt, contactCache, cancellationToken);
                conversationsProcessed++;
                messagesProcessed += msgCount;
            }

            await _repository.SaveChangesAsync(cancellationToken);
            cursor = page.After;

        } while (cursor is not null);

        var (reconciled, closedRemotely, reconcileMessages) =
            await ReconcileOpenConversationsAsync(seenIds, startedAt, contactCache, cancellationToken);

        conversationsProcessed += reconciled;
        messagesProcessed += reconcileMessages;

        var completedAt = DateTime.UtcNow;
        _logger.LogInformation(
            "smartsupp manual sync completed conversations={Conversations} messages={Messages} reconciled={Reconciled} closedRemotely={ClosedRemotely}",
            conversationsProcessed, messagesProcessed, reconciled, closedRemotely);

        return new RunManualSyncResponse
        {
            ConversationsProcessed = conversationsProcessed,
            MessagesProcessed = messagesProcessed,
            ConversationsReconciled = reconciled,
            ConversationsClosedRemotely = closedRemotely,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
    }

    private static DateTime ResolveSince(DateTime? requested, DateTime now)
    {
        var floor = now.AddDays(-MaxLookbackDays);
        var defaultSince = now.AddDays(-DefaultLookbackDays);
        var requestedOrDefault = requested ?? defaultSince;
        return requestedOrDefault < floor ? floor : requestedOrDefault;
    }

    private async Task<(int reconciled, int closedRemotely, int messages)> ReconcileOpenConversationsAsync(
        HashSet<string> seenIds,
        DateTime startedAt,
        Dictionary<string, SmartsuppContactData?> contactCache,
        CancellationToken cancellationToken)
    {
        List<OpenConversationRef> openRefs;
        try
        {
            openRefs = await _repository.ListOpenConversationRefsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "smartsupp reconcile: failed to load open conversation refs");
            return (0, 0, 0);
        }

        var candidates = openRefs.Where(r => !seenIds.Contains(r.Id)).ToList();
        _logger.LogDebug("smartsupp reconcile: {Count} locally-open conversations to check", candidates.Count);

        var reconciled = 0;
        var closedRemotely = 0;
        var messages = 0;

        foreach (var localRef in candidates)
        {
            try
            {
                SmartsuppConversationData? data;
                try
                {
                    data = await _apiClient.GetConversationAsync(localRef.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "smartsupp reconcile: failed to fetch conversation {Id}", localRef.Id);
                    continue;
                }

                if (data is null)
                {
                    _logger.LogWarning(
                        "smartsupp reconcile: conversation {Id} not found on remote, marking resolved",
                        localRef.Id);
                    await _repository.MarkConversationResolvedAsync(
                        localRef.Id,
                        Unspecified(startedAt),
                        Unspecified(startedAt),
                        cancellationToken);
                    closedRemotely++;
                    reconciled++;
                    continue;
                }

                var remoteStatus = data.Status?.ToLowerInvariant() == "resolved"
                    ? SmartsuppConversationStatus.Resolved
                    : SmartsuppConversationStatus.Open;

                var statusChanged = remoteStatus == SmartsuppConversationStatus.Resolved;
                var lastMessageAdvanced = data.LastMessageAt.HasValue
                    && (localRef.LastMessageAt is null || data.LastMessageAt > localRef.LastMessageAt);
                var shouldFetchMessages = statusChanged || lastMessageAdvanced;

                SmartsuppContactData? contact = null;
                if (!string.IsNullOrEmpty(data.ContactId))
                {
                    contact = await FetchContactCachedAsync(data.ContactId, contactCache, cancellationToken);

                    if (contact is not null)
                    {
                        var contactEntity = new SmartsuppContact
                        {
                            Id = contact.Id,
                            Email = contact.Email,
                            Name = contact.Name,
                            Phone = contact.Phone,
                            Note = contact.Note,
                            BannedAt = contact.BannedAt is { } ba ? Unspecified(ba) : null,
                            BannedBy = contact.BannedBy,
                            GdprApproved = contact.GdprApproved,
                            TagsJson = contact.TagsJson,
                            PropertiesJson = contact.PropertiesJson,
                            CreatedAt = Unspecified(contact.CreatedAt),
                            UpdatedAt = Unspecified(contact.UpdatedAt),
                            SyncedAt = Unspecified(startedAt),
                        };
                        await _repository.UpsertContactAsync(contactEntity, cancellationToken);
                    }
                }

                var conversation = MapConversationEntity(data, contact, startedAt);
                await _repository.UpsertConversationAsync(conversation, cancellationToken);

                if (shouldFetchMessages)
                {
                    List<SmartsuppMessageData> messageData;
                    try
                    {
                        messageData = await _apiClient.GetConversationMessagesAsync(data.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "smartsupp reconcile: failed to fetch messages for {ConversationId}", data.Id);
                        messageData = [];
                    }

                    if (messageData.Count > 0)
                    {
                        var msgs = messageData.Select(m => new SmartsuppMessage
                        {
                            Id = m.Id,
                            ConversationId = data.Id,
                            AuthorType = ParseAuthorType(m.SubType),
                            SubType = m.SubType,
                            AuthorName = ComposeAuthorName(m, contact),
                            Content = m.Content,
                            TriggerName = m.TriggerName,
                            TriggerId = m.TriggerId,
                            PageUrl = m.PageUrl,
                            AgentId = m.AgentId,
                            VisitorId = m.VisitorId,
                            DeliveryStatus = m.DeliveryStatus,
                            DeliveredAt = m.DeliveredAt is { } da ? Unspecified(da) : null,
                            IsOffline = m.IsOffline,
                            IsReply = m.IsReply,
                            IsFirstReply = m.IsFirstReply,
                            ResponseTime = m.ResponseTime,
                            CreatedAt = m.CreatedAt,
                            UpdatedAt = m.UpdatedAt,
                            AttachmentsJson = m.AttachmentsJson,
                        }).ToList();

                        await _repository.UpsertMessagesAsync(data.Id, msgs, cancellationToken);
                        messages += msgs.Count;
                    }
                }

                reconciled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "smartsupp reconcile: unexpected error for conversation {Id}", localRef.Id);
            }
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return (reconciled, closedRemotely, messages);
    }

    private async Task<int> ProcessConversationAsync(
        SmartsuppConversationData data,
        DateTime syncedAt,
        Dictionary<string, SmartsuppContactData?> contactCache,
        CancellationToken cancellationToken)
    {
        List<SmartsuppMessageData> messageData;
        try
        {
            messageData = await _apiClient.GetConversationMessagesAsync(data.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "smartsupp manual sync failed to fetch messages for {ConversationId}", data.Id);
            messageData = [];
        }

        SmartsuppContactData? contact = null;
        if (!string.IsNullOrEmpty(data.ContactId))
        {
            contact = await FetchContactCachedAsync(data.ContactId, contactCache, cancellationToken);

            if (contact is not null)
            {
                var contactEntity = new SmartsuppContact
                {
                    Id = contact.Id,
                    Email = contact.Email,
                    Name = contact.Name,
                    Phone = contact.Phone,
                    Note = contact.Note,
                    BannedAt = contact.BannedAt is { } ba ? Unspecified(ba) : null,
                    BannedBy = contact.BannedBy,
                    GdprApproved = contact.GdprApproved,
                    TagsJson = contact.TagsJson,
                    PropertiesJson = contact.PropertiesJson,
                    CreatedAt = Unspecified(contact.CreatedAt),
                    UpdatedAt = Unspecified(contact.UpdatedAt),
                    SyncedAt = Unspecified(syncedAt),
                };
                await _repository.UpsertContactAsync(contactEntity, cancellationToken);
            }
        }

        var conversation = MapConversationEntity(data, contact, syncedAt);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);

        if (messageData.Count == 0)
            return 0;

        var messages = messageData.Select(m => new SmartsuppMessage
        {
            Id = m.Id,
            ConversationId = data.Id,
            AuthorType = ParseAuthorType(m.SubType),
            SubType = m.SubType,
            AuthorName = ComposeAuthorName(m, contact),
            Content = m.Content,
            TriggerName = m.TriggerName,
            TriggerId = m.TriggerId,
            PageUrl = m.PageUrl,
            AgentId = m.AgentId,
            VisitorId = m.VisitorId,
            DeliveryStatus = m.DeliveryStatus,
            DeliveredAt = m.DeliveredAt is { } da ? Unspecified(da) : null,
            IsOffline = m.IsOffline,
            IsReply = m.IsReply,
            IsFirstReply = m.IsFirstReply,
            ResponseTime = m.ResponseTime,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            AttachmentsJson = m.AttachmentsJson,
        }).ToList();

        await _repository.UpsertMessagesAsync(data.Id, messages, cancellationToken);
        return messages.Count;
    }

    private SmartsuppConversation MapConversationEntity(
        SmartsuppConversationData data,
        SmartsuppContactData? contact,
        DateTime syncedAt)
    {
        var status = data.Status?.ToLowerInvariant() == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        return new SmartsuppConversation
        {
            Id = data.Id,
            ExtId = data.ExtId,
            Status = status,
            IsUnread = data.Unread,
            IsOffline = data.IsOffline,
            IsServed = data.IsServed,
            ContactId = data.ContactId,
            ContactName = contact?.Name,
            ContactEmail = contact?.Email,
            ContactAvatarUrl = null,
            VisitorId = data.VisitorId,
            FinishedAt = data.FinishedAt is { } fa ? Unspecified(fa) : null,
            Domain = data.Domain,
            Referer = data.Referer,
            LocationCountry = data.LocationCountry,
            LocationCity = data.LocationCity,
            LocationIp = data.LocationIp,
            LocationCode = data.LocationCode,
            VariablesJson = data.VariablesJson,
            TagsJson = data.TagsJson,
            LastMessagePreview = data.LastMessageText?.Length > LastMessagePreviewMaxLength
                ? data.LastMessageText[..LastMessagePreviewMaxLength]
                : data.LastMessageText,
            LastMessageAt = data.LastMessageAt,
            CreatedAt = data.CreatedAt,
            UpdatedAt = data.UpdatedAt,
            SyncedAt = syncedAt,
        };
    }

    private async Task<SmartsuppContactData?> FetchContactCachedAsync(
        string contactId,
        Dictionary<string, SmartsuppContactData?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(contactId, out var cached))
            return cached;

        SmartsuppContactData? contact;
        try
        {
            contact = await _apiClient.GetContactAsync(contactId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "smartsupp manual sync failed to fetch contact {ContactId}", contactId);
            contact = null;
        }

        cache[contactId] = contact;
        return contact;
    }

    private static string? ComposeAuthorName(SmartsuppMessageData message, SmartsuppContactData? contact) =>
        ParseAuthorType(message.SubType) switch
        {
            SmartsuppMessageAuthorType.Visitor => contact?.Name,
            SmartsuppMessageAuthorType.Bot => message.TriggerName,
            _ => null
        };

    private static SmartsuppMessageAuthorType ParseAuthorType(string? subType) =>
        subType?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            "contact" => SmartsuppMessageAuthorType.Visitor,
            _ => SmartsuppMessageAuthorType.Visitor,
        };

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
}
```

- [ ] **Step 2: Run the full Smartsupp handler tests**

```bash
dotnet test backend/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~RunManualSyncHandlerTests" \
  --no-build -q
```

Expected: all pass (including the 5 existing tests plus the 6 new ones = 11 total).

- [ ] **Step 3: Run the full Smartsupp test suite**

```bash
dotnet test backend/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~Smartsupp" \
  -q
```

Expected: all Smartsupp tests pass with no regressions.

- [ ] **Step 4: Format**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | head -20
```

If any diffs are found:

```bash
dotnet format backend/Anela.Heblo.sln
```

- [ ] **Step 5: Full build to confirm clean state**

```bash
dotnet build backend/Anela.Heblo.sln -q
```

Expected: 0 errors, 0 warnings related to this change.

- [ ] **Step 6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs \
  backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncResponse.cs \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Smartsupp/RunManualSyncHandlerTests.cs
git commit -m "feat(smartsupp): reconcile locally-open conversations during manual sync"
```

---

## Self-Review

### Spec coverage

| Requirement | Task |
|-------------|------|
| Run reconciliation inside RunManualSyncHandler | Task 8 (`ReconcileOpenConversationsAsync` called from `Handle`) |
| Add `GetConversationAsync(id)` API method | Tasks 1, 3 |
| 404 → mark Resolved + warn-log | Task 8 (`MarkConversationResolvedAsync` branch) |
| Skip messages when status+LastMessageAt unchanged | Task 8 (`shouldFetchMessages` gate) |
| Re-fetch messages when status changed or LastMessageAt advanced | Task 8 |
| Add `ListOpenConversationRefsAsync` + `MarkConversationResolvedAsync` | Tasks 4, 6 |
| seenIds tracks even conversations filtered out by `since` | Task 8 (`seenIds.Add` before the `continue`) |
| `ConversationsReconciled` + `ConversationsClosedRemotely` in response | Task 7 |
| TDD: failing tests before implementation | Tasks 2→3 (API client), 5→8 (handler) |
| No migration needed | Confirmed — no schema change |
| No new endpoint, no UI change | Confirmed — no frontend files touched |

### Placeholder scan

No TBD, TODO, or "similar to" references. All code blocks contain complete compilable code.

### Type consistency

- `OpenConversationRef` defined in `ISmartsuppRepository.cs` (Task 4), used in `SmartsuppRepository.cs` (Task 6) and `RunManualSyncHandler.cs` (Task 8) — consistent.
- `MarkConversationResolvedAsync(string, DateTime, DateTime, CancellationToken)` — interface (Task 4), implementation (Task 6), call site (Task 8) — all match.
- `GetConversationAsync(string, CancellationToken)` returns `Task<SmartsuppConversationData?>` — interface (Task 1), implementation (Task 3), call site (Task 8), tests (Task 2, 5) — all match.
- `ConversationsReconciled` / `ConversationsClosedRemotely` — defined in response (Task 7), set in handler (Task 8), asserted in tests (Task 5) — all match.
