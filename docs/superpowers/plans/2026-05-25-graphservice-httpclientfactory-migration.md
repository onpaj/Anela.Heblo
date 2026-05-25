# GraphService HttpClientFactory Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the raw `new HttpClient()` instantiation in `GraphService.GetGroupMembersAsync` with an `IHttpClientFactory`-provided client to eliminate socket exhaustion / DNS-staleness risks against `graph.microsoft.com`, preserving all observable behavior.

**Architecture:** Inject `IHttpClientFactory` into `GraphService` as a fourth constructor parameter; register the named client `"MicrosoftGraph"` (literal, matching five sister modules — no shared constant) in the production branch of `UserManagementModule.AddUserManagement`; replace the in-method `using var httpClient = new HttpClient()` with `_httpClientFactory.CreateClient("MicrosoftGraph")` (no `using`), and switch the Graph request from `httpClient.GetAsync(...)` with `DefaultRequestHeaders.Authorization` to a per-request `HttpRequestMessage` with `Headers.Authorization` (defensive against header leaks if the client is later reused). Caching, token acquisition, response parsing, and all `catch` branches stay byte-for-byte identical.

**Tech Stack:** .NET 8, C#, `Microsoft.Extensions.Http`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Identity.Web` (`ITokenAcquisition`), `Microsoft.Extensions.Caching.Memory`, xUnit + FluentAssertions + Moq, existing `Anela.Heblo.Tests.Helpers.FakeHttpMessageHandler` for HTTP-level stubbing.

---

## File Structure

Files touched by this plan:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/
  ├── Services/GraphService.cs                   ← modify constructor + client acquisition + per-request HttpRequestMessage
  └── UserManagementModule.cs                    ← add services.AddHttpClient("MicrosoftGraph") in production branch

backend/test/Anela.Heblo.Tests/Features/UserManagement/
  └── GraphServiceTests.cs                       ← NEW unit test class
```

Responsibilities:

- `GraphService.cs` — the only place that calls Graph over HTTP for the UserManagement module. Owns caching and error translation; depends on `IHttpClientFactory` for client lifetime.
- `UserManagementModule.cs` — DI composition root for the feature. Owns wiring choices between real / mock services and registers the named `HttpClient`.
- `GraphServiceTests.cs` — first unit tests against `GraphService`. Verifies factory usage, cache short-circuit, error-branch fallbacks, no client disposal, and per-request `Authorization` header.

Convention reference (do not modify these — they exist to confirm the pattern):
- `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs:43` — `services.AddHttpClient("MicrosoftGraph");`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs:31`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs:53`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs:31`
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs:208-213` — `CreateRequest` helper using per-request `HttpRequestMessage.Headers.Authorization`.

---

## Architecture Notes (binding decisions from arch-review)

These supersede the spec text where they conflict:

- **No `GraphHttpClientName` constant.** Use the literal `"MicrosoftGraph"` in both `GraphService` and `UserManagementModule`. Sister modules all use the literal; introducing a constant only here creates asymmetric drift. Add a one-line comment at each call site cross-referencing the convention.
- **Per-request `HttpRequestMessage`** with `Headers.Authorization`, not `httpClient.DefaultRequestHeaders.Authorization`. Defensive against header leaks if the named client is reused by future code.
- **Remove the `using` keyword on the factory-provided client.** Disposal is the factory's responsibility; `using` defeats pooling.
- **Constructor parameter order:** append `IHttpClientFactory` as the **fourth** parameter — do not reorder existing parameters (minimize diff blast radius and avoid breaking any reflection-based test wiring).

---

## Known Follow-ups (out of scope for this plan)

These are noted by the arch-review but explicitly NOT part of this change:

- Five sister modules independently register `AddHttpClient("MicrosoftGraph")`; `PhotobankModule` adds a custom handler. With named clients, last registration wins for handler config — registration order is not deterministic. A follow-up should consolidate into a single `AddMicrosoftGraphHttpClient()` extension applying `SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }` uniformly.
- Migration from raw HTTP to `Microsoft.Graph.GraphServiceClient` SDK.
- Polly resilience policies (retry / circuit breaker) on the named client.
- Paging support for `@odata.nextLink`.
- Cache TTL / eager refresh tuning.

---

## Task 1: Add the `IHttpClientFactory` dependency and named-client registration (red — test fails because constructor signature doesn't accept the factory yet)

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` (NEW)

