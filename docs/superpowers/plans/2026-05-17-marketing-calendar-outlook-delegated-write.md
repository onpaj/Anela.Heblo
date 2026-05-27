# Marketing Calendar — Two-Way Outlook Sync via Delegated Writes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the 403 error on marketing calendar writes by switching to delegated (on-behalf-of) tokens, inverting persistence order to Outlook-first, surfacing errors with typed codes, and removing the now-obsolete retry infrastructure.

**Architecture:** Write operations (Create/Update/Delete) call Outlook first using the signed-in user's delegated token via `GetAccessTokenForUserAsync`, then commit to the DB only on success. If Outlook returns 403 the handler returns `MarketingCalendarAccessDenied`; any other error returns `MarketingCalendarSyncFailed`. The API bearer-token authentication stack is configured with `.EnableTokenAcquisitionToCallDownstreamApi()` so the MSAL OBO exchange works for SPA Bearer requests. On a successful Create followed by a DB failure, a compensating Outlook delete is issued.

**Tech Stack:** .NET 8, MediatR, Microsoft.Identity.Web (MSAL OBO), Microsoft Graph REST API v1.0, xUnit 2.9 + FluentAssertions 6 + Moq 4.20, React + TanStack Query, i18next

---

## File Map

| Action | File |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` |
| Modify | `frontend/src/types/errors.ts` |
| Modify | `frontend/src/i18n.ts` |
| Modify | `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingSyncStatus.cs` |
| Delete | `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookSyncRetryHostedService.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionSyncTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Application/Marketing/OutlookCalendarSyncServiceTokenTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` |

---

## Task 1: Add new error codes — backend, frontend enum, and i18n

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:233-237`
- Modify: `frontend/src/types/errors.ts:80-85`
- Modify: `frontend/src/i18n.ts` (marketing calendar errors block)

### Background
The existing Marketing Calendar error codes are 2301 (`MarketingActionNotFound`) and 2302 (`UnauthorizedMarketingAccess`). The new Outlook-specific errors need two more entries in the 23XX range. The frontend error pipeline reads `errorCode` from the response body, maps it via the `ErrorCodes` enum to the i18n key `errors.<EnumName>`, and shows it in a toast. No new hook or modal code is needed — the toast already fires from `client.ts`.

- [ ] **Step 1: Add error codes to backend ErrorCodes.cs**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, after line 237 (`UnauthorizedMarketingAccess = 2302,`), add:

```csharp
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    MarketingCalendarAccessDenied = 2303,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    MarketingCalendarSyncFailed = 2304,
```

- [ ] **Step 2: Add error codes to frontend ErrorCodes enum**

In `frontend/src/types/errors.ts`, after `UnauthorizedMarketingAccess = 2302,` add:

```typescript
  MarketingCalendarAccessDenied = 2303,
  MarketingCalendarSyncFailed = 2304,
```

- [ ] **Step 3: Add Czech translations to i18n.ts**

In `frontend/src/i18n.ts`, inside the `cs.translation.errors` object, after `UnauthorizedMarketingAccess: "..."` add:

```typescript
        MarketingCalendarAccessDenied: "Nemáte oprávnění zapisovat do marketingového kalendáře. Musíte být členem marketingové skupiny.",
        MarketingCalendarSyncFailed: "Nepodařilo se kontaktovat Outlook kalendář. Zkuste to prosím znovu.",
```

- [ ] **Step 4: Build to verify**

```bash
cd backend && dotnet build Anela.Heblo.sln --no-incremental -q
cd frontend && npm run build
```

Expected: both build with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        frontend/src/types/errors.ts \
        frontend/src/i18n.ts
git commit -m "feat(marketing): add MarketingCalendarAccessDenied and MarketingCalendarSyncFailed error codes"
```

---

## Task 2: Wire OBO token acquisition for API Bearer authentication

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs:60`

### Background
`ConfigureRealAuthentication` currently calls `.EnableTokenAcquisitionToCallDownstreamApi().AddInMemoryTokenCaches()` only on the WebApp authentication builder (cookie flow). The `AddMicrosoftIdentityWebApiAuthentication` call (Bearer flow used by the SPA) lacks this chain, so `ITokenAcquisition.GetAccessTokenForUserAsync` cannot perform the OBO exchange when the API receives a Bearer token. Chaining the same methods to the API builder fixes this.

- [ ] **Step 1: Update AuthenticationExtensions.cs**

