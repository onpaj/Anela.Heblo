# Marketing Calendar Outlook Sync Job Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the Heblo marketing calendar mirrored from the Outlook group calendar automatically — hourly Hangfire job that creates, updates, and (after confirming with Graph) soft-deletes actions, sharing one code path with the existing manual "Import z Outlooku" button.

**Architecture:** The body of `ImportFromOutlookHandler` moves into a new scoped `MarketingCalendarSyncService.SyncAsync(from, to, actor, dryRun)`. The handler keeps its auth check and delegates with the current user; a new `MarketingCalendarSyncJob : IRecurringJob` delegates with a system actor. Reconciliation (orphan → `GetEventAsync` → 404 ⇒ soft-delete, found ⇒ update) and restore-on-reappearance live in the service so both callers get them.

**Tech Stack:** .NET 8, MediatR, Hangfire (`IRecurringJob` auto-discovery), EF Core, Moq + FluentAssertions + xUnit; React + react-query, Jest/RTL; NSwag-generated TS client.

**Spec:** `docs/superpowers/specs/2026-09-03-marketing-calendar-outlook-sync-job-design.md`

One simplification versus the spec: the service returns the existing `ImportFromOutlookResponse` directly instead of a new `MarketingSyncResult` type — it is a plain counts/items DTO already, and a second identical type would only add a mapping step.

## Global Constraints