- [ ] **Step 1: Create the test file with a constructor-wiring test that fails because `GraphService` does not yet take `IHttpClientFactory`**

Create `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` with this content:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Moq;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GraphServiceTests
{
    [Fact]
    public void Constructor_Resolves_FromServiceCollection_WhenNamedHttpClientRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(Mock.Of<ITokenAcquisition>());
        services.AddHttpClient("MicrosoftGraph");
        services.AddScoped<IGraphService, GraphService>();

        // Act
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();

        // Assert
        resolved.Should().BeOfType<GraphService>();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails because the constructor does not yet take `IHttpClientFactory`**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.Constructor_Resolves_FromServiceCollection_WhenNamedHttpClientRegistered" --nologo --verbosity minimal
```

Expected: build failure or test failure. The DI container will resolve `GraphService` with its current 3-parameter constructor — the test will pass accidentally if we don't enforce the factory dependency. To make this a true red, the test must require the factory. Update the test before re-running.

- [ ] **Step 3: Tighten the test to require the factory dependency**

Replace the test method body so it asserts the resolved service is constructed via the factory-aware path. Replace the file content with:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Moq;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GraphServiceTests
{
    [Fact]
    public void Constructor_RequiresIHttpClientFactory()
    {
        // Arrange
        var tokenAcquisition = Mock.Of<ITokenAcquisition>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();
        var httpClientFactory = Mock.Of<IHttpClientFactory>();

        // Act
        var service = new GraphService(tokenAcquisition, cache, logger, httpClientFactory);

        // Assert
        service.Should().NotBeNull();
    }
}
```

- [ ] **Step 4: Run the test to verify it fails to compile because `GraphService` has no 4-arg constructor**

Run:
```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo --verbosity minimal
```

Expected: compile error `CS1729: 'GraphService' does not contain a constructor that takes 4 arguments` (or equivalent). This is the RED state.

- [ ] **Step 5: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "test: add failing test requiring IHttpClientFactory on GraphService"
```

---

## Task 2: Implement the constructor change in `GraphService` (green — adds the factory dependency)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:14-29`

- [ ] **Step 1: Add the `_httpClientFactory` field and extend the constructor**

Edit `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`.

Find the existing fields and constructor (lines 14–29):
```csharp
public class GraphService : IGraphService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphService> _logger;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(20);

    public GraphService(
        ITokenAcquisition tokenAcquisition,
        IMemoryCache cache,
        ILogger<GraphService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _cache = cache;
        _logger = logger;
    }
```

Replace with:
```csharp
public class GraphService : IGraphService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(20);

    public GraphService(
        ITokenAcquisition tokenAcquisition,
        IMemoryCache cache,
        ILogger<GraphService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _tokenAcquisition = tokenAcquisition;
        _cache = cache;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }
```

- [ ] **Step 2: Run the test to verify it now compiles and passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.Constructor_RequiresIHttpClientFactory" --nologo --verbosity minimal
```

Expected: 1 test passed.

- [ ] **Step 3: Verify the existing `MockGraphService` and any other consumers still compile**

Run:
```bash
cd backend && dotnet build --nologo --verbosity minimal
```

Expected: build succeeds. (`MockGraphService` does not derive from `GraphService` — it implements `IGraphService` separately and is unaffected.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
git commit -m "feat: add IHttpClientFactory dependency to GraphService"
```

---

## Task 3: Replace `new HttpClient()` with the factory and switch to per-request `HttpRequestMessage`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:83-91`
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] **Step 1: Write the failing test for cache miss invoking the factory exactly once and parsing the canned Graph response**

Open `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` and **add** these usings to the existing imports if missing:

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
```

Then **append** these new test methods inside the `GraphServiceTests` class (after the existing `Constructor_RequiresIHttpClientFactory` method):

```csharp
private const string MicrosoftGraphClientName = "MicrosoftGraph";