In `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`, replace line 60:

```csharp
        // Also add API authentication for Bearer tokens (for API clients)
        services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");
```

with:

```csharp
        // Also add API authentication for Bearer tokens (for API clients)
        // Chain OBO so ITokenAcquisition.GetAccessTokenForUserAsync works for delegated writes.
        services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd")
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
```

- [ ] **Step 2: Build to verify compilation**

```bash
cd backend && dotnet build Anela.Heblo.sln --no-incremental -q
```

Expected: 0 errors. `ITokenAcquisition` is already registered; chaining OBO on the API builder is additive and does not break the existing WebApp registration.

- [ ] **Step 3: Run startup integration test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests" -v n
```

Expected: all startup tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs
git commit -m "feat(auth): enable OBO token acquisition on API bearer authentication builder"
```

---

## Task 3: Switch OutlookCalendarSyncService write methods to delegated token (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Marketing/OutlookCalendarSyncServiceTokenTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs`

### Background
`CreateEventAsync`, `UpdateEventAsync`, and `DeleteEventAsync` currently call `GetAccessTokenForAppAsync(GraphScope)`. These must be switched to `GetAccessTokenForUserAsync(new[] { "https://graph.microsoft.com/Group.ReadWrite.All" })`. `ListEventsAsync` stays app-only — group calendar reads work without a member context.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/Marketing/OutlookCalendarSyncServiceTokenTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Moq.Protected;
using System.Security.Claims;

namespace Anela.Heblo.Tests.Application.Marketing;

public class OutlookCalendarSyncServiceTokenTests
{
    private readonly Mock<ITokenAcquisition> _tokenAcquisition = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<IMarketingCategoryMapper> _mapper = new();
    private readonly Mock<ILogger<OutlookCalendarSyncService>> _logger = new();

    private const string DelegatedScope = "https://graph.microsoft.com/Group.ReadWrite.All";
    private const string AppScope = "https://graph.microsoft.com/.default";

    private OutlookCalendarSyncService BuildService()
    {
        var options = Options.Create(new MarketingCalendarOptions
        {
            GroupId = "test-group-id",
            PushEnabled = true,
        });
        _mapper.Setup(x => x.MapToOutlookCategory(It.IsAny<MarketingActionType>())).Returns("Blog");
        return new OutlookCalendarSyncService(
            _tokenAcquisition.Object,
            _httpClientFactory.Object,
            options,
            _mapper.Object,
            _logger.Object);
    }

    private HttpClient BuildHttpClient(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        return new HttpClient(handler.Object);
    }

    private static MarketingAction BuildAction() => new()
    {
        Id = 1,
        Title = "Test",
        ActionType = MarketingActionType.Blog,
        StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "user-1",
        OutlookEventId = "existing-event-id",
    };