- DTOs are classes, never C# records (`MarketingCalendar` contracts). `SyncActor` is an internal domain value, not a DTO — a record is fine there.
- Every `*Response` inherits `BaseResponse` (reflection contract test enforces it).
- Surgical changes: touch only what the task requires; match surrounding style (4-space C# in namespace blocks, single quotes in TSX, hard-coded Czech strings in the marketing modal — it does not use i18n).
- Sync window constants: `PastDays = 30`, `FutureMonths = 12`. Cron `0 * * * *`, job name `marketing-calendar-sync`.
- System actor: `UserId = "system"`, `Username = "Outlook sync"`.
- Run backend tests from the repo root: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~<ClassName>"`. If a run hangs at 0 % CPU (another worktree is building), run `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false` once, then re-run the test with `--no-build`.
- Commit after every task with a conventional message; every commit message ends with:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_017PADdoqjc1nhRn4UtdhAG9
  ```
- Before declaring done: `dotnet build`, `dotnet format`, `CI=false npm run build` + `npm run lint` in `frontend/`, and all touched tests green.

---

## File map

| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` | add `Restore(...)` |
| `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` | add `GetSyncedInWindowAsync` |
| `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` | implement it |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs` | add `GetEventAsync` |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs` | no-op `GetEventAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` | Graph `GetEventAsync` |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Services/SyncActor.cs` | who performs a sync |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IMarketingCalendarSyncService.cs` | service contract |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` | the sync loop + reconciliation |
| `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` | `CurrentUser` → `SyncActor` |
| `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs` | auth + delegate |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/ImportFromOutlookRequest.cs` | `Deleted`, new statuses |
| `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` | register service |
| `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/Jobs/MarketingCalendarSyncJob.cs` | hourly job |
| `frontend/src/api/hooks/useMarketingCalendar.ts` | `deleted` in result type |
| `frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx` | show `Smazáno` |

---

### Task 1: `MarketingAction.Restore`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` (after `SoftDelete`, ~line 214)
- Test: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRestoreTests.cs`

**Interfaces:**
- Produces: `public void Restore(string userId, string username, DateTime utcNow)` — clears `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername`; sets `ModifiedAt/ModifiedByUserId/ModifiedByUsername`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionRestoreTests
    {
        private static readonly DateTime FixedUtcNow =
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        private static MarketingAction CreateDeletedAction()
        {
            var action = new MarketingActionTestBuilder()
                .WithTitle("Test Action")
                .WithStartDate(FixedUtcNow)
                .WithCreatedAt(FixedUtcNow)
                .WithModifiedAt(FixedUtcNow)
                .WithCreatedBy("user-1")
                .Build();
            action.SoftDelete("system", "Outlook sync", FixedUtcNow);
            return action;
        }

        [Fact]
        public void Restore_ClearsAllDeletionFields()
        {
            // Arrange
            var action = CreateDeletedAction();
            var restoredAt = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc);

            // Act
            action.Restore("user-7", "Restorer", restoredAt);

            // Assert
            action.IsDeleted.Should().BeFalse();
            action.DeletedAt.Should().BeNull();
            action.DeletedByUserId.Should().BeNull();
            action.DeletedByUsername.Should().BeNull();
        }

        [Fact]
        public void Restore_StampsModifiedFieldsWithActorAndUtcNow()
        {
            // Arrange
            var action = CreateDeletedAction();
            var restoredAt = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc);

            // Act
            action.Restore("user-7", "Restorer", restoredAt);

            // Assert
            action.ModifiedAt.Should().Be(restoredAt);
            action.ModifiedByUserId.Should().Be("user-7");
            action.ModifiedByUsername.Should().Be("Restorer");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingActionRestoreTests"`
Expected: build error `'MarketingAction' does not contain a definition for 'Restore'`.

- [ ] **Step 3: Implement**

Insert directly after the `SoftDelete` method in `MarketingAction.cs`:

```csharp
        public void Restore(string userId, string username, DateTime utcNow)
        {
            IsDeleted = false;
            DeletedAt = null;
            DeletedByUserId = null;
            DeletedByUsername = null;
            ModifiedAt = utcNow;
            ModifiedByUserId = userId;
            ModifiedByUsername = username;
        }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingActionRestoreTests"`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRestoreTests.cs
git commit -m "feat: add MarketingAction.Restore for reversible sync deletions"
```

---

### Task 2: Repository `GetSyncedInWindowAsync`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` (append after `GetByOutlookEventIdsAsync`)
- Test: `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs`

**Interfaces:**
- Produces: `Task<List<MarketingAction>> GetSyncedInWindowAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)` — non-deleted actions with non-null `OutlookEventId` and `fromUtc <= StartDate <= toUtc`. Does **not** include navigation collections (reconciliation never touches them).

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Repositories;

public class MarketingActionRepositoryGetSyncedInWindowTests : IDisposable
{
    private static readonly DateTime WindowFrom = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InsideWindow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OutsideWindow = new(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _context;
    private readonly MarketingActionRepository _repository;

    public MarketingActionRepositoryGetSyncedInWindowTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new MarketingActionRepository(_context, NullLogger<MarketingActionRepository>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetSyncedInWindowAsync_ReturnsOnlySyncedNonDeletedActionsInsideWindow()
    {
        // Arrange
        var inside = await SeedAsync(startDate: InsideWindow, outlookEventId: "evt-inside");
        await SeedAsync(startDate: OutsideWindow, outlookEventId: "evt-outside");
        await SeedAsync(startDate: InsideWindow, outlookEventId: null);
        await SeedAsync(startDate: InsideWindow, outlookEventId: "evt-deleted", deleted: true);

        // Act
        var result = await _repository.GetSyncedInWindowAsync(WindowFrom, WindowTo);

        // Assert
        result.Should().ContainSingle(a => a.Id == inside.Id);
    }

    [Fact]
    public async Task GetSyncedInWindowAsync_IncludesActionsOnWindowBoundaries()
    {
        // Arrange
        await SeedAsync(startDate: WindowFrom, outlookEventId: "evt-start");
        await SeedAsync(startDate: WindowTo, outlookEventId: "evt-end");

        // Act
        var result = await _repository.GetSyncedInWindowAsync(WindowFrom, WindowTo);

        // Assert
        result.Should().HaveCount(2);
    }

    private async Task<MarketingAction> SeedAsync(DateTime startDate, string? outlookEventId, bool deleted = false)
    {
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: startDate,
            endDate: null,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);

        if (outlookEventId is not null)
        {
            action.MarkOutlookSynced(outlookEventId, DateTime.UtcNow);
        }

        if (deleted)
        {
            action.SoftDelete("seed-user", "Seeder", DateTime.UtcNow);
        }

        _context.Set<MarketingAction>().Add(action);
        await _context.SaveChangesAsync();
        return action;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingActionRepositoryGetSyncedInWindowTests"`
Expected: build error — `GetSyncedInWindowAsync` not defined.

- [ ] **Step 3: Implement**

`IMarketingActionRepository.cs` — add after `GetByOutlookEventIdsAsync`:

```csharp
        /// <summary>
        /// Non-deleted actions linked to an Outlook event whose StartDate lies within
        /// [fromUtc, toUtc]. Used to find actions whose event no longer exists in Outlook.
        /// </summary>
        Task<List<MarketingAction>> GetSyncedInWindowAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default);
```

`MarketingActionRepository.cs` — add after `GetByOutlookEventIdsAsync`:

```csharp
        public async Task<List<MarketingAction>> GetSyncedInWindowAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
        {
            return await Context.Set<MarketingAction>()
                .Where(x => !x.IsDeleted &&
                    x.OutlookEventId != null &&
                    x.StartDate >= fromUtc &&
                    x.StartDate <= toUtc)
                .ToListAsync(cancellationToken);
        }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingActionRepositoryGetSyncedInWindowTests"`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs
git commit -m "feat: add GetSyncedInWindowAsync to marketing action repository"
```

---

### Task 3: `IOutlookCalendarSync.GetEventAsync`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` (after `ListEventsAsync`)
- Test: `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs` (append a `GetEventAsync` section)

**Interfaces:**
- Produces: `Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct)` — `null` on HTTP 404, the event on 2xx, `OutlookCalendarSyncException` otherwise. Uses the app token (same as `ListEventsAsync`). Note: the `NoOpOutlookCalendarSync` implementation throws rather than returning `null`, since `null` is the "confirmed deleted" signal.

- [ ] **Step 1: Write the failing tests**

Append inside the `OutlookCalendarSyncServiceTests` class, before the `BuildEventBody` section:

```csharp
        // ─── GetEventAsync ────────────────────────────────────────────────────────

        [Fact]
        public async Task GetEventAsync_WhenFound_ReturnsParsedEvent()
        {
            // Arrange
            var graphResponse = new
            {
                id = "evt-a",
                subject = "Promotion Week",
                body = new { content = "Body text", contentType = "text" },
                start = new { dateTime = "2026-04-01T08:00:00.0000000", timeZone = "UTC" },
                end = new { dateTime = "2026-04-07T18:00:00.0000000", timeZone = "UTC" },
                categories = new[] { "Promotion" }
            };
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(graphResponse));
            var service = CreateService(handler);

            // Act
            var result = await service.GetEventAsync("evt-a", CancellationToken.None);

            // Assert
            handler.LastMethod.Should().Be(HttpMethod.Get);
            handler.LastRequestUri!.ToString().Should().Contain("/calendar/events/evt-a");
            handler.LastRequestUri.ToString().Should().Contain("$select=");
            result.Should().NotBeNull();
            result!.Id.Should().Be("evt-a");
            result.Subject.Should().Be("Promotion Week");
            result.Categories.Should().Contain("Promotion");
        }

        [Fact]
        public async Task GetEventAsync_WhenNotFound_ReturnsNull()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"ErrorItemNotFound\"}}");
            var service = CreateService(handler);

            // Act
            var result = await service.GetEventAsync("evt-gone", CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetEventAsync_WhenServerError_ThrowsOutlookCalendarSyncException()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"Boom\"}}");
            var service = CreateService(handler);

            // Act
            var act = async () => await service.GetEventAsync("evt-x", CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<OutlookCalendarSyncException>();
        }

        [Fact]
        public async Task GetEventAsync_UsesAppToken()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{}");
            var service = CreateService(handler);

            // Act
            await service.GetEventAsync("evt-x", CancellationToken.None);

            // Assert
            _tokenAcquisition.Verify(
                t => t.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default", null, null),
                Times.Once);
            handler.LastRequestHeaders!.Authorization!.Parameter.Should().Be(FakeToken);
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~OutlookCalendarSyncServiceTests.GetEventAsync"`
Expected: build error — `GetEventAsync` not defined.

- [ ] **Step 3: Implement**

`IOutlookCalendarSync.cs` — add:

```csharp
        /// <summary>
        /// Fetches a single event by id. Returns <c>null</c> when Graph reports 404
        /// (the event was deleted); throws <see cref="OutlookCalendarSyncException"/> on other failures.
        /// </summary>
        Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct);
```

`NoOpOutlookCalendarSync.cs` — add:

```csharp
        public Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth active (UseMockAuth or BypassJwtValidation)) — returning null for GetEvent {OutlookEventId}", outlookEventId);
            return Task.FromResult<OutlookEventDto?>(null);
        }
```

`OutlookCalendarSyncService.cs` — add after `ListEventsAsync`:

```csharp
        public async Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct)
        {
            _logger.LogDebug("Fetching Outlook event {EventId} in mailbox {Mailbox}", outlookEventId, _options.GroupId);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var select = "id,subject,body,start,end,categories";
            var url = $"{BuildBaseUrl()}/{Uri.EscapeDataString(outlookEventId)}?$select={select}";
            var request = CreateRequest(HttpMethod.Get, url, token);

            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Outlook event {EventId} not found (404)", outlookEventId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                await ThrowCalendarSyncException(response, "GetEvent", ct);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<OutlookEventDto>(stream, JsonOptions, ct)
                ?? throw new InvalidOperationException("Graph GetEvent response deserialised to null.");
        }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~OutlookCalendarSyncServiceTests"`
Expected: all pass (existing + 4 new).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs
git commit -m "feat: add GetEventAsync to Outlook calendar sync adapter"
```

---

### Task 4: Extract `MarketingCalendarSyncService` (behaviour-preserving)

This task moves code; behaviour must be identical. The existing `ImportFromOutlookHandlerTests` are the safety net — they must pass unchanged at the end of this task except for constructor wiring.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/SyncActor.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IMarketingCalendarSyncService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/ImportFromOutlookRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs` (constructor wiring only)

**Interfaces:**
- Produces: `SyncActor` record `(string UserId, string Username)` with `SyncActor.System` and `SyncActor.FromUser(CurrentUser)`.
- Produces: `IMarketingCalendarSyncService.SyncAsync(DateTime fromUtc, DateTime toUtc, SyncActor actor, bool dryRun, CancellationToken ct) : Task<ImportFromOutlookResponse>`.
- Produces: `ImportFromOutlookResponse.Deleted` (int), `ImportStatus.Deleted = "Deleted"`, `ImportStatus.WouldDelete = "WouldDelete"` — unused until Task 5.
- `OutlookEventImportMapper` stays where it is (same namespace, still `internal static`) and takes `SyncActor` instead of `CurrentUser`; the service imports its namespace.

- [ ] **Step 1: Add `SyncActor`**

```csharp
using System;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    /// <summary>
    /// Who is performing an Outlook → Heblo sync; stamped into CreatedBy/ModifiedBy/DeletedBy.
    /// </summary>
    public sealed record SyncActor(string UserId, string Username)
    {
        public const string SystemUserId = "system";

        public static readonly SyncActor System = new(SystemUserId, "Outlook sync");

        public static SyncActor FromUser(CurrentUser user)
        {
            var userId = user.Id
                ?? throw new InvalidOperationException(
                    "Outlook import requires an authenticated user context.");

            return new SyncActor(userId, user.Name ?? "Unknown User");
        }
    }
}
```

- [ ] **Step 2: Extend the contracts**

In `ImportFromOutlookRequest.cs`:

```csharp
    public class ImportFromOutlookResponse : BaseResponse
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int Deleted { get; set; }
        // ...rest unchanged
```

and

```csharp
    public static class ImportStatus
    {
        public const string Created = "Created";
        public const string WouldCreate = "WouldCreate";
        public const string Updated = "Updated";
        public const string WouldUpdate = "WouldUpdate";
        public const string Deleted = "Deleted";
        public const string WouldDelete = "WouldDelete";
        public const string Skipped = "Skipped";
        public const string Failed = "Failed";
    }
```

Update the `ImportedItemDto.Status` XML doc to list `Deleted` and `WouldDelete` too.

- [ ] **Step 3: Switch the mapper to `SyncActor`**

In `OutlookEventImportMapper.cs`: drop `using Anela.Heblo.Domain.Features.Users;` (keep the existing `using Anela.Heblo.Application.Features.Marketing.Services;` — `SyncActor` lives there) and replace the two `CurrentUser currentUser` parameters:

```csharp
        internal static MarketingAction BuildAction(
            OutlookEventDto evt,
            SyncActor actor,
            DateTime utcNow,
            MarketingActionType actionType)
        {
            var action = new MarketingAction(
                title: ParseTitle(evt.Subject),
                description: ParseDescription(evt.BodyText),
                actionType: actionType,
                startDate: evt.StartUtc,
                endDate: ParseEndDate(evt),
                createdByUserId: actor.UserId,
                createdByUsername: actor.Username,
                utcNow: utcNow);

            action.MarkOutlookSynced(evt.Id, utcNow);

            return action;
        }
```

```csharp
        internal static void ApplyChanges(
            MarketingAction existing,
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncActor actor,
            DateTime utcNow)
        {
            existing.UpdateDetails(
                title: ParseTitle(evt.Subject),
                description: ParseDescription(evt.BodyText),
                actionType: actionType,
                startDate: evt.StartUtc,
                endDate: ParseEndDate(evt),
                modifiedByUserId: actor.UserId,
                modifiedByUsername: actor.Username,
                utcNow: utcNow);

            existing.MarkOutlookSynced(evt.Id, utcNow);
        }
```

(The `?? throw` null-Id guards are gone — `SyncActor.FromUser` now owns that check.) `HasChanges` and the private parsers are unchanged.

- [ ] **Step 4: Create the service interface and implementation**

`IMarketingCalendarSyncService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    /// <summary>
    /// Mirrors the Outlook group calendar into Heblo marketing actions for a date window.
    /// Outlook is the source of truth; Heblo-only actions (no OutlookEventId) are never touched.
    /// </summary>
    public interface IMarketingCalendarSyncService
    {
        Task<ImportFromOutlookResponse> SyncAsync(
            DateTime fromUtc,
            DateTime toUtc,
            SyncActor actor,
            bool dryRun,
            CancellationToken cancellationToken);
    }
}
```

`MarketingCalendarSyncService.cs` — the handler body, restructured into small methods but with identical behaviour:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public class MarketingCalendarSyncService : IMarketingCalendarSyncService
    {
        private readonly IMarketingActionRepository _repository;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IMarketingCategoryMapper _mapper;
        private readonly ILogger<MarketingCalendarSyncService> _logger;

        public MarketingCalendarSyncService(
            IMarketingActionRepository repository,
            IOutlookCalendarSync outlookSync,
            IMarketingCategoryMapper mapper,
            ILogger<MarketingCalendarSyncService> logger)
        {
            _repository = repository;
            _outlookSync = outlookSync;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ImportFromOutlookResponse> SyncAsync(
            DateTime fromUtc,
            DateTime toUtc,
            SyncActor actor,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            var events = await _outlookSync.ListEventsAsync(fromUtc, toUtc, cancellationToken);

            var eventIds = events.Select(e => e.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
            var existingActions = await _repository.GetByOutlookEventIdsAsync(eventIds, cancellationToken);
            var existingByEventId = existingActions
                .GroupBy(a => a.OutlookEventId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var run = new SyncRun(actor, dryRun, DateTime.UtcNow);

            foreach (var evt in events)
            {
                try
                {
                    await ProcessEventAsync(evt, existingByEventId, run, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to import Outlook event {EventId} (subject: {Subject})",
                        evt.Id,
                        evt.Subject);
                    run.AddFailed(evt.Id, evt.Subject, ex.Message);
                }
            }

            await PersistAsync(run, cancellationToken);
            run.ReportStaged();

            var response = run.Response;
            response.UnmappedCategories = run.UnmappedCategories.ToList();

            if (run.UnmappedCategories.Count > 0)
            {
                _logger.LogInformation(
                    "Marketing import completed with {Count} unmapped Outlook categor{Plural}: {Categories}",
                    run.UnmappedCategories.Count,
                    run.UnmappedCategories.Count == 1 ? "y" : "ies",
                    string.Join(", ", run.UnmappedCategories));
            }

            return response;
        }

        private async Task ProcessEventAsync(
            OutlookEventDto evt,
            IReadOnlyDictionary<string, MarketingAction> existingByEventId,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());

            if (mapping.MatchedCategory is null && mapping.UnmappedCategories.Count > 0)
            {
                foreach (var name in mapping.UnmappedCategories)
                {
                    run.UnmappedCategories.Add(name);
                }
            }

            if (existingByEventId.TryGetValue(evt.Id, out var existing))
            {
                await StageUpdateAsync(existing, evt, mapping.ActionType, run, cancellationToken);
                return;
            }

            await StageCreateAsync(evt, mapping.ActionType, run, cancellationToken);
        }

        private async Task StageUpdateAsync(
            MarketingAction existing,
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            if (!OutlookEventImportMapper.HasChanges(existing, evt, actionType))
            {
                run.AddSkipped(evt);
                return;
            }

            OutlookEventImportMapper.ApplyChanges(existing, evt, actionType, run.Actor, run.UtcNow);

            if (run.DryRun)
            {
                run.AddWouldUpdate(evt);
                return;
            }

            // AddAsync/UpdateAsync stay per-event (not deferred to PersistAsync) so a
            // failure here is caught by this event's own try/catch in SyncAsync and
            // only that event is reported Failed — the rest of the batch still commits.
            await _repository.UpdateAsync(existing, cancellationToken);
            run.PendingUpdates.Add((existing, evt));
        }

        private async Task StageCreateAsync(
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var action = OutlookEventImportMapper.BuildAction(evt, run.Actor, run.UtcNow, actionType);

            if (run.DryRun)
            {
                run.AddWouldCreate(evt);
                return;
            }

            await _repository.AddAsync(action, cancellationToken);
            run.PendingCreates.Add((action, evt));
        }

        // Persistence is deferred until the loop completes so that a single
        // SaveChangesAsync covers the whole run. Saving per-event used to leave the
        // shared DbContext dirty after a failed save, poisoning every subsequent
        // event in the run (and costing N round-trips). AddAsync/UpdateAsync
        // themselves are NOT deferred here — they run inline, per-event, inside
        // StageCreateAsync/StageUpdateAsync (and, from Task 5, inline in
        // ReconcileOrphanAsync for deletes) so that one event's repository
        // failure is caught by that event's own try/catch in SyncAsync and only
        // that event is reported Failed, instead of aborting the whole batch.
        private async Task PersistAsync(SyncRun run, CancellationToken cancellationToken)
        {
            if (run.DryRun || !run.HasPendingWrites)
            {
                return;
            }

            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // The batch is atomic: if the single save fails, none of the staged
                // writes were persisted. Report them all as failed instead of
                // claiming success for unwritten rows.
                _logger.LogError(ex,
                    "Failed to persist Outlook import batch of {Count} action(s); no changes were saved",
                    run.PendingCount);

                run.FailAllPending(ex.Message);
            }
        }

        /// <summary>Mutable bookkeeping for one SyncAsync call.</summary>
        private sealed class SyncRun
        {
            public SyncRun(SyncActor actor, bool dryRun, DateTime utcNow)
            {
                Actor = actor;
                DryRun = dryRun;
                UtcNow = utcNow;
            }

            public SyncActor Actor { get; }
            public bool DryRun { get; }
            public DateTime UtcNow { get; }
            public ImportFromOutlookResponse Response { get; } = new();
            public HashSet<string> UnmappedCategories { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<(MarketingAction action, OutlookEventDto evt)> PendingCreates { get; } = new();
            public List<(MarketingAction action, OutlookEventDto evt)> PendingUpdates { get; } = new();

            public bool HasPendingWrites => PendingCount > 0;
            public int PendingCount => PendingCreates.Count + PendingUpdates.Count;

            public void AddSkipped(OutlookEventDto evt)
            {
                Response.Skipped++;
                Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.Skipped));
            }

            public void AddWouldCreate(OutlookEventDto evt)
            {
                Response.Created++;
                Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.WouldCreate));
            }

            public void AddWouldUpdate(OutlookEventDto evt)
            {
                Response.Updated++;
                Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.WouldUpdate));
            }

            public void AddFailed(string eventId, string subject, string error)
            {
                Response.Failed++;
                Response.Items.Add(Item(eventId, subject, ImportStatus.Failed, error: error));
            }

            public void FailAllPending(string error)
            {
                foreach (var (_, evt) in PendingCreates.Concat(PendingUpdates))
                {
                    AddFailed(evt.Id, evt.Subject, error);
                }

                PendingCreates.Clear();
                PendingUpdates.Clear();
            }

            /// <summary>Turns the surviving staged writes into Created/Updated items.</summary>
            public void ReportStaged()
            {
                foreach (var (action, evt) in PendingCreates)
                {
                    Response.Created++;
                    Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.Created, actionId: action.Id));
                }

                foreach (var (action, evt) in PendingUpdates)
                {
                    Response.Updated++;
                    Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.Updated, actionId: action.Id));
                }
            }

            private static ImportedItemDto Item(
                string eventId,
                string subject,
                string status,
                string? error = null,
                int? actionId = null)
            {
                return new ImportedItemDto
                {
                    OutlookEventId = eventId,
                    Subject = subject,
                    Status = status,
                    Error = error,
                    CreatedActionId = actionId,
                };
            }
        }
    }
}
```

`AddAsync`/`UpdateAsync` are called inline, per event, inside `StageCreateAsync`/`StageUpdateAsync` — exactly where the original handler called them, inside that event's own try/catch in the `foreach` loop. Only `SaveChangesAsync` is deferred to `PersistAsync`, matching the original handler's structure. (**Ruling recorded during Task 4 review:** an earlier draft of this section deferred `AddAsync`/`UpdateAsync` into `PersistAsync` alongside `SaveChangesAsync`; that broke per-event failure isolation — one event's `AddAsync` throwing would fail the *entire* batch via `FailAllPending` instead of just that event. The code above is the corrected, binding version.) The existing tests only verify *whether* `AddAsync`/`UpdateAsync`/`SaveChangesAsync` were called (and never in dry-run), so they remain valid; a `Times.Never` on `SaveChangesAsync` in dry-run still holds.

- [ ] **Step 5: Slim the handler**

Replace `ImportFromOutlookHandler.cs` entirely:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook
{
    public class ImportFromOutlookHandler : IRequestHandler<ImportFromOutlookRequest, ImportFromOutlookResponse>
    {
        private readonly IMarketingCalendarSyncService _syncService;
        private readonly ICurrentUserService _currentUserService;

        public ImportFromOutlookHandler(
            IMarketingCalendarSyncService syncService,
            ICurrentUserService currentUserService)
        {
            _syncService = syncService;
            _currentUserService = currentUserService;
        }

        public async Task<ImportFromOutlookResponse> Handle(
            ImportFromOutlookRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new ImportFromOutlookResponse(
                    ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            return await _syncService.SyncAsync(
                request.FromUtc,
                request.ToUtc,
                SyncActor.FromUser(currentUser),
                request.DryRun,
                cancellationToken);
        }
    }
}
```