private const string SampleGraphResponse = """
{
  "value": [
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "11111111-1111-1111-1111-111111111111",
      "displayName": "Alice Example",
      "mail": "alice@example.com",
      "userPrincipalName": "alice@example.com"
    },
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "22222222-2222-2222-2222-222222222222",
      "displayName": "Bob Example",
      "mail": null,
      "userPrincipalName": "bob@example.com"
    },
    {
      "@odata.type": "#microsoft.graph.group",
      "id": "33333333-3333-3333-3333-333333333333",
      "displayName": "Nested Group"
    }
  ]
}
""";

private static GraphService BuildService(
    HttpMessageHandler handler,
    out Mock<IHttpClientFactory> factoryMock,
    out Mock<ITokenAcquisition> tokenMock,
    out IMemoryCache cache)
{
    factoryMock = new Mock<IHttpClientFactory>();
    factoryMock
        .Setup(f => f.CreateClient(MicrosoftGraphClientName))
        .Returns(() => new HttpClient(handler, disposeHandler: false));

    tokenMock = new Mock<ITokenAcquisition>();
    tokenMock
        .Setup(t => t.GetAccessTokenForAppAsync(
            "https://graph.microsoft.com/.default",
            It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()))
        .ReturnsAsync("test-token");

    cache = new MemoryCache(new MemoryCacheOptions());
    var logger = Mock.Of<ILogger<GraphService>>();

    return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object);
}

[Fact]
public async Task GetGroupMembersAsync_CacheMiss_InvokesFactory_AndReturnsParsedUsers()
{
    // Arrange
    var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleGraphResponse);
    var service = BuildService(handler, out var factoryMock, out _, out _);

    // Act
    var result = await service.GetGroupMembersAsync("group-1");

    // Assert
    factoryMock.Verify(f => f.CreateClient(MicrosoftGraphClientName), Times.Once);
    result.Should().HaveCount(2);
    result[0].Id.Should().Be("11111111-1111-1111-1111-111111111111");
    result[0].DisplayName.Should().Be("Alice Example");
    result[0].Email.Should().Be("alice@example.com");
    result[1].Id.Should().Be("22222222-2222-2222-2222-222222222222");
    result[1].Email.Should().Be("bob@example.com"); // fallback to UPN when mail is null

    handler.LastRequestUri!.ToString()
        .Should().Be("https://graph.microsoft.com/v1.0/groups/group-1/members?$select=id,displayName,mail,userPrincipalName");
    handler.LastMethod.Should().Be(HttpMethod.Get);
}
```

- [ ] **Step 2: Run the test to verify it currently passes (because today's code creates a `new HttpClient()` which would bypass the factory) or fails (because the factory is never called)**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_CacheMiss_InvokesFactory_AndReturnsParsedUsers" --nologo --verbosity minimal
```

Expected: FAIL. `GraphService` still calls `new HttpClient()` (Task 2 only added the constructor dependency); the factory's `CreateClient` is never invoked, so `factoryMock.Verify(..., Times.Once)` fails. The request never hits the `FakeHttpMessageHandler`, so the URI assertion will also fail.

- [ ] **Step 3: Replace `new HttpClient()` with `_httpClientFactory.CreateClient("MicrosoftGraph")` and switch to per-request `HttpRequestMessage`**

Edit `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`.

Find the block (currently lines 83–91):
```csharp
            // Get group members from Microsoft Graph
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

            var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members?$select=id,displayName,mail,userPrincipalName";
            _logger.LogInformation("Making MS Graph API request to: {RequestUrl}", requestUrl);

            var apiCallStart = DateTime.UtcNow;
            var response = await httpClient.GetAsync(requestUrl, cancellationToken);
            var apiCallDuration = DateTime.UtcNow - apiCallStart;
```

Replace with:
```csharp
            // Get group members from Microsoft Graph.
            // Matches the shared "MicrosoftGraph" named client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules.
            var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");

            var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members?$select=id,displayName,mail,userPrincipalName";
            _logger.LogInformation("Making MS Graph API request to: {RequestUrl}", requestUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

            var apiCallStart = DateTime.UtcNow;
            var response = await httpClient.SendAsync(request, cancellationToken);
            var apiCallDuration = DateTime.UtcNow - apiCallStart;
```