    [Fact]
    public async Task CreateEventAsync_UsesDelegatedToken_NotAppToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("delegated-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.Created, @"{""id"":""new-event-id""}"));

        var service = BuildService();
        await service.CreateEventAsync(BuildAction(), CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.Is<IEnumerable<string>>(s => s.Contains(DelegatedScope)),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateEventAsync_UsesDelegatedToken_NotAppToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("delegated-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.OK, "{}"));

        var service = BuildService();
        await service.UpdateEventAsync(BuildAction(), CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.Is<IEnumerable<string>>(s => s.Contains(DelegatedScope)),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteEventAsync_UsesDelegatedToken_NotAppToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("delegated-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.NoContent, ""));

        var service = BuildService();
        await service.DeleteEventAsync("event-id", CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.Is<IEnumerable<string>>(s => s.Contains(DelegatedScope)),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task ListEventsAsync_UsesAppToken_NotDelegatedToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForAppAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("app-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.OK, @"{""value"":[]}"));

        var service = BuildService();
        await service.ListEventsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.Is<string>(s => s == AppScope),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~OutlookCalendarSyncServiceTokenTests" -v n
```

Expected: 3 tests fail (currently all methods call `GetAccessTokenForAppAsync`).

- [ ] **Step 3: Implement delegated token switch in OutlookCalendarSyncService.cs**

In `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs`:

Add a new constant after `GraphScope`:
```csharp
        private const string DelegatedGraphScope = "https://graph.microsoft.com/Group.ReadWrite.All";
```

Change `CreateEventAsync` (line 51) — replace:
```csharp
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
```
with:
```csharp
            var token = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedGraphScope });
```

Change `UpdateEventAsync` (line 82) — replace:
```csharp
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
```
with:
```csharp
            var token = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedGraphScope });
```

Change `DeleteEventAsync` (line 105) — replace:
```csharp
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
```
with:
```csharp
            var token = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedGraphScope });
```

`ListEventsAsync` (line 125) keeps `GetAccessTokenForAppAsync(GraphScope)` — no change.

- [ ] **Step 4: Run tests — verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~OutlookCalendarSyncServiceTokenTests" -v n
```

Expected: 4/4 pass.

- [ ] **Step 5: Build**

```bash
cd backend && dotnet build Anela.Heblo.sln --no-incremental -q
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/OutlookCalendarSyncServiceTokenTests.cs
git commit -m "feat(marketing): switch Outlook write methods to delegated token (OBO)"
```

---

## Task 4: Rewrite Create handler — Outlook-first with DB compensation (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`

### Background
The current handler saves to the DB first, then pushes to Outlook in a best-effort try/catch that marks the action as `Failed`. The new handler:
1. Builds the action in memory.
2. Calls `CreateEventAsync` (delegated). On `OutlookCalendarSyncException` with status 403, returns `MarketingCalendarAccessDenied`; any other status returns `MarketingCalendarSyncFailed`. No DB write happens.
3. Sets `OutlookEventId` via `MarkOutlookSynced`.
4. Saves to DB. If `SaveChangesAsync` throws, issues a compensating `DeleteEventAsync` on the Outlook event (best-effort), logs if the compensating delete also fails, then returns `ErrorCodes.DatabaseError`.

When `PushEnabled = false`, steps 2-3 are skipped and only the DB write runs.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class CreateMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<CreateMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    public CreateMarketingActionHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);