- [ ] **Step 6: Register the service**

In `MarketingModule.AddMarketingModule`, after the `IOutlookCalendarSync` registration:

```csharp
            services.AddScoped<IMarketingCalendarSyncService, MarketingCalendarSyncService>();
```

- [ ] **Step 7: Rewire the existing handler tests**

In `ImportFromOutlookHandlerTests` constructor, replace the `_handler = new ImportFromOutlookHandler(...)` construction with:

```csharp
        var syncService = new MarketingCalendarSyncService(
            _repositoryMock.Object,
            _outlookSyncMock.Object,
            _mapperMock.Object,
            NullLogger<MarketingCalendarSyncService>.Instance);

        _handler = new ImportFromOutlookHandler(syncService, _currentUserServiceMock.Object);
```

Also add to the constructor's repository setups (needed from Task 5 onward, harmless now — Moq returns `null` for `List<T>` otherwise):

```csharp
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());
```

Nothing else in that file changes. The `Handle_WhenUserNotAuthenticated_ReturnsUnauthorizedError` test still exercises the handler; every other test exercises the real service through the handler.

- [ ] **Step 8: Build and run the marketing tests**

Run: `dotnet build backend/src/Anela.Heblo.API && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Marketing"`
Expected: build succeeds; all marketing tests pass (the `MarketingActionHandlerSyncTests`, controller tests, and `ImportFromOutlookHandlerTests` included).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs
git commit -m "refactor: extract MarketingCalendarSyncService from ImportFromOutlookHandler"
```

---

### Task 5: Reconciliation — soft-delete confirmed orphans

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs` (new)

