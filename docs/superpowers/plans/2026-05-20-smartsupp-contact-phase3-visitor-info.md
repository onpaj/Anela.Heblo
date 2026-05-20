# Smartsupp Contact Phase 3 — Visitor Info, OS/Browser, Visits, Browsing History

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose visitor-level data (OS, browser, visit count, browsing history) from the Smartsupp `/v2/visitors/{id}` API, cache it on `SmartsuppConversation`, and surface it in the ContactDetailsPanel sidebar.

**Architecture:** A new `GetVisitorInfo` use case fetches from the Smartsupp REST API on demand, writes a 24-hour denormalized cache to `SmartsuppConversation` visitor columns, and returns the snapshot + chats count + browsing history. A new `VisitorInfoCard.tsx` component renders the "Zařízení" and "Historie procházení" sections; the ContactDetailsPanel header gains visits/chats stats.

**Tech Stack:** .NET 8, MediatR, EF Core (PostgreSQL), xUnit + FluentAssertions + Moq, React 18, React Query, Tailwind CSS.

---

### Task 1: Spike — confirm Smartsupp visitor API shape

**Files:**
- Create: `docs/integrations/smartsupp-visitor-api.md`

This task calls the live API for a known conversation and documents the exact JSON. No code is written here — just curl + documentation. Required by the project rule that Smartsupp API findings be written before relying on them.

- [ ] **Step 1: Find a VisitorId from the database**

Run in psql against the local dev database:
```sql
SELECT "Id", "VisitorId", "ContactId"
FROM public."SmartsuppConversations"
WHERE "VisitorId" IS NOT NULL
LIMIT 5;
```

Note a `VisitorId` value (format: `vitXXXXX`) and its `Id`.

- [ ] **Step 2: Call GET /v2/visitors/{visitor_id}**

```bash
VISITOR_ID="<paste VisitorId from step 1>"
API_TOKEN="<value from secrets.json Smartsupp:ApiToken>"

curl -s -H "Authorization: Bearer $API_TOKEN" \
  "https://api.smartsupp.com/v2/visitors/$VISITOR_ID" | jq .
```

Capture the full JSON. Note whether it contains: `os`, `browser`, `user_agent`, `visits_count` (or `visits`). If the response is different from the assumed shape, update the field names in `SmartsuppVisitorApiResponse` in Task 3 before continuing.

- [ ] **Step 3: Call GET /v2/visitors/{visitor_id}/pages**

```bash
curl -s -H "Authorization: Bearer $API_TOKEN" \
  "https://api.smartsupp.com/v2/visitors/$VISITOR_ID/pages" | jq .
```

If 404 → the endpoint doesn't exist. Plan falls back to `SmartsuppMessage.PageUrl` data (already handled in Task 6). If 200 → note the exact field names (`url`, `title`, `viewed_at`).

- [ ] **Step 4: Write spike findings to docs**

Create `docs/integrations/smartsupp-visitor-api.md`:

```markdown
# Smartsupp Visitor API — Findings (spiked YYYY-MM-DD)

## GET /v2/visitors/{visitor_id}

**Response shape (exact):**
\`\`\`json
<paste actual JSON here>
\`\`\`

**Fields relevant for Phase 3:**
- `os`: present / absent
- `browser`: present / absent
- `user_agent`: present / absent
- `visits_count` (or `visits`): present as `<field_name>` / absent

## GET /v2/visitors/{visitor_id}/pages

**Status:** 200 OK / 404 Not Found

**Response shape (if 200):**
\`\`\`json
<paste actual JSON here>
\`\`\`

## Rate limits

Observed Retry-After? Y/N.
```

- [ ] **Step 5: Commit**

```bash
git add docs/integrations/smartsupp-visitor-api.md
git commit -m "docs: spike Smartsupp visitor API findings for Phase 3"
```

> **GATE:** If `/v2/visitors/{id}` returns 404 for all known visitors, or the shape is completely different, stop and re-scope before Task 2.

---

### Task 2: Domain types and interface extension

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppConversation.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`

Pure type definitions — build validates correctness implicitly.

- [ ] **Step 1: Add visitor data types to ISmartsuppApiClient.cs**

At the end of `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`, after `SmartsuppContactData`:

```csharp
public class SmartsuppVisitorData
{
    public string Id { get; set; } = null!;
    public string? UserAgent { get; set; }
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public int? VisitsCount { get; set; }
}

public class SmartsuppVisitorPageData
{
    public string Url { get; set; } = null!;
    public string? Title { get; set; }
    public DateTime? ViewedAt { get; set; }
}
```

- [ ] **Step 2: Add two methods to the ISmartsuppApiClient interface**

In `ISmartsuppApiClient.cs`, after `GetContactAsync`:

```csharp
Task<SmartsuppVisitorData?> GetVisitorAsync(
    string visitorId,
    CancellationToken cancellationToken);

Task<List<SmartsuppVisitorPageData>> GetVisitorPagesAsync(
    string visitorId,
    CancellationToken cancellationToken);