Key changes:
- `using var httpClient = new HttpClient();` → `var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");` (no `using` keyword; factory owns disposal).
- `httpClient.DefaultRequestHeaders.Authorization = ...` removed; the header is set on the per-request `HttpRequestMessage` instead.
- `await httpClient.GetAsync(requestUrl, cancellationToken)` → `await httpClient.SendAsync(request, cancellationToken)`.
- `using var request = new HttpRequestMessage(...)` ensures the request itself is disposed (good practice; doesn't touch the pooled client).
- The cross-reference comment makes the convention explicit for future readers.

- [ ] **Step 4: Run the cache-miss test to verify it now passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_CacheMiss_InvokesFactory_AndReturnsParsedUsers" --nologo --verbosity minimal
```

Expected: 1 test passed.

- [ ] **Step 5: Run the full test class to confirm no regression**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests" --nologo --verbosity minimal
```

Expected: all tests in `GraphServiceTests` pass.

- [ ] **Step 6: Run `dotnet build` and `dotnet format --verify-no-changes` to verify no new warnings or formatting drift**

Run:
```bash
cd backend && dotnet build --nologo --verbosity minimal
cd backend && dotnet format --verify-no-changes --verbosity minimal
```

Expected: build succeeds with no new warnings (specifically: no `CA2000`/`CA1816` regressions for `GraphService`); `dotnet format` exits with code 0.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "fix(usermanagement): use IHttpClientFactory and per-request headers in GraphService"
```

---

## Task 4: Register the named `"MicrosoftGraph"` HttpClient in `UserManagementModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs:23-30`
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] **Step 1: Write the failing test that verifies the production wiring registers the named client and resolves `GraphService`**

Open `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` and add these usings if missing:

```csharp
using Microsoft.Extensions.Configuration;
using Anela.Heblo.Application.Features.UserManagement;
```

Append a new test method inside `GraphServiceTests`:

```csharp
[Fact]
public void AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    services.AddSingleton(Mock.Of<ITokenAcquisition>());
    var configuration = new ConfigurationBuilder().Build(); // no mock-auth keys set => production branch

    // Act
    services.AddUserManagement(configuration);

    // Assert
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
    resolved.Should().BeOfType<GraphService>();

    var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("MicrosoftGraph");
    client.Should().NotBeNull();
}
```

- [ ] **Step 2: Run the test to verify it fails because `IHttpClientFactory` is not registered by the module**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService" --nologo --verbosity minimal
```

Expected: FAIL — `InvalidOperationException: Unable to resolve service for type 'System.Net.Http.IHttpClientFactory'` when constructing `GraphService` (the module does not yet call `AddHttpClient`).

- [ ] **Step 3: Add the `AddHttpClient("MicrosoftGraph")` registration in the production branch**

Edit `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`.

Find the `else` block (lines 23–30):
```csharp
        else
        {
            // Register real GraphService for production authentication
            services.AddScoped<IGraphService, GraphService>();

            // Note: GraphServiceClient must be registered in the API layer with proper authentication
            // through Microsoft.Identity.Web's AddMicrosoftGraph() method
        }
```

Replace with:
```csharp
        else
        {
            // Register the named "MicrosoftGraph" HttpClient for IHttpClientFactory.
            // Matches the shared "MicrosoftGraph" named client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules.
            services.AddHttpClient("MicrosoftGraph");

            // Register real GraphService for production authentication
            services.AddScoped<IGraphService, GraphService>();

            // Note: GraphServiceClient must be registered in the API layer with proper authentication
            // through Microsoft.Identity.Web's AddMicrosoftGraph() method
        }
```

- [ ] **Step 4: Run the test to verify it now passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService" --nologo --verbosity minimal
```

Expected: 1 test passed.

- [ ] **Step 5: Verify the mock branch is untouched**

Append a regression test (in the same `GraphServiceTests` class) that proves the mock branch still binds `MockGraphService`:

```csharp
[Fact]
public void AddUserManagement_MockBranch_RegistersMockGraphService()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseMockAuth"] = "true"
        })
        .Build();

    // Act
    services.AddUserManagement(configuration);

    // Assert
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
    resolved.Should().BeOfType<MockGraphService>();
}
```

Note: `ConfigurationConstants.USE_MOCK_AUTH` is defined as `"UseMockAuth"` in `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:13`. The dictionary key in the test matches this constant exactly.

- [ ] **Step 6: Run the mock-branch test**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.AddUserManagement_MockBranch_RegistersMockGraphService" --nologo --verbosity minimal
```