**Interfaces:**
- Consumes: `IMarketingActionRepository.GetSyncedInWindowAsync` (Task 2), `IOutlookCalendarSync.GetEventAsync` (Task 3), `ImportStatus.Deleted/WouldDelete`, `ImportFromOutlookResponse.Deleted` (Task 4).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Domain.Marketing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing.Services;

public class MarketingCalendarSyncServiceTests
{
    private static readonly DateTime WindowFrom = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EventStart = new(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EventEnd = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly SyncActor Actor = new("user-import", "Import User");

    private readonly Mock<IMarketingActionRepository> _repositoryMock = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSyncMock = new();
    private readonly Mock<IMarketingCategoryMapper> _mapperMock = new();
    private readonly MarketingCalendarSyncService _service;

    public MarketingCalendarSyncServiceTests()
    {
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) => { a.Id = 100; return a; });
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto>());

        _mapperMock
            .Setup(m => m.MapToActionType(It.IsAny<IReadOnlyList<string>>()))
            .Returns(new CategoryMappingResult(MarketingActionType.SocialMedia, null, new List<string>()));

        _service = new MarketingCalendarSyncService(
            _repositoryMock.Object,
            _outlookSyncMock.Object,
            _mapperMock.Object,
            NullLogger<MarketingCalendarSyncService>.Instance);
    }

    private static OutlookEventDto BuildEvent(string id = "evt-1", string subject = "Test Event")
    {
        return new OutlookEventDto
        {
            Id = id,
            Subject = subject,
            Start = new GraphEventDateTime { DateTimeString = EventStart.ToString("O"), TimeZone = "UTC" },
            End = new GraphEventDateTime { DateTimeString = EventEnd.ToString("O"), TimeZone = "UTC" },
            Categories = Array.Empty<string>(),
        };
    }

    private static MarketingAction BuildSyncedAction(int id, string outlookEventId, string title = "Test Event")
    {
        return new MarketingActionTestBuilder()
            .WithId(id)
            .WithOutlookEventId(outlookEventId)
            .WithTitle(title)
            .WithDescription(null)
            .WithStartDate(EventStart)
            .WithEndDate(EventEnd)
            .WithActionType(MarketingActionType.SocialMedia)
            .WithCreatedAt(DateTime.UtcNow)
            .WithModifiedAt(DateTime.UtcNow)
            .WithCreatedBy("user-1")
            .Build();
    }

    private Task<ImportFromOutlookResponse> SyncAsync(bool dryRun = false) =>
        _service.SyncAsync(WindowFrom, WindowTo, Actor, dryRun, CancellationToken.None);

    // ─── Reconciliation ───────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_WhenOrphanConfirmedGone_SoftDeletesWithActor()
    {
        // Arrange — Heblo has an action in the window; Outlook no longer lists it and GET returns 404
        var orphan = BuildSyncedAction(7, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { orphan });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Deleted && i.OutlookEventId == "evt-gone" && i.CreatedActionId == 7);
        orphan.IsDeleted.Should().BeTrue();
        orphan.DeletedByUserId.Should().Be(Actor.UserId);
        orphan.DeletedByUsername.Should().Be(Actor.Username);
        _repositoryMock.Verify(x => x.UpdateAsync(orphan, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenOrphanStillExistsInOutlook_UpdatesInsteadOfDeleting()
    {
        // Arrange — event moved outside the window: not in the list, but GET still finds it (with a new title)
        var moved = BuildSyncedAction(8, "evt-moved", title: "Old Title");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { moved });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-moved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvent(id: "evt-moved", subject: "New Title"));

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Updated.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Updated && i.OutlookEventId == "evt-moved");
        moved.IsDeleted.Should().BeFalse();
        moved.Title.Should().Be("New Title");
    }

    [Fact]
    public async Task SyncAsync_WhenOrphanStillExistsAndUnchanged_SkipsIt()
    {
        // Arrange
        var unchanged = BuildSyncedAction(9, "evt-same");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { unchanged });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-same", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvent(id: "evt-same"));

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Skipped.Should().Be(1);
        unchanged.IsDeleted.Should().BeFalse();
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenOrphanConfirmationThrows_ReportsFailedAndContinues()
    {
        // Arrange — two orphans; the first confirmation blows up, the second is a real delete
        var failing = BuildSyncedAction(10, "evt-boom");
        var gone = BuildSyncedAction(11, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { failing, gone });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-boom", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph 500"));
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);

        // Act
        var result = await SyncAsync();

        // Assert
        result.Failed.Should().Be(1);
        result.Deleted.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Failed && i.OutlookEventId == "evt-boom" && i.Error == "Graph 500");
        failing.IsDeleted.Should().BeFalse();
        gone.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAsync_WhenListedEventExists_IsNotTreatedAsOrphan()
    {
        // Arrange — the action's event IS in the list → no GET, no delete
        var listed = BuildSyncedAction(12, "evt-listed");
        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { BuildEvent(id: "evt-listed") });
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { listed });
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { listed });

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Skipped.Should().Be(1);
        _outlookSyncMock.Verify(s => s.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenDryRunAndOrphanGone_ReportsWouldDeleteWithoutPersisting()
    {
        // Arrange
        var orphan = BuildSyncedAction(13, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { orphan });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);

        // Act
        var result = await SyncAsync(dryRun: true);

        // Assert
        result.Deleted.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.WouldDelete && i.OutlookEventId == "evt-gone");
        orphan.IsDeleted.Should().BeFalse();
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenBatchSaveFails_ReportsDeletesAsFailed()
    {
        // Arrange
        var orphan = BuildSyncedAction(14, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { orphan });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Failed.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Failed && i.OutlookEventId == "evt-gone" && i.Error == "DB down");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingCalendarSyncServiceTests"`
Expected: 7 tests; all except `SyncAsync_WhenListedEventExists_IsNotTreatedAsOrphan` fail (`Deleted` is 0, nothing deleted, no `GetEventAsync` calls).

- [ ] **Step 3: Implement reconciliation**

In `MarketingCalendarSyncService.SyncAsync`, insert between the `foreach (var evt in events)` loop and `await PersistAsync(...)`:

```csharp
            await ReconcileOrphansAsync(fromUtc, toUtc, eventIds, run, cancellationToken);
```

Add these methods to the service (after `StageCreateAsync`):

```csharp
        /// <summary>
        /// Actions in the window whose event was not returned by calendarView are
        /// confirmed one by one: 404 ⇒ deleted in Outlook ⇒ soft-delete here;
        /// found ⇒ moved outside the window ⇒ treat as a normal update.
        /// </summary>
        private async Task ReconcileOrphansAsync(
            DateTime fromUtc,
            DateTime toUtc,
            IReadOnlyCollection<string> fetchedEventIds,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var fetched = new HashSet<string>(fetchedEventIds, StringComparer.OrdinalIgnoreCase);
            var windowActions = await _repository.GetSyncedInWindowAsync(fromUtc, toUtc, cancellationToken);
            var orphans = windowActions.Where(a => !fetched.Contains(a.OutlookEventId!)).ToList();

            foreach (var orphan in orphans)
            {
                try
                {
                    await ReconcileOrphanAsync(orphan, run, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to reconcile marketing action {ActionId} (Outlook event {EventId})",
                        orphan.Id,
                        orphan.OutlookEventId);
                    run.AddFailed(orphan.OutlookEventId!, orphan.Title, ex.Message);
                }
            }
        }

        private async Task ReconcileOrphanAsync(MarketingAction orphan, SyncRun run, CancellationToken cancellationToken)
        {
            var evt = await _outlookSync.GetEventAsync(orphan.OutlookEventId!, cancellationToken);

            if (evt is not null)
            {
                var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
                await StageUpdateAsync(orphan, evt, mapping.ActionType, run, cancellationToken);
                return;
            }

            if (run.DryRun)
            {
                run.AddWouldDelete(orphan);
                return;
            }

            orphan.SoftDelete(run.Actor.UserId, run.Actor.Username, run.UtcNow);

            // Inline, per-orphan — same reasoning as StageCreateAsync/StageUpdateAsync
            // (see Task 4's PersistAsync comment): a failure here is caught by this
            // orphan's own try/catch in ReconcileOrphansAsync, not the whole batch.
            await _repository.UpdateAsync(orphan, cancellationToken);
            run.PendingDeletes.Add(orphan);
        }
```