        _repository
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id-abc");
    }

    private CreateMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            Options.Create(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    private static CreateMarketingActionRequest BuildRequest() => new()
    {
        Title = "Test Action",
        ActionType = MarketingActionType.Blog,
        StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Handle_CallsOutlookBeforeDb_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .ReturnsAsync("event-id-abc");

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => callOrder.Add("db"))
            .Returns(Task.CompletedTask);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db");
    }

    [Fact]
    public async Task Handle_ReturnsForbiddenError_WhenOutlookThrows403()
    {
        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.Forbidden, null, "403 Forbidden"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarAccessDenied);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSyncError_WhenOutlookThrowsNon403()
    {
        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.InternalServerError, null, "500"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarSyncFailed);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CompensatesOutlookEvent_WhenDbSaveFails()
    {
        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-to-compensate");

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB unavailable"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
        _outlookSync.Verify(
            x => x.DeleteEventAsync("event-to-compensate", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SetsOutlookEventId_WhenBothSucceed()
    {
        MarketingAction? capturedAction = null;
        _repository
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
            .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedAction!.OutlookEventId.Should().Be("event-id-abc");
        capturedAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenPushDisabled()
    {
        var result = await BuildHandler(pushEnabled: false).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenUserNotAuthenticated()
    {
        _currentUserService
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, IsAuthenticated: false));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedMarketingAccess);
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateMarketingActionHandlerTests" -v n
```

Expected: most tests fail (current handler writes DB before Outlook and swallows Outlook errors).

- [ ] **Step 3: Rewrite CreateMarketingActionHandler.cs**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.CreateMarketingAction
{
    public class CreateMarketingActionHandler : IRequestHandler<CreateMarketingActionRequest, CreateMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CreateMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptions<MarketingCalendarOptions> _options;

        public CreateMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<CreateMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptions<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
        }

        public async Task<CreateMarketingActionResponse> Handle(
            CreateMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new CreateMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var now = DateTime.UtcNow;

            var action = new MarketingAction
            {
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                ActionType = request.ActionType,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = currentUser.Id,
                CreatedByUsername = currentUser.Name ?? "Unknown User",
            };

            if (request.AssociatedProducts?.Any() == true)
                foreach (var product in request.AssociatedProducts.Distinct())
                    action.AssociateWithProduct(product);

            if (request.FolderLinks?.Any() == true)
                foreach (var link in request.FolderLinks)
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);

            string? outlookEventId = null;

            if (_options.Value.PushEnabled)
            {
                try
                {
                    outlookEventId = await _outlookSync.CreateEventAsync(action, cancellationToken);
                    action.MarkOutlookSynced(outlookEventId, now);
                }
                catch (OutlookCalendarSyncException ex)
                {
                    _logger.LogError(ex, "Outlook CreateEvent failed for new MarketingAction");
                    return OutlookError(ex);
                }
            }

            await _repository.AddAsync(action, cancellationToken);
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "DB save failed after Outlook create; compensating Outlook event {EventId}", outlookEventId);

                if (outlookEventId != null)
                {
                    try
                    {
                        await _outlookSync.DeleteEventAsync(outlookEventId, cancellationToken);
                        _logger.LogWarning("Compensating delete of Outlook event {EventId} succeeded", outlookEventId);
                    }
                    catch (Exception compEx)
                    {
                        _logger.LogError(compEx, "Compensating delete of Outlook event {EventId} also failed — event orphaned", outlookEventId);
                    }
                }

                return new CreateMarketingActionResponse(ErrorCodes.DatabaseError);
            }

            _logger.LogInformation("MarketingAction {ActionId} created by user {UserId}", action.Id, currentUser.Id);

            return new CreateMarketingActionResponse { Id = action.Id, CreatedAt = action.CreatedAt };
        }

        private static CreateMarketingActionResponse OutlookError(OutlookCalendarSyncException ex) =>
            ex.StatusCode == HttpStatusCode.Forbidden
                ? new CreateMarketingActionResponse(ErrorCodes.MarketingCalendarAccessDenied)
                : new CreateMarketingActionResponse(ErrorCodes.MarketingCalendarSyncFailed);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateMarketingActionHandlerTests" -v n
```

Expected: 7/7 pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs
git commit -m "feat(marketing): rewrite Create handler — Outlook-first with compensating delete"
```

---

## Task 5: Rewrite Update handler — Outlook-first (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`

### Background
Current handler: saves to DB first, then Outlook in a best-effort try/catch. New handler: applies all field changes in memory → calls `UpdateEventAsync` (or `CreateEventAsync` if no `OutlookEventId`) → on `OutlookCalendarSyncException` returns the typed error, leaving the DB unchanged → on success, calls `UpdateAsync` + `SaveChangesAsync`.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class UpdateMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<UpdateMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    private static MarketingAction BuildExistingAction(string? outlookEventId = "existing-event-id") =>
        new()
        {
            Id = 42,
            Title = "Old Title",
            ActionType = MarketingActionType.Blog,
            StartDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ModifiedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = "user-1",
            OutlookEventId = outlookEventId,
            OutlookSyncStatus = outlookEventId != null ? MarketingSyncStatus.Synced : MarketingSyncStatus.NotSynced,
        };

    private static UpdateMarketingActionRequest BuildRequest(int id = 42) => new()
    {
        Id = id,
        Title = "New Title",
        ActionType = MarketingActionType.Newsletter,
        StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    public UpdateMarketingActionHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction());
        _repository.Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) => a);
        _repository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outlookSync.Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outlookSync.Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-event-id");
    }

    private UpdateMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            Options.Create(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    [Fact]
    public async Task Handle_CallsOutlookBeforeDb_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => callOrder.Add("db"))
            .Returns(Task.CompletedTask);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db");
    }

    [Fact]
    public async Task Handle_ReturnsForbiddenError_WhenOutlookUpdateThrows403()
    {
        _outlookSync
            .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.Forbidden, null, "403"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarAccessDenied);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSyncError_WhenOutlookUpdateThrowsNon403()
    {
        _outlookSync
            .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.BadGateway, null, "502"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarSyncFailed);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreatesOutlookEvent_WhenActionHasNoEventId()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction(outlookEventId: null));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenPushDisabled()
    {
        var result = await BuildHandler(pushEnabled: false).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenActionDoesNotExist()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction?)null);

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingActionNotFound);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests" -v n
```

Expected: most tests fail (current handler saves DB before Outlook).

- [ ] **Step 3: Rewrite UpdateMarketingActionHandler.cs**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.UpdateMarketingAction
{
    public class UpdateMarketingActionHandler : IRequestHandler<UpdateMarketingActionRequest, UpdateMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<UpdateMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptions<MarketingCalendarOptions> _options;

        public UpdateMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<UpdateMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptions<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
        }

        public async Task<UpdateMarketingActionResponse> Handle(
            UpdateMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new UpdateMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new UpdateMarketingActionResponse(ErrorCodes.MarketingActionNotFound,
                    new Dictionary<string, string> { { "actionId", request.Id.ToString() } });
            }

            var now = DateTime.UtcNow;

            action.Title = request.Title.Trim();
            action.Description = request.Description?.Trim();
            action.ActionType = request.ActionType;
            action.StartDate = request.StartDate;
            action.EndDate = request.EndDate;
            action.ModifiedAt = now;
            action.ModifiedByUserId = currentUser.Id;
            action.ModifiedByUsername = currentUser.Name ?? "Unknown User";

            action.ProductAssociations.Clear();
            if (request.AssociatedProducts?.Any() == true)
                foreach (var product in request.AssociatedProducts.Distinct())
                    action.AssociateWithProduct(product);

            action.FolderLinks.Clear();
            if (request.FolderLinks?.Any() == true)
                foreach (var link in request.FolderLinks)
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);

            if (_options.Value.PushEnabled)
            {
                try
                {
                    if (!string.IsNullOrEmpty(action.OutlookEventId))
                    {
                        await _outlookSync.UpdateEventAsync(action, cancellationToken);
                        action.MarkOutlookSynced(action.OutlookEventId, now);
                    }
                    else
                    {
                        var eventId = await _outlookSync.CreateEventAsync(action, cancellationToken);
                        action.MarkOutlookSynced(eventId, now);
                    }
                }
                catch (OutlookCalendarSyncException ex)
                {
                    _logger.LogError(ex, "Outlook push failed for MarketingAction {ActionId}", request.Id);
                    return OutlookError(ex);
                }
            }

            await _repository.UpdateAsync(action, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("MarketingAction {ActionId} updated by user {UserId}", action.Id, currentUser.Id);

            return new UpdateMarketingActionResponse { Id = action.Id, ModifiedAt = action.ModifiedAt };
        }

        private static UpdateMarketingActionResponse OutlookError(OutlookCalendarSyncException ex) =>
            ex.StatusCode == HttpStatusCode.Forbidden
                ? new UpdateMarketingActionResponse(ErrorCodes.MarketingCalendarAccessDenied)
                : new UpdateMarketingActionResponse(ErrorCodes.MarketingCalendarSyncFailed);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests" -v n
```