```

- [ ] **Step 3: Add visitor cache fields to SmartsuppConversation.cs**

In `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppConversation.cs`, after `LastClosedAt`:

```csharp
public string? VisitorUserAgent { get; set; }
public string? VisitorOs { get; set; }
public string? VisitorBrowser { get; set; }
public int? VisitorVisitsCount { get; set; }
public DateTime? VisitorInfoFetchedAt { get; set; }
```

- [ ] **Step 4: Add UpdateVisitorCacheAsync to ISmartsuppRepository.cs**

In `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`, after `SaveChangesAsync`:

```csharp
Task UpdateVisitorCacheAsync(
    string conversationId,
    string? userAgent,
    string? os,
    string? browser,
    int? visitsCount,
    DateTime fetchedAt,
    CancellationToken cancellationToken);
```

- [ ] **Step 5: Verify expected build failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet build
```

Expected: **FAIL** — `SmartsuppApiClient` does not implement the two new interface methods, and `SmartsuppRepository` doesn't implement `UpdateVisitorCacheAsync`. This is expected at this step.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/
git commit -m "feat(smartsupp): add visitor data types, cache fields, and repo/client interface stubs"
```

---

### Task 3: API client implementation (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppVisitorApiClientTests.cs`
- Modify: `backend/src/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppVisitorApiClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Polly;
using Polly.Retry;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppVisitorApiClientTests
{
    private static SmartsuppApiClient CreateClient(HttpMessageHandler handler, ResiliencePipeline? pipeline = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.smartsupp.com/v2/") };
        factory.Setup(f => f.CreateClient("Smartsupp")).Returns(httpClient);
        var options = Options.Create(new SmartsuppOptions
        {
            ApiToken = "test-token",
            BaseUrl = "https://api.smartsupp.com/v2/",
        });
        return new SmartsuppApiClient(options, factory.Object, NullLogger<SmartsuppApiClient>.Instance, pipeline);
    }

    private static Mock<HttpMessageHandler> RespondWith(int statusCode, string? body = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = body is null
                    ? new StringContent("")
                    : new StringContent(body, Encoding.UTF8, "application/json")
            });
        return handler;
    }

    [Fact]
    public async Task GetVisitorAsync_ReturnsVisitor_WhenApiResponds()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            id = "vitABC",
            user_agent = "Mozilla/5.0 (Macintosh) Chrome/148",
            os = "macOS 10.15.7",
            browser = "Chrome 148.0.0.0",
            visits_count = 321,
        });
        var client = CreateClient(RespondWith(200, json).Object);

        // Act
        var result = await client.GetVisitorAsync("vitABC", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("vitABC");
        result.Os.Should().Be("macOS 10.15.7");
        result.Browser.Should().Be("Chrome 148.0.0.0");
        result.VisitsCount.Should().Be(321);
        result.UserAgent.Should().Contain("Chrome");
    }

    [Fact]
    public async Task GetVisitorAsync_ReturnsNull_When404()
    {
        // Arrange
        var client = CreateClient(RespondWith(404).Object);

        // Act
        var result = await client.GetVisitorAsync("unknown", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetVisitorAsync_Retries_On429()
    {
        // Arrange
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { id = "v1", os = "macOS", browser = "Chrome", visits_count = 1 }),
                        Encoding.UTF8, "application/json")
                };
            });

        var immediateRetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
            })
            .Build();

        var client = CreateClient(handler.Object, immediateRetryPipeline);

        // Act
        var result = await client.GetVisitorAsync("v1", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetVisitorPagesAsync_ReturnsPages_WhenApiResponds()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { url = "https://www.anela.cz/product", title = "Produkt A", viewed_at = "2026-05-01T10:00:00Z" },
                new { url = "https://www.anela.cz/checkout", title = (string?)null, viewed_at = "2026-05-01T10:05:00Z" },
            }
        });
        var client = CreateClient(RespondWith(200, json).Object);

        // Act
        var result = await client.GetVisitorPagesAsync("vitABC", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Url.Should().Be("https://www.anela.cz/product");
        result[0].Title.Should().Be("Produkt A");
        result[1].Title.Should().BeNull();
    }

    [Fact]
    public async Task GetVisitorPagesAsync_ReturnsEmpty_When404()
    {
        // Arrange
        var client = CreateClient(RespondWith(404).Object);

        // Act
        var result = await client.GetVisitorPagesAsync("vitABC", CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "SmartsuppVisitorApiClientTests" -v minimal
```

Expected: **FAIL** — `SmartsuppApiClient` does not implement the two new methods.

- [ ] **Step 3: Add private response shapes to SmartsuppApiClient.cs**

In `backend/src/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`, inside the `// ---- API response shapes ----` section at the bottom, after `SmartsuppContactApiResponse`:

```csharp
private sealed class SmartsuppVisitorApiResponse
{
    public string? Id { get; set; }
    public string? UserAgent { get; set; }
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public int? VisitsCount { get; set; }
}

private sealed class SmartsuppVisitorPagesApiResponse
{
    public List<SmartsuppVisitorPageApiItem>? Items { get; set; }
}

private sealed class SmartsuppVisitorPageApiItem
{
    public string? Url { get; set; }
    public string? Title { get; set; }
    public DateTime? ViewedAt { get; set; }
}
```

> **Spike adjustment:** If the live API uses `visits` instead of `visits_count`, rename `VisitsCount` here only (not in the domain type `SmartsuppVisitorData`). The domain type name stays stable.

- [ ] **Step 4: Add MapVisitor and MapVisitorPage to SmartsuppApiClient.cs**

After `MapContact`:

```csharp
private static SmartsuppVisitorData MapVisitor(SmartsuppVisitorApiResponse item) =>
    new()
    {
        Id = item.Id ?? "",
        UserAgent = item.UserAgent,
        Os = item.Os,
        Browser = item.Browser,
        VisitsCount = item.VisitsCount,
    };

private static SmartsuppVisitorPageData MapVisitorPage(SmartsuppVisitorPageApiItem item) =>
    new()
    {
        Url = item.Url ?? "",
        Title = item.Title,
        ViewedAt = item.ViewedAt is { } va ? Unspecified(va) : null,
    };
```

- [ ] **Step 5: Implement GetVisitorAsync in SmartsuppApiClient.cs**

After `GetConversationAsync`:

```csharp
public async Task<SmartsuppVisitorData?> GetVisitorAsync(
    string visitorId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(_options.ApiToken))
        throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

    return await _pipeline.ExecuteAsync(async ct =>
    {
        var client = _httpClientFactory.CreateClient("Smartsupp");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_options.BaseUrl}visitors/{visitorId}");
        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        var response = await client.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Smartsupp visitor failed {Status}: {Body}", response.StatusCode, errorBody);
            var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            if (response.Headers.RetryAfter?.Delta is { } delta)
                ex.Data["RetryAfter"] = delta;
            throw ex;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SmartsuppVisitorApiResponse>(raw, JsonOptions);
        return result is null ? null : MapVisitor(result);
    }, cancellationToken);
}
```

- [ ] **Step 6: Implement GetVisitorPagesAsync in SmartsuppApiClient.cs**

After `GetVisitorAsync`:

```csharp
public async Task<List<SmartsuppVisitorPageData>> GetVisitorPagesAsync(
    string visitorId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(_options.ApiToken))
        throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

    return await _pipeline.ExecuteAsync(async ct =>
    {
        var client = _httpClientFactory.CreateClient("Smartsupp");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_options.BaseUrl}visitors/{visitorId}/pages");
        request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

        var response = await client.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new List<SmartsuppVisitorPageData>();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Smartsupp visitor pages failed {Status}: {Body}", response.StatusCode, errorBody);
            var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            if (response.Headers.RetryAfter?.Delta is { } delta)
                ex.Data["RetryAfter"] = delta;
            throw ex;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SmartsuppVisitorPagesApiResponse>(raw, JsonOptions);
        return result?.Items?.Select(MapVisitorPage).ToList() ?? new List<SmartsuppVisitorPageData>();
    }, cancellationToken);
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "SmartsuppVisitorApiClientTests" -v minimal
```

Expected: **PASS** — all 5 tests pass.

- [ ] **Step 8: Run full build + all tests**

```bash
dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: Build fails only on `SmartsuppRepository` not implementing `UpdateVisitorCacheAsync` (expected — handled in Task 5).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppVisitorApiClientTests.cs
git commit -m "feat(smartsupp): implement GetVisitorAsync and GetVisitorPagesAsync on API client"
```

---

### Task 4: EF configuration + migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs`
- Create: migration files (generated by `dotnet ef`)

- [ ] **Step 1: Add visitor column configs to SmartsuppConversationConfiguration.cs**

After `builder.Property(e => e.LastClosedAt).HasColumnType("timestamp without time zone");`, add:

```csharp
builder.Property(e => e.VisitorUserAgent).HasColumnType("text");
builder.Property(e => e.VisitorOs).HasMaxLength(100);
builder.Property(e => e.VisitorBrowser).HasMaxLength(100);
builder.Property(e => e.VisitorVisitsCount);
builder.Property(e => e.VisitorInfoFetchedAt).HasColumnType("timestamp without time zone");
```

- [ ] **Step 2: Generate the migration**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet ef migrations add AddSmartsuppVisitorCache \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API \
  --output-dir Migrations
```

Expected: Creates `YYYYMMDDHHMMSS_AddSmartsuppVisitorCache.cs` and its `.Designer.cs` in `Migrations/`.

- [ ] **Step 3: Verify the generated migration**

Open the generated file and confirm it contains `AddColumn` calls for:
- `VisitorUserAgent` (text, nullable)
- `VisitorOs` (character varying 100, nullable)
- `VisitorBrowser` (character varying 100, nullable)
- `VisitorVisitsCount` (integer, nullable)
- `VisitorInfoFetchedAt` (timestamp without time zone, nullable)

- [ ] **Step 4: Generate the SQL script for manual application**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet ef migrations script --idempotent \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API \
  -o /tmp/visitor-cache-migration.sql
```

Apply this script against the dev database. Save the path for staging deployment.

- [ ] **Step 5: Build to verify**

```bash
dotnet build
```