Extend `SyncRun`:

```csharp
            public List<MarketingAction> PendingDeletes { get; } = new();

            public int PendingCount => PendingCreates.Count + PendingUpdates.Count + PendingDeletes.Count;

            public void AddWouldDelete(MarketingAction action)
            {
                Response.Deleted++;
                Response.Items.Add(Item(action.OutlookEventId!, action.Title, ImportStatus.WouldDelete, actionId: action.Id));
            }
```

In `FailAllPending`, after the existing loop and before the `Clear()` calls:

```csharp
                foreach (var action in PendingDeletes)
                {
                    AddFailed(action.OutlookEventId!, action.Title, error);
                }

                PendingDeletes.Clear();
```

In `ReportStaged`, append:

```csharp
                foreach (var action in PendingDeletes)
                {
                    Response.Deleted++;
                    Response.Items.Add(Item(action.OutlookEventId!, action.Title, ImportStatus.Deleted, actionId: action.Id));
                }
```

`PersistAsync` itself needs no change: it already only calls `SaveChangesAsync` (per the Task 4 ruling — `AddAsync`/`UpdateAsync` run inline in `StageCreateAsync`/`StageUpdateAsync`, and now `ReconcileOrphanAsync` for deletes, never in `PersistAsync`). `SaveChangesAsync` still commits the whole run — creates, updates, and now deletes — in one round-trip, because `_repository.UpdateAsync` on a tracked entity marks it Modified without hitting the database.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Marketing"`
Expected: all marketing tests pass, including the 7 new ones and the untouched `ImportFromOutlookHandlerTests`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs
git commit -m "feat: soft-delete marketing actions whose Outlook event is confirmed gone"
```

---

### Task 6: Restore sync-deleted actions when their event reappears

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` (`StageUpdate`)
- Test: `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs` (append)

**Interfaces:**
- Consumes: `MarketingAction.Restore` (Task 1), `SyncActor.SystemUserId` (Task 4).

- [ ] **Step 1: Write the failing tests**

Append to `MarketingCalendarSyncServiceTests`:

```csharp
    // ─── Restore on reappearance ──────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_WhenSyncDeletedActionReappearsInOutlook_RestoresIt()
    {
        // Arrange — the sync job deleted it earlier; the event is back in the list, unchanged
        var restored = BuildSyncedAction(20, "evt-back");
        restored.SoftDelete(SyncActor.SystemUserId, "Outlook sync", DateTime.UtcNow.AddHours(-1));
        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { BuildEvent(id: "evt-back") });
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { restored });

        // Act
        var result = await SyncAsync();

        // Assert
        result.Updated.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Updated && i.OutlookEventId == "evt-back");
        restored.IsDeleted.Should().BeFalse();
        restored.DeletedByUserId.Should().BeNull();
        restored.ModifiedByUserId.Should().Be(Actor.UserId);
        _repositoryMock.Verify(x => x.UpdateAsync(restored, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenUserDeletedActionReappearsInOutlook_StaysDeleted()
    {
        // Arrange — a person deleted it in Heblo; existing "must not be re-created" behaviour is kept
        var userDeleted = BuildSyncedAction(21, "evt-hidden");
        userDeleted.SoftDelete("user-9", "Some Person", DateTime.UtcNow.AddHours(-1));
        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { BuildEvent(id: "evt-hidden") });
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { userDeleted });

        // Act
        var result = await SyncAsync();

        // Assert
        result.Skipped.Should().Be(1);
        result.Created.Should().Be(0);
        userDeleted.IsDeleted.Should().BeTrue();
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenSyncDeletedActionReappearsInDryRun_ReportsWouldUpdateWithoutRestoring()
    {
        // Arrange
        var restored = BuildSyncedAction(22, "evt-back");
        restored.SoftDelete(SyncActor.SystemUserId, "Outlook sync", DateTime.UtcNow.AddHours(-1));
        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { BuildEvent(id: "evt-back") });
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { restored });

        // Act
        var result = await SyncAsync(dryRun: true);

        // Assert
        result.Updated.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.WouldUpdate);
        restored.IsDeleted.Should().BeTrue();
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingCalendarSyncServiceTests"`
Expected: the two `SyncDeleted…` tests fail (`Skipped == 1`, `IsDeleted == true`); `UserDeleted…` passes already.