Expected: 1 test passed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "feat(usermanagement): register MicrosoftGraph named HttpClient in production branch"
```

---

## Task 5: Verify cache-hit short-circuit does not touch the factory

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] **Step 1: Write the failing test for cache-hit behavior**

Append inside `GraphServiceTests`:

```csharp
[Fact]
public async Task GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory()
{
    // Arrange
    var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "should not be called");
    var service = BuildService(handler, out var factoryMock, out _, out var cache);

    var cached = new List<Application.Features.UserManagement.Contracts.UserDto>
    {
        new() { Id = "cached-1", DisplayName = "Cached User", Email = "cached@example.com" }
    };
    cache.Set("group_members_group-1", cached, TimeSpan.FromMinutes(20));

    // Act
    var result = await service.GetGroupMembersAsync("group-1");

    // Assert
    result.Should().BeEquivalentTo(cached);
    factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    handler.LastRequestUri.Should().BeNull();
}
```

- [ ] **Step 2: Run the test to verify it passes (cache-hit logic is preserved verbatim by Task 3)**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory" --nologo --verbosity minimal
```

Expected: 1 test passed (no implementation change needed — the cache short-circuit at `GraphService.cs:37-41` is unchanged).

If the test fails, investigate: the cache key must match `group_members_{groupId}` exactly (see `GraphService.cs:34`).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "test: verify cache-hit path does not invoke IHttpClientFactory"
```

---

## Task 6: Verify error branches return empty list (token failure, non-2xx, transport exception)

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] **Step 1: Write failing tests for each failure branch**

Append the following tests inside `GraphServiceTests`. Add `using Microsoft.Identity.Client;` to the file's usings if not already present.

```csharp
[Fact]
public async Task GetGroupMembersAsync_TokenAcquisitionMsalException_ReturnsEmptyList_AndDoesNotInvokeFactory()
{
    // Arrange
    var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleGraphResponse);
    var service = BuildService(handler, out var factoryMock, out var tokenMock, out _);

    tokenMock
        .Setup(t => t.GetAccessTokenForAppAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()))
        .ThrowsAsync(new MsalUiRequiredException("err", "msg"));

    // Act
    var result = await service.GetGroupMembersAsync("group-1");

    // Assert
    result.Should().BeEmpty();
    factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
}

[Fact]
public async Task GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList()
{
    // Arrange
    var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}");
    var service = BuildService(handler, out _, out _, out _);

    // Act
    var result = await service.GetGroupMembersAsync("group-1");

    // Assert
    result.Should().BeEmpty();
}

[Fact]
public async Task GetGroupMembersAsync_TransportThrows_ReturnsEmptyList()
{
    // Arrange
    var throwingHandler = new ThrowingHttpMessageHandler(new HttpRequestException("boom"));
    var service = BuildService(throwingHandler, out _, out _, out _);

    // Act
    var result = await service.GetGroupMembersAsync("group-1");

    // Assert
    result.Should().BeEmpty();
}

[Fact]
public async Task GetGroupMembersAsync_EmptyGroupId_ReturnsEmptyList_WithoutTouchingFactory()
{
    // Arrange
    var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleGraphResponse);
    var service = BuildService(handler, out var factoryMock, out _, out _);

    // Act
    var result = await service.GetGroupMembersAsync("   ");

    // Assert
    result.Should().BeEmpty();
    factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
}
```

- [ ] **Step 2: Add a small in-test helper handler that throws on send (the existing `FakeHttpMessageHandler` only returns canned responses)**

Append a nested helper class **at the end of the `GraphServiceTests` class file** (still inside the namespace, outside the test class). To keep `GraphServiceTests.cs` self-contained, declare it as a `private sealed class` inside `GraphServiceTests`:

```csharp
private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;
    public ThrowingHttpMessageHandler(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw _exception;
}
```

- [ ] **Step 3: Run the failure-branch tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_TokenAcquisitionMsalException_ReturnsEmptyList_AndDoesNotInvokeFactory|FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList|FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_TransportThrows_ReturnsEmptyList|FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_EmptyGroupId_ReturnsEmptyList_WithoutTouchingFactory" --nologo --verbosity minimal
```