Expected: Still fails only on `SmartsuppRepository.UpdateVisitorCacheAsync` — persistence layer.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(smartsupp): EF config and migration for visitor cache columns"
```

---

### Task 5: Repository — implement UpdateVisitorCacheAsync

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Implement UpdateVisitorCacheAsync in SmartsuppRepository.cs**

After the `SaveChangesAsync` method, add:

```csharp
public async Task UpdateVisitorCacheAsync(
    string conversationId,
    string? userAgent,
    string? os,
    string? browser,
    int? visitsCount,
    DateTime fetchedAt,
    CancellationToken cancellationToken)
{
    var conversation = await _db.SmartsuppConversations
        .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    if (conversation is null)
        return;

    conversation.VisitorUserAgent = userAgent;
    conversation.VisitorOs = os;
    conversation.VisitorBrowser = browser;
    conversation.VisitorVisitsCount = visitsCount;
    conversation.VisitorInfoFetchedAt = fetchedAt;
    await _db.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 2: Build to verify (full clean build now)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet build
```

Expected: **PASS** — all interface members are now implemented.

- [ ] **Step 3: Run all tests**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: **PASS**

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs
git commit -m "feat(smartsupp): implement UpdateVisitorCacheAsync in SmartsuppRepository"
```

---

### Task 6: ErrorCode + GetVisitorInfo use case (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/GetVisitorInfoRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/GetVisitorInfoResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/GetVisitorInfoHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetVisitorInfoHandlerTests.cs`

- [ ] **Step 1: Add SmartsuppVisitorNotFound to ErrorCodes.cs**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, after `SmartsuppShoptetCustomerNotFound = 2704`:

```csharp
SmartsuppVisitorNotFound = 2705,
```

- [ ] **Step 2: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetVisitorInfoHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetVisitorInfoHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();

    private GetVisitorInfoHandler CreateHandler() =>
        new(_repo.Object, _apiClient.Object);

    private static SmartsuppConversation MakeConversation(
        string id = "c1",
        string? visitorId = "vis1",
        string? contactId = "ct1",
        DateTime? visitorInfoFetchedAt = null) =>
        new()
        {
            Id = id,
            VisitorId = visitorId,
            ContactId = contactId,
            Status = SmartsuppConversationStatus.Open,
            VisitorInfoFetchedAt = visitorInfoFetchedAt,
            Messages = [],
        };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenConversationMissing()
    {
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation?)null);

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoVisitorId()
    {
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConversation(visitorId: null));

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppVisitorNotFound);
    }

    [Fact]
    public async Task Handle_CallsApiAndCaches_WhenCacheMiss()
    {
        // Arrange
        var conv = MakeConversation(visitorInfoFetchedAt: null);
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppConversation> { MakeConversation("c2"), MakeConversation("c3") });
        _apiClient.Setup(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppVisitorData
            {
                Id = "vis1",
                Os = "macOS 10.15.7",
                Browser = "Chrome 148.0.0.0",
                UserAgent = "Mozilla...",
                VisitsCount = 321,
            });
        _apiClient.Setup(a => a.GetVisitorPagesAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppVisitorPageData>
            {
                new() { Url = "https://www.anela.cz/product", Title = "Produkt", ViewedAt = new DateTime(2026, 5, 1) },
            });

        // Act
        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.VisitorInfo.Should().NotBeNull();
        result.VisitorInfo!.Os.Should().Be("macOS 10.15.7");
        result.VisitorInfo.Browser.Should().Be("Chrome 148.0.0.0");
        result.VisitorInfo.VisitsCount.Should().Be(321);
        result.VisitorInfo.ChatsCount.Should().Be(3); // c1 + the 2 others
        result.VisitorInfo.Pages.Should().HaveCount(1);

        _repo.Verify(r => r.UpdateVisitorCacheAsync(
            "c1", "Mozilla...", "macOS 10.15.7", "Chrome 148.0.0.0", 321,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UsesCachedData_WhenCacheFresh()
    {
        // Arrange — cache is 1 hour old (fresh, within 24h TTL)
        var conv = MakeConversation(visitorInfoFetchedAt: DateTime.UtcNow.AddHours(-1));
        conv.VisitorOs = "Windows 11";
        conv.VisitorBrowser = "Firefox 120";
        conv.VisitorVisitsCount = 50;
        conv.VisitorUserAgent = "Mozilla/5.0 Windows";

        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppConversation>());

        // Act
        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.VisitorInfo!.Os.Should().Be("Windows 11");
        result.VisitorInfo.VisitsCount.Should().Be(50);

        _apiClient.Verify(a => a.GetVisitorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpdateVisitorCacheAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RefreshesCache_WhenCacheStale()
    {
        // Arrange — cache is 25 hours old (beyond 24h TTL)
        var conv = MakeConversation(visitorInfoFetchedAt: DateTime.UtcNow.AddHours(-25));
        conv.VisitorOs = "Old OS";

        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppConversation>());
        _apiClient.Setup(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppVisitorData { Id = "vis1", Os = "New OS", Browser = "Chrome 149" });
        _apiClient.Setup(a => a.GetVisitorPagesAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppVisitorPageData>());

        // Act
        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.VisitorInfo!.Os.Should().Be("New OS");
        _apiClient.Verify(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FallsBackToMessagePages_WhenVisitorPagesEmpty()
    {
        // Arrange
        var conv = MakeConversation(visitorInfoFetchedAt: null);
        conv.Messages =
        [
            new() { Id = "m1", ConversationId = "c1", PageUrl = "https://www.anela.cz/shop", CreatedAt = new DateTime(2026, 5, 1, 10, 0, 0) },
            new() { Id = "m2", ConversationId = "c1", PageUrl = "https://www.anela.cz/checkout", CreatedAt = new DateTime(2026, 5, 1, 10, 5, 0) },
            new() { Id = "m3", ConversationId = "c1", PageUrl = "https://www.anela.cz/shop", CreatedAt = new DateTime(2026, 5, 1, 10, 2, 0) }, // duplicate
        ];

        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClient.Setup(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppVisitorData { Id = "vis1" });
        _apiClient.Setup(a => a.GetVisitorPagesAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]); // empty → triggers fallback

        // Act
        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        // Assert
        result.VisitorInfo!.Pages.Should().HaveCount(2); // deduplicated
        result.VisitorInfo.Pages.Select(p => p.Url).Should().Contain("https://www.anela.cz/shop");
        result.VisitorInfo.Pages.Select(p => p.Url).Should().Contain("https://www.anela.cz/checkout");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetVisitorInfoHandlerTests" -v minimal
```

Expected: **FAIL** — types don't exist yet.

- [ ] **Step 4: Create GetVisitorInfoRequest.cs**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/GetVisitorInfoRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;

public class GetVisitorInfoRequest : IRequest<GetVisitorInfoResponse>
{
    public required string ConversationId { get; set; }
}
```

- [ ] **Step 5: Create GetVisitorInfoResponse.cs**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/GetVisitorInfoResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;

public class GetVisitorInfoResponse : BaseResponse
{
    public VisitorInfoDto? VisitorInfo { get; set; }

    public GetVisitorInfoResponse() { }
    public GetVisitorInfoResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class VisitorInfoDto
{
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public string? UserAgent { get; set; }
    public int? VisitsCount { get; set; }
    public int ChatsCount { get; set; }
    public List<VisitorPageDto> Pages { get; set; } = [];
}

public class VisitorPageDto
{
    public string Url { get; set; } = null!;
    public string? Title { get; set; }
    public DateTime? ViewedAt { get; set; }
}
```

- [ ] **Step 6: Create GetVisitorInfoHandler.cs**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/GetVisitorInfoHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;

public class GetVisitorInfoHandler : IRequestHandler<GetVisitorInfoRequest, GetVisitorInfoResponse>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly ISmartsuppRepository _repo;
    private readonly ISmartsuppApiClient _apiClient;

    public GetVisitorInfoHandler(ISmartsuppRepository repo, ISmartsuppApiClient apiClient)
    {
        _repo = repo;
        _apiClient = apiClient;
    }

    public async Task<GetVisitorInfoResponse> Handle(
        GetVisitorInfoRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repo.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GetVisitorInfoResponse(ErrorCodes.SmartsuppConversationNotFound);

        if (string.IsNullOrEmpty(conversation.VisitorId))
            return new GetVisitorInfoResponse(ErrorCodes.SmartsuppVisitorNotFound);

        var otherChats = await _repo.ListConversationsForContactAsync(
            conversation.ContactId ?? "",
            conversation.Id,
            cancellationToken);
        var chatsCount = otherChats.Count + 1;

        var isCacheStale = conversation.VisitorInfoFetchedAt is null ||
                           DateTime.UtcNow - conversation.VisitorInfoFetchedAt.Value > CacheTtl;

        List<SmartsuppVisitorPageData> pages;

        if (isCacheStale)
        {
            var visitor = await _apiClient.GetVisitorAsync(conversation.VisitorId, cancellationToken);
            pages = await _apiClient.GetVisitorPagesAsync(conversation.VisitorId, cancellationToken);

            var fetchedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _repo.UpdateVisitorCacheAsync(
                conversation.Id,
                visitor?.UserAgent,
                visitor?.Os,
                visitor?.Browser,
                visitor?.VisitsCount,
                fetchedAt,
                cancellationToken);

            conversation.VisitorUserAgent = visitor?.UserAgent;
            conversation.VisitorOs = visitor?.Os;
            conversation.VisitorBrowser = visitor?.Browser;
            conversation.VisitorVisitsCount = visitor?.VisitsCount;
        }
        else
        {
            pages = [];
        }

        if (pages.Count == 0)
            pages = FallbackPagesFromMessages(conversation);

        return new GetVisitorInfoResponse
        {
            VisitorInfo = new VisitorInfoDto
            {
                Os = conversation.VisitorOs,
                Browser = conversation.VisitorBrowser,
                UserAgent = conversation.VisitorUserAgent,
                VisitsCount = conversation.VisitorVisitsCount,
                ChatsCount = chatsCount,
                Pages = pages.Select(p => new VisitorPageDto
                {
                    Url = p.Url,
                    Title = p.Title,
                    ViewedAt = p.ViewedAt,
                }).ToList(),
            }
        };
    }

    private static List<SmartsuppVisitorPageData> FallbackPagesFromMessages(SmartsuppConversation conversation) =>
        conversation.Messages
            .Where(m => !string.IsNullOrEmpty(m.PageUrl))
            .GroupBy(m => m.PageUrl!)
            .Select(g => g.MinBy(m => m.CreatedAt)!)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new SmartsuppVisitorPageData { Url = m.PageUrl! })
            .ToList();
}
```

- [ ] **Step 7: Run handler tests to verify they pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetVisitorInfoHandlerTests" -v minimal
```

Expected: **PASS** — all 6 tests pass.

- [ ] **Step 8: Run all backend tests**

```bash
dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: **PASS**

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/ \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetVisitorInfoHandlerTests.cs
git commit -m "feat(smartsupp): add GetVisitorInfo use case with 24h cache and page fallback"
```

---

### Task 7: Controller endpoint + docs

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`
- Modify: `docs/features/smartsupp.md`

- [ ] **Step 1: Add using directive to SmartsuppController.cs**

At the top of `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`, add:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;
```

- [ ] **Step 2: Add visitor-info endpoint after GetShoptetInfo**

```csharp
[HttpGet("conversations/{id}/visitor-info")]
[ProducesResponseType(typeof(GetVisitorInfoResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<GetVisitorInfoResponse>> GetVisitorInfo(
    string id,
    CancellationToken cancellationToken = default)
{
    var result = await _mediator.Send(
        new GetVisitorInfoRequest { ConversationId = id },
        cancellationToken);
    return HandleResponse(result);
}
```

- [ ] **Step 3: Update docs/features/smartsupp.md**

In the `### Konverzace (chráněné — vyžaduje přihlášení)` endpoint table, add after the `shoptet-info` row:

```markdown
| `GET` | `/api/smartsupp/conversations/{id}/visitor-info` | Vrátí visitor data (OS, prohlížeč, počet návštěv, historie stránek). Vrátí 404 pokud konverzace nemá `visitor_id`. Data jsou cachována 24 h. |
```

- [ ] **Step 4: Build + test**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/backend
dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: **PASS**

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs \
        docs/features/smartsupp.md
git commit -m "feat(smartsupp): expose GET /conversations/{id}/visitor-info endpoint"
```

---

### Task 8: Frontend hook (TDD)

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts`
- Modify: `frontend/src/api/hooks/useSmartsupp.ts`

- [ ] **Step 1: Write the failing hook test**

Create `frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts`:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useSmartsuppVisitorInfo } from "../useSmartsupp";

const mockFetch = jest.fn();

jest.mock("../client", () => ({
  getAuthenticatedApiClient: () => ({
    baseUrl: "http://localhost:5001",
    http: { fetch: mockFetch },
  }),
}));

beforeEach(() => mockFetch.mockReset());

function Wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

describe("useSmartsuppVisitorInfo", () => {
  it("is disabled when conversationId is null", () => {
    const { result } = renderHook(() => useSmartsuppVisitorInfo(null), { wrapper: Wrapper });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("returns null when API returns 404", async () => {
    mockFetch.mockResolvedValue({ status: 404, ok: false });

    const { result } = renderHook(() => useSmartsuppVisitorInfo("c1"), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data).toBeNull();
  });

  it("returns visitor info on 200", async () => {
    const payload = {
      success: true,
      visitorInfo: {
        os: "macOS 10.15.7",
        browser: "Chrome 148.0.0.0",
        visitsCount: 321,
        chatsCount: 3,
        pages: [{ url: "https://www.anela.cz/product", title: "Produkt", viewedAt: null }],
      },
    };
    mockFetch.mockResolvedValue({
      status: 200,
      ok: true,
      json: () => Promise.resolve(payload),
    });

    const { result } = renderHook(() => useSmartsuppVisitorInfo("c1"), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data?.visitorInfo?.os).toBe("macOS 10.15.7");
    expect(result.current.data?.visitorInfo?.visitsCount).toBe(321);
    expect(result.current.data?.visitorInfo?.pages).toHaveLength(1);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/frontend
npm test -- --testPathPattern="useSmartsuppVisitorInfo" --watchAll=false
```

Expected: **FAIL** — `useSmartsuppVisitorInfo` is not exported.

- [ ] **Step 3: Add types and key to useSmartsupp.ts**

In `frontend/src/api/hooks/useSmartsupp.ts`:

Add to `SMARTSUPP_QUERY_KEYS` object:
```typescript
visitorInfo: (id: string) => ["smartsupp", "visitor-info", id] as const,
```

Add type declarations after `GetSmartsuppShoptetInfoResponse`:
```typescript
export interface VisitorPageDto {
  url: string;
  title?: string | null;
  viewedAt?: string | null;
}

export interface VisitorInfoDto {
  os?: string | null;
  browser?: string | null;
  userAgent?: string | null;
  visitsCount?: number | null;
  chatsCount: number;
  pages: VisitorPageDto[];
}

export interface GetSmartsuppVisitorInfoResponse {
  success: boolean;
  visitorInfo?: VisitorInfoDto | null;
}
```

- [ ] **Step 4: Add useSmartsuppVisitorInfo hook**

After `useSmartsuppShoptetInfo`:

```typescript
export function useSmartsuppVisitorInfo(conversationId: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.visitorInfo(conversationId ?? ""),
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetchRaw(
        apiClient,
        `${baseUrl}/api/smartsupp/conversations/${conversationId}/visitor-info`
      );
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`Visitor info error: ${response.status}`);
      return response.json() as Promise<GetSmartsuppVisitorInfoResponse>;
    },
    enabled: !!conversationId,
    staleTime: 600_000, // 10 minutes
    retry: false,
  });
}
```

- [ ] **Step 5: Run test to verify it passes**

```bash
npm test -- --testPathPattern="useSmartsuppVisitorInfo" --watchAll=false
```

Expected: **PASS**

- [ ] **Step 6: Frontend build**

```bash
npm run build
```

Expected: **PASS**

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useSmartsupp.ts \
        frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts
git commit -m "feat(smartsupp): add useSmartsuppVisitorInfo hook"
```

---

### Task 9: VisitorInfoCard component (TDD)

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/__tests__/VisitorInfoCard.test.tsx`
- Create: `frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx`

- [ ] **Step 1: Write the failing render tests**

Create `frontend/src/components/customer-support/smartsupp/__tests__/VisitorInfoCard.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import VisitorInfoCard from "../VisitorInfoCard";
import { useSmartsuppVisitorInfo } from "../../../../api/hooks/useSmartsupp";

jest.mock("../../../../api/hooks/useSmartsupp", () => ({
  ...jest.requireActual("../../../../api/hooks/useSmartsupp"),
  useSmartsuppVisitorInfo: jest.fn(),
}));

const mockUseVisitorInfo = useSmartsuppVisitorInfo as jest.Mock;

const wrap = (ui: React.ReactNode) => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
};

describe("VisitorInfoCard", () => {
  it("renders nothing while loading", () => {
    mockUseVisitorInfo.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(container.firstChild).toBeNull();
  });

  it("renders nothing when no visitor info", () => {
    mockUseVisitorInfo.mockReturnValue({ data: null, isLoading: false });
    const { container } = render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(container.firstChild).toBeNull();
  });

  it("renders OS and browser in Zařízení section", () => {
    mockUseVisitorInfo.mockReturnValue({
      isLoading: false,
      data: {
        success: true,
        visitorInfo: {
          os: "macOS 10.15.7",
          browser: "Chrome 148.0.0.0",
          visitsCount: 321,
          chatsCount: 3,
          pages: [],
        },
      },
    });
    render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(screen.getByText("Zařízení")).toBeInTheDocument();
    expect(screen.getByText("macOS 10.15.7, Chrome 148.0.0.0")).toBeInTheDocument();
  });

  it("renders browsing history pages", () => {
    mockUseVisitorInfo.mockReturnValue({
      isLoading: false,
      data: {
        success: true,
        visitorInfo: {
          os: null,
          browser: null,
          visitsCount: null,
          chatsCount: 1,
          pages: [
            { url: "https://www.anela.cz/product", title: "Produkt A", viewedAt: null },
            { url: "https://www.anela.cz/checkout", title: null, viewedAt: null },
          ],
        },
      },
    });
    render(wrap(<VisitorInfoCard conversationId="c1" />));
    expect(screen.getByText("Historie procházení")).toBeInTheDocument();
    expect(screen.getByText("Produkt A")).toBeInTheDocument();
    expect(screen.getByText("https://www.anela.cz/checkout")).toBeInTheDocument();
  });

  it("collapses pages beyond 3 and expands on click", () => {
    const pages = Array.from({ length: 5 }, (_, i) => ({
      url: `https://www.anela.cz/page${i + 1}`,
      title: `Stránka ${i + 1}`,
      viewedAt: null,
    }));
    mockUseVisitorInfo.mockReturnValue({
      isLoading: false,
      data: {
        success: true,
        visitorInfo: { os: null, browser: null, visitsCount: null, chatsCount: 1, pages },
      },
    });
    render(wrap(<VisitorInfoCard conversationId="c1" />));

    expect(screen.getByText("Stránka 1")).toBeInTheDocument();
    expect(screen.queryByText("Stránka 5")).not.toBeInTheDocument();
    expect(screen.getByText("+ 2 stránky")).toBeInTheDocument();

    fireEvent.click(screen.getByText("+ 2 stránky"));
    expect(screen.getByText("Stránka 5")).toBeInTheDocument();
    expect(screen.queryByText("+ 2 stránky")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/frontend
npm test -- --testPathPattern="VisitorInfoCard" --watchAll=false
```

Expected: **FAIL** — `VisitorInfoCard` does not exist.

- [ ] **Step 3: Implement VisitorInfoCard.tsx**

Create `frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx`:

```typescript
import React, { useState } from "react";
import { useSmartsuppVisitorInfo, VisitorPageDto } from "../../../api/hooks/useSmartsupp";
import Section from "./Section";

const INITIAL_PAGE_LIMIT = 3;

interface VisitorInfoCardProps {
  conversationId: string | null;
}

function PageRow({ page }: { page: VisitorPageDto }) {
  return (
    <a
      href={page.url}
      target="_blank"
      rel="noopener noreferrer"
      className="block text-xs text-blue-600 hover:underline truncate"
      title={page.url}
    >
      {page.title ?? page.url}
    </a>
  );
}

function VisitorInfoCard({ conversationId }: VisitorInfoCardProps) {
  const { data, isLoading } = useSmartsuppVisitorInfo(conversationId);
  const [expanded, setExpanded] = useState(false);

  if (isLoading) return null;
  if (!data?.visitorInfo) return null;

  const { os, browser, pages } = data.visitorInfo;

  const deviceLabel =
    os && browser ? `${os}, ${browser}` : os ?? browser ?? null;

  const visiblePages = expanded ? pages : pages.slice(0, INITIAL_PAGE_LIMIT);
  const hiddenCount = pages.length - INITIAL_PAGE_LIMIT;

  return (
    <>
      {deviceLabel && (
        <Section title="Zařízení">
          <div className="text-sm text-gray-700">{deviceLabel}</div>
        </Section>
      )}

      {pages.length > 0 && (
        <Section title="Historie procházení">
          <div className="space-y-1">
            {visiblePages.map((p, i) => (
              <PageRow key={i} page={p} />
            ))}
            {!expanded && hiddenCount > 0 && (
              <button
                onClick={() => setExpanded(true)}
                className="text-xs text-gray-500 hover:text-gray-700 mt-0.5"
              >
                + {hiddenCount} {hiddenCount === 1 ? "stránka" : "stránky"}
              </button>
            )}
          </div>
        </Section>
      )}
    </>
  );
}

export default VisitorInfoCard;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
npm test -- --testPathPattern="VisitorInfoCard" --watchAll=false
```

Expected: **PASS** — all 5 tests pass.

- [ ] **Step 5: Build + lint**

```bash
npm run build && npm run lint
```

Expected: **PASS**

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/VisitorInfoCard.test.tsx
git commit -m "feat(smartsupp): add VisitorInfoCard with Zařízení and page history sections"
```

---

### Task 10: Wire ContactDetailsPanel

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx`

- [ ] **Step 1: Add imports to ContactDetailsPanel.tsx**

At the top, after existing imports, add:

```typescript
import VisitorInfoCard from "./VisitorInfoCard";
import { useSmartsuppVisitorInfo } from "../../../api/hooks/useSmartsupp";
```

- [ ] **Step 2: Add hook call inside ContactDetailsPanel**

At the start of the `ContactDetailsPanel` function body, after the existing `const hasKontakt = ...` block:

```typescript
const { data: visitorData, isLoading: visitorLoading } = useSmartsuppVisitorInfo(conversation.id);
const visitorInfo = visitorData?.visitorInfo;
```

- [ ] **Step 3: Update the header div to show visits + chats counts**

In the header section, replace the inner `<div className="min-w-0">` block (currently contains displayName, contactEmail):

```typescript
<div className="min-w-0">
  <div className="font-semibold text-sm text-gray-900 truncate">{displayName}</div>
  {conversation.contactEmail && (
    <div className="text-xs text-gray-500 truncate">{conversation.contactEmail}</div>
  )}
  {(visitorLoading || visitorInfo) && (
    <div className="flex gap-3 mt-1">
      <span className="text-xs text-gray-500" data-testid="visits-count">
        {visitorLoading ? (
          <span className="inline-block w-8 h-2 bg-gray-200 rounded animate-pulse" />
        ) : visitorInfo?.visitsCount != null ? (
          <>Návštěvy <span className="font-medium text-gray-800">{visitorInfo.visitsCount}</span></>
        ) : null}
      </span>
      <span className="text-xs text-gray-500" data-testid="chats-count">
        {visitorLoading ? (
          <span className="inline-block w-8 h-2 bg-gray-200 rounded animate-pulse" />
        ) : visitorInfo?.chatsCount != null ? (
          <>Chaty <span className="font-medium text-gray-800">{visitorInfo.chatsCount}</span></>
        ) : null}
      </span>
    </div>
  )}
</div>
```

- [ ] **Step 4: Add VisitorInfoCard before ShoptetCustomerCard**

After the `{/* Other conversations */}` block and before `{/* Shoptet Zákazník */}`:

```typescript
{/* Visitor info — OS, browser, browsing history */}
<VisitorInfoCard conversationId={conversation.id} />
```

- [ ] **Step 5: Run all frontend tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/phoenix/frontend
npm test -- --watchAll=false
```

Expected: **PASS** — no regressions.

- [ ] **Step 6: Build + lint**

```bash
npm run build && npm run lint
```

Expected: **PASS**

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx
git commit -m "feat(smartsupp): wire VisitorInfoCard and visits/chats header stats into ContactDetailsPanel"
```

---

## Self-review

### Spec coverage

| Spec requirement | Task(s) |
|---|---|
| Spike `GET /visitors/{id}`, document findings | Task 1 |
| `ISmartsuppApiClient.GetVisitorAsync` (bearer, Polly, 404→null) | Tasks 2, 3 |
| `ISmartsuppApiClient.GetVisitorPagesAsync` (404→empty) | Tasks 2, 3 |
| 5 visitor cache columns on `SmartsuppConversation` | Tasks 2, 4 |
| Manual migration (SQL script) | Task 4 |
| `GetVisitorInfoQuery` 24h cache logic | Task 6 |
| Cache miss → API call → persist | Task 6 |
| Cache hit → no API call | Task 6 |
| Stale cache → refresh | Task 6 |
| `chatsCount` via `ListConversationsForContactAsync` | Task 6 |
| Page fallback from `SmartsuppMessage.PageUrl` | Task 6 |
| `GET /api/smartsupp/conversations/{id}/visitor-info` | Task 7 |
| Document endpoint in `docs/features/smartsupp.md` | Task 7 |
| `useSmartsuppVisitorInfo` hook, staleTime ≥10 min | Task 8 |
| Header: "Návštěvy N  Chaty N" with skeleton | Task 10 |
| "Zařízení" section (OS, browser) | Task 9 |
| "Historie procházení" + "+ N stránky" expand | Task 9 |
| API client tests: happy + 404 + 429 | Task 3 |
| Handler tests: all 5 cache scenarios | Task 6 |
| Frontend render tests: loading/loaded/no-data/pages/expand | Task 9 |
| Spike findings documented before use | Task 1 |

### Placeholder scan

No TBDs, no "similar to above", no vague steps — every step has exact code or exact commands.

### Type consistency

- `SmartsuppVisitorData.VisitsCount` → `GetVisitorInfoHandler` → `VisitorInfoDto.VisitsCount` → frontend `visitorInfo.visitsCount` ✓
- `UpdateVisitorCacheAsync` signature: interface (Task 2) = implementation (Task 5) = handler call (Task 6) = test verification (Task 6) ✓
- `VisitorPageDto` (backend) camelCase → frontend `VisitorPageDto` fields match ✓
- `SMARTSUPP_QUERY_KEYS.visitorInfo` used in hook and test ✓
- `GetVisitorInfoRequest.ConversationId` used consistently across handler, controller, and tests ✓