- [ ] **Step 3: Implement**

Replace `StageUpdateAsync` in `MarketingCalendarSyncService`:

```csharp
        private async Task StageUpdateAsync(
            MarketingAction existing,
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var needsRestore = existing.IsDeleted
                && existing.DeletedByUserId == SyncActor.SystemUserId;

            if (!needsRestore && !OutlookEventImportMapper.HasChanges(existing, evt, actionType))
            {
                run.AddSkipped(evt);
                return;
            }

            if (run.DryRun)
            {
                run.AddWouldUpdate(evt);
                return;
            }

            if (needsRestore)
            {
                // Only deletions made by the sync itself are reversible; a person deleting
                // an imported action in Heblo keeps it hidden even if Outlook still has it.
                existing.Restore(run.Actor.UserId, run.Actor.Username, run.UtcNow);
            }

            OutlookEventImportMapper.ApplyChanges(existing, evt, actionType, run.Actor, run.UtcNow);
            await _repository.UpdateAsync(existing, cancellationToken);
            run.PendingUpdates.Add((existing, evt));
        }
```

Note this also moves `ApplyChanges` out of the dry-run path — dry-run no longer mutates the in-memory entity. The existing `Handle_WhenDryRunAndEventChanged_ReportsWouldUpdateWithoutPersisting` test asserts only counts and `Times.Never`, so it stays green.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Marketing"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs
git commit -m "feat: restore sync-deleted marketing actions when their Outlook event reappears"
```

---

### Task 7: `MarketingCalendarSyncJob`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/Jobs/MarketingCalendarSyncJob.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` (comment only)
- Test: `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingCalendarSyncJobTests.cs`

**Interfaces:**
- Consumes: `IMarketingCalendarSyncService.SyncAsync`, `SyncActor.System`, `IRecurringJobStatusChecker.IsJobEnabledAsync(jobName, ct, defaultIfMissing)`, `MarketingCalendarOptions.GroupId`.
- Produces: `MarketingCalendarSyncJob : IRecurringJob` with `internal const int PastDays = 30; internal const int FutureMonths = 12;` and `JobName = "marketing-calendar-sync"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

public sealed class MarketingCalendarSyncJobTests
{
    private readonly Mock<IMarketingCalendarSyncService> _syncServiceMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly Mock<ILogger<MarketingCalendarSyncJob>> _loggerMock = new();

    public MarketingCalendarSyncJobTests()
    {
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync("marketing-calendar-sync", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);

        _syncServiceMock
            .Setup(s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportFromOutlookResponse { Created = 1, Updated = 2, Deleted = 3, Skipped = 4, Failed = 0 });
    }

    private MarketingCalendarSyncJob CreateJob(string groupId = "marketing@example.com")
    {
        return new MarketingCalendarSyncJob(
            _syncServiceMock.Object,
            _statusCheckerMock.Object,
            Options.Create(new MarketingCalendarOptions { GroupId = groupId }),
            _loggerMock.Object);
    }

    [Fact]
    public void Metadata_DescribesHourlySyncJob()
    {
        // Arrange / Act
        var metadata = CreateJob().Metadata;

        // Assert
        metadata.JobName.Should().Be("marketing-calendar-sync");
        metadata.CronExpression.Should().Be("0 * * * *");
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobDisabled_DoesNotSync()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _syncServiceMock.Verify(
            s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGroupIdBlank_DoesNotSync()
    {
        // Act
        await CreateJob(groupId: "  ").ExecuteAsync(CancellationToken.None);

        // Assert
        _syncServiceMock.Verify(
            s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_SyncsExpectedWindowAsSystemActor()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        var after = DateTime.UtcNow;
        _syncServiceMock.Verify(
            s => s.SyncAsync(
                It.Is<DateTime>(from =>
                    from >= before.AddDays(-MarketingCalendarSyncJob.PastDays) &&
                    from <= after.AddDays(-MarketingCalendarSyncJob.PastDays)),
                It.Is<DateTime>(to =>
                    to >= before.AddMonths(MarketingCalendarSyncJob.FutureMonths) &&
                    to <= after.AddMonths(MarketingCalendarSyncJob.FutureMonths)),
                SyncActor.System,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_LogsCounts()
    {
        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("1 created, 2 updated, 3 deleted, 4 skipped, 0 failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSyncThrows_Propagates()
    {
        // Arrange — Hangfire must see the failure
        _syncServiceMock
            .Setup(s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph down"));

        // Act
        var act = async () => await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Graph down");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingCalendarSyncJobTests"`
Expected: build error — `MarketingCalendarSyncJob` not defined.

- [ ] **Step 3: Implement the job**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Marketing.Infrastructure.Jobs;

/// <summary>
/// Hourly Outlook → Heblo mirror of the marketing group calendar.
/// Outlook is the source of truth; see <see cref="IMarketingCalendarSyncService"/>.
/// </summary>
public class MarketingCalendarSyncJob : IRecurringJob
{
    internal const int PastDays = 30;
    internal const int FutureMonths = 12;