Expected: all 4 tests pass without any changes to `GraphService` (the error branches at `GraphService.cs:71-81`, `97-107`, and `171-185` already return `new List<UserDto>()`).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "test: cover GraphService failure branches (token, non-2xx, transport, empty id)"
```

---

## Task 7: Verify the factory-provided client is not disposed by `GraphService`

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] **Step 1: Add a test that fails if `GraphService` disposes the handler returned by the factory**

Append inside `GraphServiceTests`:

```csharp
[Fact]
public async Task GetGroupMembersAsync_DoesNotDispose_FactoryProvidedClient()
{
    // Arrange
    var tracker = new DisposalTrackingHandler(HttpStatusCode.OK, SampleGraphResponse);

    var factoryMock = new Mock<IHttpClientFactory>();
    factoryMock
        .Setup(f => f.CreateClient("MicrosoftGraph"))
        .Returns(() => new HttpClient(tracker, disposeHandler: true));

    var tokenMock = new Mock<ITokenAcquisition>();
    tokenMock
        .Setup(t => t.GetAccessTokenForAppAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()))
        .ReturnsAsync("test-token");

    var cache = new MemoryCache(new MemoryCacheOptions());
    var logger = Mock.Of<ILogger<GraphService>>();
    var service = new GraphService(tokenMock.Object, cache, logger, factoryMock.Object);

    // Act
    await service.GetGroupMembersAsync("group-1");

    // Assert
    tracker.DisposeCount.Should().Be(0,
        "GraphService must not dispose the HttpClient/HttpMessageHandler returned by IHttpClientFactory — disposal is the factory's responsibility, and disposing defeats connection pooling.");
}
```

And add the tracking handler as a `private sealed class` inside `GraphServiceTests`:

```csharp
private sealed class DisposalTrackingHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;
    public int DisposeCount { get; private set; }

    public DisposalTrackingHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
        });

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeCount++;
        base.Dispose(disposing);
    }
}
```

Why this test design: the factory test setup returns a fresh `HttpClient` wrapping the tracking handler with `disposeHandler: true` so that any `using` / `Dispose()` call on the client would cascade into the handler. Task 3 removed the `using` keyword on the client — this test will fail if a future change reintroduces it.

- [ ] **Step 2: Run the disposal test**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_DoesNotDispose_FactoryProvidedClient" --nologo --verbosity minimal
```