Expected: 6/6 pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs
git commit -m "feat(marketing): rewrite Update handler — Outlook-first, fail-fast on sync errors"
```

---

## Task 6: Rewrite Delete handler — Outlook-first, 404 as success (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs`

### Background
Current handler: calls Outlook (best-effort), saves `MarkOutlookFailed` if it fails, then always proceeds with soft-delete. New handler: calls `DeleteEventAsync` → if 404, treat as success (event already gone) → any other `OutlookCalendarSyncException` returns an error and the DB soft-delete is NOT performed → on success (or 404), performs soft-delete.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class DeleteMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<DeleteMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    private static MarketingAction BuildExistingAction(string? outlookEventId = "event-abc") =>
        new()
        {
            Id = 7,
            Title = "To Delete",
            ActionType = MarketingActionType.Blog,
            StartDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ModifiedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = "user-1",
            OutlookEventId = outlookEventId,
        };

    private static DeleteMarketingActionRequest BuildRequest(int id = 7) => new() { Id = id };

    public DeleteMarketingActionHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction());
        _repository.Setup(x => x.DeleteSoftAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outlookSync.Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private DeleteMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            Options.Create(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    [Fact]
    public async Task Handle_CallsOutlookDeleteBeforeSoftDelete_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(x => x.DeleteSoftAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string, CancellationToken>((_, _, _, _) => callOrder.Add("db"))
            .Returns(Task.CompletedTask);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db");
    }

    [Fact]
    public async Task Handle_TreatsOutlook404AsSuccess_AndProceedsWithSoftDelete()
    {
        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.NotFound, null, "404"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.DeleteSoftAsync(
            7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsForbiddenError_WhenOutlookThrows403()
    {
        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.Forbidden, null, "403"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarAccessDenied);
        _repository.Verify(x => x.DeleteSoftAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSyncError_WhenOutlookThrowsNon403Non404()
    {
        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.ServiceUnavailable, null, "503"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarSyncFailed);
        _repository.Verify(x => x.DeleteSoftAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenActionHasNoEventId()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction(outlookEventId: null));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(x => x.DeleteSoftAsync(
            7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenPushDisabled()
    {
        var result = await BuildHandler(pushEnabled: false).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(x => x.DeleteSoftAsync(
            7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenActionDoesNotExist()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction?)null);

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingActionNotFound);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests" -v n
```

Expected: most fail (current handler always soft-deletes even when Outlook fails).

- [ ] **Step 3: Rewrite DeleteMarketingActionHandler.cs**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.DeleteMarketingAction
{
    public class DeleteMarketingActionHandler : IRequestHandler<DeleteMarketingActionRequest, DeleteMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<DeleteMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptions<MarketingCalendarOptions> _options;

        public DeleteMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<DeleteMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptions<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
        }

        public async Task<DeleteMarketingActionResponse> Handle(
            DeleteMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new DeleteMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new DeleteMarketingActionResponse(ErrorCodes.MarketingActionNotFound,
                    new Dictionary<string, string> { { "actionId", request.Id.ToString() } });
            }

            if (_options.Value.PushEnabled && !string.IsNullOrEmpty(action.OutlookEventId))
            {
                try
                {
                    await _outlookSync.DeleteEventAsync(action.OutlookEventId, cancellationToken);
                }
                catch (OutlookCalendarSyncException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation(
                        "Outlook event {EventId} already deleted (404); proceeding with soft-delete",
                        action.OutlookEventId);
                }
                catch (OutlookCalendarSyncException ex)
                {
                    _logger.LogError(ex, "Outlook DeleteEvent failed for MarketingAction {ActionId}", request.Id);
                    return OutlookError(ex);
                }
            }

            await _repository.DeleteSoftAsync(
                request.Id, currentUser.Id, currentUser.Name ?? "Unknown User", cancellationToken);

            _logger.LogInformation("MarketingAction {ActionId} deleted by user {UserId}", request.Id, currentUser.Id);

            return new DeleteMarketingActionResponse { Id = request.Id };
        }

        private static DeleteMarketingActionResponse OutlookError(OutlookCalendarSyncException ex) =>
            ex.StatusCode == HttpStatusCode.Forbidden
                ? new DeleteMarketingActionResponse(ErrorCodes.MarketingCalendarAccessDenied)
                : new DeleteMarketingActionResponse(ErrorCodes.MarketingCalendarSyncFailed);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests" -v n
```

Expected: 7/7 pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs
git commit -m "feat(marketing): rewrite Delete handler — Outlook-first, 404 treated as success"
```

---

## Task 7: Remove dead code — retry service, MarkOutlookFailed, Failed sync status

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookSyncRetryHostedService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingSyncStatus.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionSyncTests.cs`

### Background
`OutlookSyncRetryHostedService` polled `GetFailedOutlookSyncAsync` every 5 minutes and retried failed Outlook operations. With the new fail-fast approach there is no `Failed` status to retry. `MarkOutlookFailed` is only called by the retry service and the old handlers (now rewritten). The `Failed = 2` enum value is removed; any DB rows with value 2 must be cleaned up before deploying (see Note below).

**Note on DB cleanup (manual step, run before deploy):**
```sql
UPDATE "MarketingActions" SET "OutlookSyncStatus" = 0, "OutlookSyncError" = NULL
WHERE "OutlookSyncStatus" = 2;
```

- [ ] **Step 1: Delete OutlookSyncRetryHostedService.cs**

```bash
rm backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookSyncRetryHostedService.cs
```

- [ ] **Step 2: Remove AddHostedService from MarketingModule.cs**

In `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`, remove line 51:

```csharp
            services.AddHostedService<OutlookSyncRetryHostedService>();
```

- [ ] **Step 3: Remove GetFailedOutlookSyncAsync from IMarketingActionRepository.cs**

In `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`, remove line 22:

```csharp
        Task<List<MarketingAction>> GetFailedOutlookSyncAsync(int batchSize, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Remove GetFailedOutlookSyncAsync implementation from MarketingActionRepository.cs**

In `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`, delete the entire `GetFailedOutlookSyncAsync` method (lines 131-139).

- [ ] **Step 5: Remove MarkOutlookFailed from MarketingAction.cs**

In `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`, delete the `MarkOutlookFailed` method (lines 126-135):

```csharp
        public void MarkOutlookFailed(string? error, DateTime utcNow)
        {
            const int maxErrorLength = 1000;

            OutlookSyncStatus = MarketingSyncStatus.Failed;
            OutlookLastAttemptAt = utcNow;
            OutlookSyncError = error?.Length > maxErrorLength
                ? error[..maxErrorLength]
                : error;
        }
```

- [ ] **Step 6: Remove Failed from MarketingSyncStatus.cs**

In `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingSyncStatus.cs`, remove `Failed = 2,` so the enum becomes:

```csharp
namespace Anela.Heblo.Domain.Features.Marketing
{
    public enum MarketingSyncStatus
    {
        NotSynced = 0,
        Synced = 1,
    }
}
```

- [ ] **Step 7: Update MarketingActionSyncTests.cs — remove MarkOutlookFailed tests**

In `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionSyncTests.cs`, delete the two tests that reference `MarkOutlookFailed`:
- `MarkOutlookFailed_SetsFailedStatusAndTruncatesError_WhenErrorIsLong` (lines 54-68)
- `MarkOutlookFailed_KeepsEventId_WhenFailed` (lines 70-85)

- [ ] **Step 8: Build — verify no references remain**

```bash
cd backend && dotnet build Anela.Heblo.sln --no-incremental -q 2>&1 | grep -E "error|warning" | head -20
```

Expected: 0 errors. If any error references `MarkOutlookFailed`, `GetFailedOutlookSyncAsync`, `MarketingSyncStatus.Failed`, or `OutlookSyncRetryHostedService`, fix it before continuing.

- [ ] **Step 9: Run all Marketing tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Marketing" -v n
```

Expected: all pass.

- [ ] **Step 10: Run full test suite**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n
```

Expected: no new failures.

- [ ] **Step 11: Commit**

```bash
git add -u
git commit -m "refactor(marketing): remove retry service, MarkOutlookFailed, and Failed sync status"
```

---

## Task 8: Final validation

- [ ] **Step 1: Backend build + format**

```bash
cd backend && dotnet build Anela.Heblo.sln --no-incremental -q
cd backend && dotnet format Anela.Heblo.sln --verify-no-changes
```

If `dotnet format` reports diffs, run `dotnet format Anela.Heblo.sln` to apply, then `git add -u && git commit -m "style: apply dotnet format"`.

- [ ] **Step 2: Frontend build + lint**

```bash
cd frontend && npm run build
cd frontend && npm run lint
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Run complete test suite**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n 2>&1 | tail -20
```

Expected: all existing tests pass, new Marketing tests included.

- [ ] **Step 4: Verify coverage on changed code**

Run coverage for the Marketing test area:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Marketing" \
  --collect:"XPlat Code Coverage" --results-directory ./coverage-out -v q
```

Inspect `coverage-out/**/coverage.cobertura.xml`. Changed source files (`OutlookCalendarSyncService.cs`, three handlers) should show ≥80% line coverage.

---

## Azure manual step (outside code change)

Per the spec, the `Heblo-Service` app registration needs the **delegated** `Group.ReadWrite.All` Microsoft Graph permission with admin consent. This is a one-time Azure Portal action by an admin — not a code change. Document in `docs/integrations/` once done.

---

## Spec coverage check (self-review)

| Spec section | Covered by task |
|---|---|
| Section 1 — Outlook-first Create | Task 4 |
| Section 1 — Outlook-first Update | Task 5 |
| Section 1 — Outlook-first Delete (404 = success) | Task 6 |
| Section 1 — PushEnabled=false skip | Tasks 4, 5, 6 |
| Section 2 — Compensating delete on DB failure | Task 4 |
| Section 3 — 403 → MarketingCalendarAccessDenied | Tasks 1, 4, 5, 6 |
| Section 3 — Other → MarketingCalendarSyncFailed | Tasks 1, 4, 5, 6 |
| Section 3 — Modal stays open | Already works (catch doesn't call onClose) |
| Section 3 — Toast shows localized message | Task 1 (i18n + ErrorCodes enum) |
| Section 4 — GetAccessTokenForUserAsync for writes | Task 3 |
| Section 4 — GetAccessTokenForAppAsync for reads | Task 3 |
| Section 4 — OBO wiring on API builder | Task 2 |
| Section 5 — Remove OutlookSyncRetryHostedService | Task 7 |
| Section 5 — Remove GetFailedOutlookSyncAsync | Task 7 |
| Section 5 — Remove MarkOutlookFailed | Task 7 |
| Section 5 — Remove Failed sync status | Task 7 |
| Section 6 — Handler unit tests | Tasks 4, 5, 6 |
| Section 6 — Service delegated-token tests | Task 3 |
| Section 6 — ≥80% coverage on changed code | Task 8 |