    private readonly IMarketingCalendarSyncService _syncService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IOptions<MarketingCalendarOptions> _options;
    private readonly ILogger<MarketingCalendarSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "marketing-calendar-sync",
        DisplayName = "Marketing — sync Outlook calendar",
        Description = "Hourly mirror of the Outlook marketing group calendar into Heblo: creates and updates actions from Outlook events and soft-deletes actions whose event was deleted in Outlook (each deletion confirmed with a direct Graph lookup). Window: 30 days back to 12 months ahead.",
        CronExpression = "0 * * * *",
        DefaultIsEnabled = true
    };

    public MarketingCalendarSyncJob(
        IMarketingCalendarSyncService syncService,
        IRecurringJobStatusChecker statusChecker,
        IOptions<MarketingCalendarOptions> options,
        ILogger<MarketingCalendarSyncJob> logger)
    {
        _syncService = syncService;
        _statusChecker = statusChecker;
        _options = options;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Value.GroupId))
        {
            _logger.LogInformation("Job {JobName}: MarketingCalendar:GroupId is not configured. Skipping.", Metadata.JobName);
            return;
        }

        var now = DateTime.UtcNow;
        var fromUtc = now.AddDays(-PastDays);
        var toUtc = now.AddMonths(FutureMonths);

        _logger.LogInformation("Starting {JobName} for window {From:O} → {To:O}", Metadata.JobName, fromUtc, toUtc);

        var result = await _syncService.SyncAsync(fromUtc, toUtc, SyncActor.System, dryRun: false, cancellationToken);

        _logger.LogInformation(
            "{JobName} complete: {Created} created, {Updated} updated, {Deleted} deleted, {Skipped} skipped, {Failed} failed",
            Metadata.JobName, result.Created, result.Updated, result.Deleted, result.Skipped, result.Failed);

        if (result.UnmappedCategories.Count > 0)
        {
            _logger.LogWarning(
                "{JobName}: {Count} unmapped Outlook categor{Plural}: {Categories}",
                Metadata.JobName,
                result.UnmappedCategories.Count,
                result.UnmappedCategories.Count == 1 ? "y" : "ies",
                string.Join(", ", result.UnmappedCategories));
        }
    }
}
```

In `MarketingModule.AddMarketingModule`, add next to the service registration (mirrors the comment other modules carry):

```csharp
            // MarketingCalendarSyncJob is auto-discovered via the IRecurringJob assembly scan in AddRecurringJobs().
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingCalendarSyncJobTests"`
Expected: 6 passed.

- [ ] **Step 5: Check the job is discovered and seeded**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RecurringJob"`
Expected: pass — any seeding/registration tests that enumerate `IRecurringJob` implementations must still be green with the new job present.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/Jobs/MarketingCalendarSyncJob.cs backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingCalendarSyncJobTests.cs
git commit -m "feat: hourly Hangfire job mirroring the Outlook marketing calendar into Heblo"
```

---

### Task 8: Frontend — show deleted count in the import modal

**Files:**
- Regenerate: `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useMarketingCalendar.ts` (`ImportFromOutlookResult`)
- Modify: `frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx`
- Test: `frontend/src/components/marketing/detail/__tests__/ImportFromOutlookModal.test.tsx`

**Interfaces:**
- Consumes: `ImportFromOutlookResponse.deleted?: number` from the regenerated client.

- [ ] **Step 1: Regenerate the TypeScript client**

Run (from repo root): `cd backend/src/Anela.Heblo.API && dotnet build --target GenerateFrontendClientManual && cd -`
Then: `grep -n "deleted?: number" frontend/src/api/generated/api-client.ts | head`
Expected: a `deleted?: number;` line inside `class ImportFromOutlookResponse`. (See `frontend/src/api/README.md` if the target name has moved.)

- [ ] **Step 2: Write the failing test**

Append a new `describe` to `ImportFromOutlookModal.test.tsx`:

```tsx
describe('ImportFromOutlookModal — deleted count', () => {
  it('renders the deleted count from the response', async () => {
    mockMutateAsync.mockResolvedValue({
      created: 1,
      skipped: 2,
      failed: 0,
      deleted: 3,
      unmappedCategories: [],
    });

    render(<ImportFromOutlookModal {...defaultProps} />);
    await triggerImport();

    const deletedLine = screen.getByText('Smazáno:');
    expect(deletedLine).toBeInTheDocument();
    expect(deletedLine).toHaveTextContent('Smazáno: 3');
  });

  it('shows zero deleted when the field is missing', async () => {
    mockMutateAsync.mockResolvedValue({
      created: 1,
      skipped: 0,
      failed: 0,
      unmappedCategories: [],
    } as any);

    render(<ImportFromOutlookModal {...defaultProps} />);
    await triggerImport();

    expect(screen.getByText('Smazáno:')).toHaveTextContent('Smazáno: 0');
  });
});
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/detail/__tests__/ImportFromOutlookModal.test.tsx`
Expected: the two new tests fail with `Unable to find an element with the text: Smazáno:`.

- [ ] **Step 4: Implement**

`useMarketingCalendar.ts` — extend the result type:

```ts
export interface ImportFromOutlookResult {
  created: number;
  skipped: number;
  failed: number;
  deleted: number;
  // Always present — normalized from the generated client's optional field via `?? []` in handleImport.
  unmappedCategories: string[];
}
```

`ImportFromOutlookModal.tsx` — in `handleImport`:

```tsx
      setResult({
        created: data?.created ?? 0,
        skipped: data?.skipped ?? 0,
        failed: data?.failed ?? 0,
        deleted: data?.deleted ?? 0,
        unmappedCategories: data?.unmappedCategories ?? [],
      });
```

and in the result block, after the `Přeskočeno` line:

```tsx
              <p>Smazáno: <strong>{result.deleted}</strong></p>
```

- [ ] **Step 5: Run to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/detail/__tests__/ImportFromOutlookModal.test.tsx`
Expected: all tests in the file pass.

- [ ] **Step 6: Build and lint**

Run: `cd frontend && CI=false npm run build && npm run lint`
Expected: both succeed with no errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useMarketingCalendar.ts frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx frontend/src/components/marketing/detail/__tests__/ImportFromOutlookModal.test.tsx
git commit -m "feat: show deleted count in Outlook import modal"
```

---

### Task 9: Final validation

**Files:** none new.

- [ ] **Step 1: Backend build + format**

Run: `dotnet build Anela.Heblo.sln && dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: build succeeds; format reports no changes (if it does report changes, run `dotnet format Anela.Heblo.sln`, review the diff is confined to files this plan touched, and commit as `chore: dotnet format`).

- [ ] **Step 2: Full backend unit suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "Category!=Integration"`
Expected: all pass. Pay attention to the reflection contract test for `*Response : BaseResponse` and any `IRecurringJob` seeding tests.

- [ ] **Step 3: Frontend suite for marketing**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing src/api/hooks/__tests__/useImportFromOutlook.test.ts`
Expected: all pass.

- [ ] **Step 4: Post-deploy check (staging, after merge)**

Not automatable here — record for the PR description: after the staging deploy, open the Hangfire dashboard, trigger `marketing-calendar-sync` manually once, and confirm (a) the run succeeds, (b) `GET /groups/{id}/calendar/events/{eventId}` with the app token is accepted (if Graph returns 403, the app registration needs `Calendars.Read` application permission — `ListEventsAsync` already works with the same token, so this is expected to pass), (c) the stale Hubboy events are gone from the Heblo calendar.

- [ ] **Step 5: Commit anything outstanding**

```bash
git status
```
Expected: clean tree. If format touched files, they were committed in Step 1.