Expected: 1 test passed (Task 3 removed the `using` keyword on the factory-provided client).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "test: GraphService must not dispose factory-provided HttpClient"
```

---

## Task 8: Final full-suite validation and formatter check

**Files:** none modified — verification only.

- [ ] **Step 1: Run the entire test suite to confirm no regressions across the solution**

Run:
```bash
cd backend && dotnet test --nologo --verbosity minimal
```

Expected: all tests pass. Pay particular attention to:
- `Anela.Heblo.Tests.Features.UserManagement.*` — must all pass.
- Any test that constructs `GraphService` directly via reflection or via a different DI graph — none expected, but the test runner is the authority.

If any test outside the `UserManagement` namespace fails, inspect whether it touches `GraphService` constructor or `UserManagementModule.AddUserManagement` wiring. The new 4-parameter constructor is the most likely failure surface.

- [ ] **Step 2: Run the formatter and verifier**

Run:
```bash
cd backend && dotnet format --verify-no-changes --verbosity minimal
cd backend && dotnet build --nologo --verbosity minimal
```

Expected: format reports no changes; build succeeds with no new warnings. Look specifically for new `CA2000` (Dispose objects before losing scope) — there should be none on `GraphService.cs` because the factory owns the lifetime.

- [ ] **Step 3: Manual sanity check — confirm `using` keyword is gone from the factory-provided client and the per-request `HttpRequestMessage` is in place**

Run:
```bash
grep -n "new HttpClient\|DefaultRequestHeaders\|HttpRequestMessage\|CreateClient" backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
```

Expected output should include:
- `_httpClientFactory.CreateClient("MicrosoftGraph")` — present.
- `using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);` — present.
- `request.Headers.Authorization = ...` — present.

Expected output should **NOT** include:
- `new HttpClient(` (without arguments) — must be absent in production code path.
- `httpClient.DefaultRequestHeaders.Authorization` — must be absent.
- `using var httpClient` — must be absent.

- [ ] **Step 4: Commit the validation pass if any incidental fixes were required (skip if no changes)**

If steps 1–3 surfaced no required edits, this task produces no commit and is considered complete.

If incidental changes were needed (e.g., `dotnet format` produced diffs unrelated to functional behavior), commit them separately:

```bash
git add backend/
git commit -m "chore: dotnet format pass after GraphService HttpClientFactory migration"
```

---

## Validation Checklist (run before declaring the plan done)

- [ ] `dotnet build` succeeds for the solution with no new warnings.
- [ ] `dotnet test` passes for the entire `Anela.Heblo.Tests` project.
- [ ] `dotnet format --verify-no-changes` reports no changes.
- [ ] `GraphService.cs` no longer contains `new HttpClient()` or `using var httpClient`.
- [ ] `GraphService.cs` uses `_httpClientFactory.CreateClient("MicrosoftGraph")` once and `using var request = new HttpRequestMessage(...)`.
- [ ] `UserManagementModule.cs` production branch calls `services.AddHttpClient("MicrosoftGraph")` before `AddScoped<IGraphService, GraphService>()`.
- [ ] `MockGraphService` is unchanged; mock-auth branch is unchanged.
- [ ] `IGraphService` interface is unchanged.
- [ ] `UserDto` shape is unchanged.
- [ ] Cache key (`group_members_{groupId}`) and 20-minute TTL preserved.
- [ ] All five existing `catch` branches preserved verbatim.
- [ ] `CancellationToken` flows into `SendAsync` and `ReadAsStringAsync`.

## Spec Coverage Cross-Check

| Spec requirement | Implemented in |
|---|---|
| FR-1 — Inject `IHttpClientFactory` into `GraphService` | Task 2 |
| FR-1 — Field `_httpClientFactory`, constructor order preserved (factory appended) | Task 2 |
| FR-1 — No `new HttpClient(...)` in any code path | Task 3 |
| FR-1 — `MockGraphService` unchanged | (no change; verified by Task 4 mock-branch test in Step 5) |
| FR-1 — `IGraphService` interface unchanged | (no change; verified by Validation Checklist) |
| FR-2 — `services.AddHttpClient("MicrosoftGraph")` in production branch | Task 4 |
| FR-2 — Default handler lifetime; no `BaseAddress`/`DefaultRequestHeaders` on the named client | Task 4 |
| FR-3 — Cache key + 20-min TTL preserved | Task 5 (cache-hit test) |
| FR-3 — Token-acquisition flow preserved | Task 3, Task 6 |
| FR-3 — Request URL + query string preserved | Task 3 (URI assertion) |
| FR-3 — Response parsing preserved | Task 3 (parsed users assertion) |
| FR-3 — All `catch` branches preserved | Task 6 |
| FR-3 — `CancellationToken` flow preserved | Task 3 (`SendAsync(request, cancellationToken)`) |
| FR-3 — Per-request `HttpRequestMessage` (arch-review amendment) | Task 3 |
| FR-4 — `GraphService` remains scoped | Task 4 (production branch test) |
| FR-4 — No `using` block on factory-provided client | Task 7 (disposal test) |
| FR-4 — No new analyzer warnings | Task 8 (build check) |
| NFR-1 — Performance (factory pooling) | Indirect — verified by code review of Task 3 |
| NFR-2 — Security (no token logging, scopes unchanged) | No change to logging or scopes |
| NFR-3 — Literal `"MicrosoftGraph"` (arch-review amendment, supersedes spec NFR-3) | Task 3 + Task 4 |
| NFR-4 — Logging preserved | Task 3 (no logging touched) |
| NFR-5 — No public API / DTO / migration / env change | (no change) |
