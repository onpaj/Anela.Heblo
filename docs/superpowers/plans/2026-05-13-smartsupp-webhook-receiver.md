# Smartsupp Webhook Receiver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broken Hangfire polling sync (`SmartsuppSyncJob`) with an authenticated HMAC-verified webhook receiver, plus an on-demand "Sync now" UI button for backfill/disaster recovery.

**Architecture:** Anonymous `POST /api/webhooks/smartsupp` endpoint on a dedicated controller (extends `ControllerBase`, not `BaseApiController`) verifies an `X-Smartsupp-Hmac` header against the raw request bytes, deserializes the envelope, dispatches to a MediatR `ProcessWebhookEvent` handler that maps event types to existing repository upserts. A second MediatR use case `RunManualSync` ports the old `SmartsuppSyncJob.ExecuteAsync` logic and is invoked through `POST /api/smartsupp/sync` on the existing authenticated `SmartsuppController`, reachable from a "Sync now" button on `SmartsuppChatsPage`. The deprecated `SmartsuppSyncState` entity, its EF configuration, the recurring job, and its tests are deleted; a new EF migration removes `SmartsuppSyncState` from the model snapshot. Idempotency is preserved at the repository layer via an `UpdatedAt` timestamp guard inside `UpsertConversationAsync`.

**Tech Stack:** .NET 8, ASP.NET Core MVC controllers, MediatR, EF Core (PostgreSQL/InMemory for tests), xUnit + FluentAssertions + Moq, React 18 + TypeScript + `@tanstack/react-query`.

---

## File Structure

**New backend files:**
```
backend/src/Anela.Heblo.API/
  Controllers/
    SmartsuppWebhookController.cs                # Anonymous POST receiver (ControllerBase)
  Webhooks/Smartsupp/
    SmartsuppHmacVerifier.cs                     # internal static helper, unit-testable

backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/
  ProcessWebhookEvent/
    ProcessWebhookEventRequest.cs                # class : IRequest<ProcessWebhookEventResponse>
    ProcessWebhookEventResponse.cs               # class (NOT BaseResponse — never serialized to HTTP)
    ProcessWebhookEventHandler.cs                # event-name switch → repo.Upsert*
  RunManualSync/
    RunManualSyncRequest.cs                      # class : IRequest<RunManualSyncResponse>
    RunManualSyncResponse.cs                     # class : BaseResponse (controller uses HandleResponse)
    RunManualSyncHandler.cs                      # port of SmartsuppSyncJob.ExecuteAsync

backend/src/Anela.Heblo.Persistence/Migrations/
  <timestamp>_RemoveSmartsuppSyncState.cs        # ef migration scaffolded after entity deletion
  <timestamp>_RemoveSmartsuppSyncState.Designer.cs

backend/test/Anela.Heblo.Tests/Features/Smartsupp/
  SmartsuppHmacVerifierTests.cs
  ProcessWebhookEventHandlerTests.cs
  RunManualSyncHandlerTests.cs
  SmartsuppWebhookControllerTests.cs             # WebApplicationFactory integration test
  SmartsuppRepositoryUpdatedAtGuardTests.cs      # repository timestamp-guard tests
```

**Edited backend files:**
```
backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs            # +WebhookSecret, +WebhookAppId, -PollIntervalMinutes
backend/src/Anela.Heblo.API/appsettings.json                                       # Smartsupp section
backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs                     # + POST /sync
backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs                 # +X-Smartsupp-Hmac to sensitive headers
backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs               # add UpdatedAt guard, remove sync-state methods
backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs          # remove sync-state methods
backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs                        # remove SmartsuppSyncState DbSet
backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs          # remove SmartsuppSyncJob registration
frontend/src/api/hooks/useSmartsupp.ts                                             # + useTriggerSmartsuppSync mutation
frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx    # + Sync now button in header
```

**Deleted backend files:**
```
backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs
backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppSyncState.cs
backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppSyncStateConfiguration.cs
backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs
```

---

## Task 1: Remove dead `SmartsuppSyncJob`, `SmartsuppSyncState`, and related plumbing

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppSyncState.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppSyncStateConfiguration.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`

- [ ] **Step 1: Delete the four dead files**

```bash
rm backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs
rm backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppSyncState.cs
rm backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppSyncStateConfiguration.cs
rm backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs
```

- [ ] **Step 2: Strip sync-state methods from `ISmartsuppRepository`**

Open `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` and replace the entire file with:

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Strip sync-state methods from `SmartsuppRepository`**

In `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`:

1. Delete the entire `GetOrCreateSyncStateAsync` method (lines 146–155 in the current file).
2. Delete the entire `SetSyncWatermarkAsync` method (lines 157–163 in the current file).
3. **Keep** the `Unspecified` helper method — it is still required by future tasks (timestamp guard); do not delete it.

After the edit the bottom of the file should read:

```csharp
    public async Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken)
    {
        // ... existing body unchanged ...
    }

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 4: Remove the `SmartsuppSyncState` DbSet from `ApplicationDbContext`**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, locate and delete the line:

```csharp
public DbSet<SmartsuppSyncState> SmartsuppSyncState { get; set; } = null!;
```

(It sits in the `// Smartsupp module` section, between `SmartsuppMessages` and `SmartsuppContacts`.)

- [ ] **Step 5: Drop `SmartsuppSyncJob` registration from `SmartsuppModule`**

Open `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` and replace the entire file with:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations.Validators;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence.Smartsupp;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Smartsupp;

public static class SmartsuppModule
{
    public static IServiceCollection AddSmartsuppModule(this IServiceCollection services)
    {
        services.AddScoped<ISmartsuppRepository, SmartsuppRepository>();

        services.AddScoped<IValidator<ListConversationsRequest>, ListConversationsValidator>();
        services.AddScoped<IPipelineBehavior<ListConversationsRequest, ListConversationsResponse>,
            ValidationBehavior<ListConversationsRequest, ListConversationsResponse>>();

        return services;
    }
}
```

- [ ] **Step 6: Verify build is clean**

Run: `cd backend && dotnet build`
Expected: PASS — no compile errors. No file references `SmartsuppSyncJob`, `SmartsuppSyncState`, `GetOrCreateSyncStateAsync`, or `SetSyncWatermarkAsync`.

If you see errors, search and remove the offending reference:
```bash
cd backend && grep -RIn 'SmartsuppSyncJob\|SmartsuppSyncState\|GetOrCreateSyncStateAsync\|SetSyncWatermarkAsync' src/ test/
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(smartsupp): remove broken SmartsuppSyncJob and SyncState"
```

---

## Task 2: Add EF migration to remove `SmartsuppSyncState` from the model snapshot

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_RemoveSmartsuppSyncState.cs` (auto-generated)
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_RemoveSmartsuppSyncState.Designer.cs` (auto-generated)
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (auto-updated)

**Context:** `ApplicationDbContextModelSnapshot.cs` currently still references `SmartsuppSyncState` (verified at line 2787). After Task 1 deleted the entity, the model and snapshot are out of sync — `dotnet ef migrations add` will generate a `DropTable` delta.

- [ ] **Step 1: Scaffold the migration**

Run:
```bash
cd backend
dotnet ef migrations add RemoveSmartsuppSyncState \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: two new files appear under `src/Anela.Heblo.Persistence/Migrations/` and `ApplicationDbContextModelSnapshot.cs` is updated to drop the `SmartsuppSyncState` entry.

- [ ] **Step 2: Make the migration idempotent against environments where the table is already gone**

Open the generated `<timestamp>_RemoveSmartsuppSyncState.cs`. The `Up` method should look like:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(
        name: "SmartsuppSyncState",
        schema: "public");
}
```

Replace the body of `Up` with an `IF EXISTS` raw-SQL drop, so re-running against an already-dropped DB does not fail:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"SmartsuppSyncState\";");
}
```

Leave `Down` as the auto-generated `CreateTable` block.

- [ ] **Step 3: Verify the build**

Run: `cd backend && dotnet build`
Expected: PASS.

- [ ] **Step 4: Verify the snapshot is clean**

Run:
```bash
cd backend && grep -n 'SmartsuppSyncState' src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs
```
Expected: zero matches (the entity block at the old line 2787 should be gone).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "chore(db): migration to remove SmartsuppSyncState from model"
```

---

## Task 3: Extend `SmartsuppOptions` with webhook secret/app_id and drop poll interval

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Update `SmartsuppOptions`**

Replace the entire contents of `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs` with:

```csharp
namespace Anela.Heblo.Adapters.Smartsupp;

public class SmartsuppOptions
{
    public const string SectionKey = "Smartsupp";

    public string ApiToken { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.smartsupp.com/v2/";
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string WebhookSecret { get; set; } = "";
    public string? WebhookAppId { get; set; }
}
```

(Note: `PollIntervalMinutes` is removed — no caller references it after Task 1.)

- [ ] **Step 2: Update `appsettings.json` Smartsupp section**

In `backend/src/Anela.Heblo.API/appsettings.json`, locate the existing `Smartsupp` block (currently the last entry, around lines 513–517):

```json
"Smartsupp": {
  "ApiToken": "-- stored in secrets.json --",
  "BaseUrl": "https://api.smartsupp.com/v2/",
  "PollIntervalMinutes": 2
}
```

Replace with:

```json
"Smartsupp": {
  "ApiToken": "-- stored in secrets.json --",
  "BaseUrl": "https://api.smartsupp.com/v2/",
  "WebhookSecret": "-- stored in secrets.json --",
  "WebhookAppId": ""
}
```

- [ ] **Step 3: Verify build**

Run: `cd backend && dotnet build`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(smartsupp): add WebhookSecret/WebhookAppId options, drop poll interval"
```

---

## Task 4: Add `UpdatedAt` timestamp guard to `SmartsuppRepository.UpsertConversationAsync` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppRepositoryUpdatedAtGuardTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppConversation MakeConversation(string id, DateTime updatedAt, string? subject = null) =>
        new()
        {
            Id = id,
            Status = SmartsuppConversationStatus.Open,
            Subject = subject,
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    [Fact]
    public async Task UpsertConversationAsync_AppliesUpdate_WhenIncomingIsNewer()
    {
        // Arrange
        await using var db = NewContext();
        var existing = MakeConversation("c1", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified), subject: "old");
        db.SmartsuppConversations.Add(existing);
        await db.SaveChangesAsync();

        var repo = new SmartsuppRepository(db);
        var incoming = MakeConversation("c1", new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified), subject: "new");

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.UpdatedAt.Should().Be(incoming.UpdatedAt);
    }

    [Fact]
    public async Task UpsertConversationAsync_SkipsUpdate_WhenIncomingIsOlder()
    {
        // Arrange
        await using var db = NewContext();
        var existingUpdatedAt = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Unspecified);
        var existing = MakeConversation("c1", existingUpdatedAt, subject: "newer");
        db.SmartsuppConversations.Add(existing);
        await db.SaveChangesAsync();

        var repo = new SmartsuppRepository(db);
        var staleIncoming = MakeConversation("c1", new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified), subject: "older");

        // Act — should be a no-op, must not throw
        await repo.UpsertConversationAsync(staleIncoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — UpdatedAt unchanged
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.UpdatedAt.Should().Be(existingUpdatedAt);
    }

    [Fact]
    public async Task UpsertConversationAsync_InsertsNew_WhenIdNotPresent()
    {
        // Arrange
        await using var db = NewContext();
        var repo = new SmartsuppRepository(db);
        var incoming = MakeConversation("c-new", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.Id.Should().Be("c-new");
    }
}
```

- [ ] **Step 2: Run the failing test**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppRepositoryUpdatedAtGuardTests" --no-restore
```
Expected: `UpsertConversationAsync_SkipsUpdate_WhenIncomingIsOlder` FAILS — the current implementation overwrites unconditionally. The two other tests should PASS.

- [ ] **Step 3: Implement the timestamp guard**

In `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`, locate `UpsertConversationAsync` and replace its body so the `else` branch is guarded:

```csharp
public async Task UpsertConversationAsync(
    SmartsuppConversation conversation,
    CancellationToken cancellationToken)
{
    var existing = await _db.SmartsuppConversations
        .FirstOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);

    if (existing is null)
    {
        _db.SmartsuppConversations.Add(conversation);
        return;
    }

    if (existing.UpdatedAt > conversation.UpdatedAt)
    {
        // Out-of-order event: stored state is fresher. Treat as success, do not regress.
        return;
    }

    existing.Status = conversation.Status;
    existing.IsUnread = conversation.IsUnread;
    existing.IsOffline = conversation.IsOffline;
    existing.IsServed = conversation.IsServed;
    existing.ContactId = conversation.ContactId;
    existing.ContactName = conversation.ContactName;
    existing.ContactEmail = conversation.ContactEmail;
    existing.ContactAvatarUrl = conversation.ContactAvatarUrl;
    existing.VisitorId = conversation.VisitorId;
    existing.ExtId = conversation.ExtId;
    existing.FinishedAt = conversation.FinishedAt;
    existing.Domain = conversation.Domain;
    existing.Referer = conversation.Referer;
    existing.LocationCountry = conversation.LocationCountry;
    existing.LocationCity = conversation.LocationCity;
    existing.LocationIp = conversation.LocationIp;
    existing.LocationCode = conversation.LocationCode;
    existing.VariablesJson = conversation.VariablesJson;
    existing.TagsJson = conversation.TagsJson;
    existing.LastMessageAt = conversation.LastMessageAt;
    existing.LastMessagePreview = conversation.LastMessagePreview;
    existing.UpdatedAt = conversation.UpdatedAt;
    existing.SyncedAt = conversation.SyncedAt;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppRepositoryUpdatedAtGuardTests" --no-restore
```
Expected: all three tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs
git commit -m "feat(smartsupp): guard UpsertConversationAsync against stale UpdatedAt"
```

---

## Task 5: Build `SmartsuppHmacVerifier` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppHmacVerifierTests.cs`
- Create: `backend/src/Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppHmacVerifier.cs`

**Context:** Must be testable in isolation. We make it `public static` (no `InternalsVisibleTo` plumbing — simplest path); usage is single-consumer (the webhook controller).

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppHmacVerifierTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.API.Webhooks.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppHmacVerifierTests
{
    private const string Secret = "shared-secret-for-tests";

    private static string ComputeSignature(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Verify_ReturnsTrue_WhenSignatureMatches()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"conversation.created\"}");
        var signature = ComputeSignature(body, Secret);

        var result = SmartsuppHmacVerifier.Verify(body, signature, Secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_IsCaseInsensitive_AndTrimsHeaderValue()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var signature = ComputeSignature(body, Secret);
        var asUpperWithSpaces = "  " + signature.ToUpperInvariant() + "  ";

        var result = SmartsuppHmacVerifier.Verify(body, asUpperWithSpaces, Secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenSignatureMismatch()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, "0000000000000000000000000000000000000000000000000000000000000000", Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenHeaderIsNull()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, null, Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenHeaderIsEmpty()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, "", Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenHeaderIsWhitespace()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, "   ", Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenSecretIsEmpty()
    {
        // Fail-closed: with no configured secret, no signature can be valid
        var body = Encoding.UTF8.GetBytes("{}");
        var sigForEmptySecret = ComputeSignature(body, "");

        var result = SmartsuppHmacVerifier.Verify(body, sigForEmptySecret, "");

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenBodyTampered()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"event\":\"conversation.created\"}");
        var signature = ComputeSignature(originalBody, Secret);

        var tamperedBody = Encoding.UTF8.GetBytes("{\"event\":\"conversation.deleted\"}");
        var result = SmartsuppHmacVerifier.Verify(tamperedBody, signature, Secret);

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the failing tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppHmacVerifierTests" --no-restore
```
Expected: COMPILE FAILURE — `SmartsuppHmacVerifier` does not exist yet.

- [ ] **Step 3: Implement the verifier**

Create `backend/src/Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppHmacVerifier.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Anela.Heblo.API.Webhooks.Smartsupp;

public static class SmartsuppHmacVerifier
{
    public static bool Verify(byte[] rawBody, string? headerValue, string secret)
    {
        // Fail closed when not configured — never accept a signature against an empty secret
        if (string.IsNullOrEmpty(secret))
            return false;

        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        var normalizedHeader = headerValue.Trim().ToLowerInvariant();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        var headerBytes = Encoding.ASCII.GetBytes(normalizedHeader);
        var computedBytes = Encoding.ASCII.GetBytes(computedHex);

        if (headerBytes.Length != computedBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(headerBytes, computedBytes);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppHmacVerifierTests" --no-restore
```
Expected: all eight tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppHmacVerifier.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppHmacVerifierTests.cs
git commit -m "feat(smartsupp): add HMAC-SHA256 webhook signature verifier"
```

---

## Task 6: Implement `ProcessWebhookEvent` MediatR use case (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/ProcessWebhookEventHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventHandler.cs`

**Context:** Handler switches on `EventName`; maps `data` from `JsonElement` into domain entities and calls existing repository upserts. `Data` field uses the same JSON conventions as `SmartsuppApiClient` (snake_case lowercase, case-insensitive).

- [ ] **Step 1: Create the request DTO**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventRequest.cs`:

```csharp
using System.Text.Json;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventRequest : IRequest<ProcessWebhookEventResponse>
{
    public string EventName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string AppId { get; set; } = "";
    public JsonElement Data { get; set; }
}
```

- [ ] **Step 2: Create the response DTO**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventResponse.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventResponse
{
    public bool Handled { get; set; }
    public string? Reason { get; set; }
}
```

(This class does NOT inherit `BaseResponse` because the webhook controller never serializes it to HTTP — Smartsupp expects an empty 200.)

- [ ] **Step 3: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/ProcessWebhookEventHandlerTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class ProcessWebhookEventHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private ProcessWebhookEventHandler CreateHandler() =>
        new(_repo.Object, NullLogger<ProcessWebhookEventHandler>.Instance);

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static ProcessWebhookEventRequest MakeRequest(string eventName, string dataJson) =>
        new()
        {
            EventName = eventName,
            Timestamp = DateTime.UtcNow,
            AccountId = "acc-1",
            AppId = "app-1",
            Data = Parse(dataJson),
        };

    [Fact]
    public async Task Handle_ConversationCreated_UpsertsConversationAndSaves()
    {
        // Arrange
        var data = """
            {
              "id": "c1",
              "status": "open",
              "unread": false,
              "is_offline": false,
              "is_served": false,
              "contact_id": null,
              "visitor_id": null,
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T10:00:00Z"
            }
            """;
        var request = MakeRequest("conversation.created", data);

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.Status == SmartsuppConversationStatus.Open),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConversationUpdated_UpsertsConversation()
    {
        // Arrange
        var data = """
            {
              "id": "c1",
              "status": "open",
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T10:05:00Z"
            }
            """;
        var request = MakeRequest("conversation.updated", data);

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConversationClosed_MapsResolvedStatus()
    {
        // Arrange
        var data = """
            {
              "id": "c1",
              "status": "resolved",
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T11:00:00Z"
            }
            """;
        var request = MakeRequest("conversation.closed", data);

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.Status == SmartsuppConversationStatus.Resolved),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MessageCreated_UpsertsMessageOnly()
    {
        // Arrange
        var data = """
            {
              "id": "m1",
              "conversation_id": "c1",
              "sub_type": "contact",
              "content": { "text": "Hello", "type": "text" },
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T10:00:00Z"
            }
            """;
        var request = MakeRequest("message.created", data);

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertMessagesAsync(
            "c1",
            It.Is<List<SmartsuppMessage>>(msgs =>
                msgs.Count == 1
                && msgs[0].Id == "m1"
                && msgs[0].AuthorType == SmartsuppMessageAuthorType.Visitor),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownEvent_ReturnsHandledFalse_WithReason()
    {
        // Arrange
        var request = MakeRequest("conversation.exploded", "{}");

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Handled.Should().BeFalse();
        response.Reason.Should().Be("unknown event");
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 4: Run the failing tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessWebhookEventHandlerTests" --no-restore
```
Expected: COMPILE FAILURE — `ProcessWebhookEventHandler` does not exist yet.

- [ ] **Step 5: Implement the handler**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventHandler.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventHandler : IRequestHandler<ProcessWebhookEventRequest, ProcessWebhookEventResponse>
{
    private const int LastMessagePreviewMaxLength = 200;

    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<ProcessWebhookEventHandler> _logger;

    public ProcessWebhookEventHandler(
        ISmartsuppRepository repository,
        ILogger<ProcessWebhookEventHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ProcessWebhookEventResponse> Handle(
        ProcessWebhookEventRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.EventName)
        {
            case "conversation.created":
            case "conversation.updated":
            case "conversation.closed":
                await HandleConversationAsync(request.Data, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("smartsupp webhook handled conversation event {Event}", request.EventName);
                return new ProcessWebhookEventResponse { Handled = true };

            case "message.created":
                await HandleMessageAsync(request.Data, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("smartsupp webhook handled message event {Event}", request.EventName);
                return new ProcessWebhookEventResponse { Handled = true };

            default:
                _logger.LogInformation("smartsupp webhook unknown event={Event}", request.EventName);
                return new ProcessWebhookEventResponse { Handled = false, Reason = "unknown event" };
        }
    }

    private async Task HandleConversationAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var id = data.GetProperty("id").GetString() ?? "";
        var statusStr = TryGetString(data, "status")?.ToLowerInvariant();
        var status = statusStr == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        var createdAt = ReadUtc(data, "created_at");
        var updatedAt = ReadUtc(data, "updated_at");
        var lastMessageText = TryGetString(data, "last_message_text");

        var conversation = new SmartsuppConversation
        {
            Id = id,
            ExtId = TryGetString(data, "ext_id"),
            Status = status,
            IsUnread = TryGetBool(data, "unread") ?? false,
            IsOffline = TryGetBool(data, "is_offline") ?? false,
            IsServed = TryGetBool(data, "is_served") ?? false,
            ContactId = TryGetString(data, "contact_id"),
            VisitorId = TryGetString(data, "visitor_id"),
            FinishedAt = ReadOptionalUtc(data, "finished_at"),
            Domain = TryGetString(data, "domain"),
            Referer = TryGetString(data, "referer"),
            LastMessageAt = ReadOptionalUtc(data, "last_message_at"),
            LastMessagePreview = lastMessageText?.Length > LastMessagePreviewMaxLength
                ? lastMessageText[..LastMessagePreviewMaxLength]
                : lastMessageText,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            SyncedAt = DateTime.UtcNow,
        };

        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }

    private async Task HandleMessageAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var conversationId = data.GetProperty("conversation_id").GetString() ?? "";
        var subType = TryGetString(data, "sub_type");
        var contentText = data.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
            ? TryGetString(content, "text")
            : TryGetString(data, "content");

        var message = new SmartsuppMessage
        {
            Id = data.GetProperty("id").GetString() ?? "",
            ConversationId = conversationId,
            AuthorType = ParseAuthorType(subType),
            SubType = subType,
            AuthorName = TryGetString(data, "author_name"),
            Content = contentText,
            TriggerName = TryGetString(data, "trigger_name"),
            TriggerId = TryGetString(data, "trigger_id"),
            PageUrl = TryGetString(data, "page_url"),
            AgentId = TryGetString(data, "agent_id"),
            VisitorId = TryGetString(data, "visitor_id"),
            DeliveryStatus = TryGetString(data, "delivery_status"),
            DeliveredAt = ReadOptionalUtc(data, "delivered_at"),
            IsOffline = TryGetBool(data, "is_offline") ?? false,
            IsReply = TryGetBool(data, "is_reply") ?? false,
            IsFirstReply = TryGetBool(data, "is_first_reply") ?? false,
            ResponseTime = TryGetInt(data, "response_time"),
            CreatedAt = ReadUtc(data, "created_at"),
            UpdatedAt = ReadUtc(data, "updated_at"),
        };

        await _repository.UpsertMessagesAsync(conversationId, new List<SmartsuppMessage> { message }, cancellationToken);
    }

    private static SmartsuppMessageAuthorType ParseAuthorType(string? subType) =>
        subType?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            "contact" => SmartsuppMessageAuthorType.Visitor,
            _ => SmartsuppMessageAuthorType.Visitor,
        };

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? TryGetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : null;

    private static int? TryGetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static DateTime ReadUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : DateTime.UtcNow;

    private static DateTime? ReadOptionalUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : null;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessWebhookEventHandlerTests" --no-restore
```
Expected: all five tests PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/ProcessWebhookEventHandlerTests.cs
git commit -m "feat(smartsupp): add ProcessWebhookEvent MediatR handler"
```

---

## Task 7: Add `X-Smartsupp-Hmac` to `RequestLoggingMiddleware` sensitive header list

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs`

- [ ] **Step 1: Add the header to the sensitive list**

In `backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs`, find the `IsSensitiveHeader` method (around line 223). Replace:

```csharp
private bool IsSensitiveHeader(string headerName)
{
    var sensitiveHeaders = new[]
    {
        "Authorization",
        "Cookie",
        "X-API-Key",
        "X-Auth-Token"
    };

    return sensitiveHeaders.Any(h =>
        h.Equals(headerName, StringComparison.OrdinalIgnoreCase));
}
```

with:

```csharp
private bool IsSensitiveHeader(string headerName)
{
    var sensitiveHeaders = new[]
    {
        "Authorization",
        "Cookie",
        "X-API-Key",
        "X-Auth-Token",
        "X-Smartsupp-Hmac"
    };

    return sensitiveHeaders.Any(h =>
        h.Equals(headerName, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Verify build**

Run: `cd backend && dotnet build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs
git commit -m "chore(logging): mark X-Smartsupp-Hmac as sensitive"
```

---

## Task 8: Build `SmartsuppWebhookController` (anonymous, raw-body, HMAC + app_id)

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs`

**Context:** This controller does NOT inherit `BaseApiController` because Smartsupp expects an empty `200`, not the JSON `BaseResponse` envelope. It also caps body at 1 MB (NFR-2) and always returns 200 except on HMAC/app_id failure (returns 401), to suppress Smartsupp retry storms on transient errors.

- [ ] **Step 1: Implement the controller**

Create `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.API.Webhooks.Smartsupp;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/webhooks/smartsupp")]
[AllowAnonymous]
public class SmartsuppWebhookController : ControllerBase
{
    private const string SignatureHeader = "X-Smartsupp-Hmac";
    private const int MaxBodyBytes = 1_048_576; // 1 MB

    private readonly IMediator _mediator;
    private readonly SmartsuppOptions _options;
    private readonly ILogger<SmartsuppWebhookController> _logger;

    public SmartsuppWebhookController(
        IMediator mediator,
        IOptions<SmartsuppOptions> options,
        ILogger<SmartsuppWebhookController> logger)
    {
        _mediator = mediator;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        Request.EnableBuffering();
        Request.Body.Position = 0;

        byte[] rawBody;
        await using (var ms = new MemoryStream())
        {
            await Request.Body.CopyToAsync(ms, cancellationToken);
            rawBody = ms.ToArray();
        }
        Request.Body.Position = 0;

        var headerValue = Request.Headers.TryGetValue(SignatureHeader, out var sig) ? sig.ToString() : null;

        if (!SmartsuppHmacVerifier.Verify(rawBody, headerValue, _options.WebhookSecret))
        {
            _logger.LogWarning("smartsupp webhook signature mismatch from {RemoteIp}", remoteIp);
            return Unauthorized();
        }

        JsonElement envelope;
        try
        {
            envelope = JsonDocument.Parse(rawBody).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "smartsupp webhook malformed JSON from {RemoteIp}", remoteIp);
            return Ok(); // do not trigger retries — body cannot be parsed regardless of attempts
        }

        var eventName = TryGetString(envelope, "event") ?? "";
        var accountId = TryGetString(envelope, "account_id") ?? "";
        var appId = TryGetString(envelope, "app_id") ?? "";
        var timestamp = TryGetUtc(envelope, "timestamp") ?? DateTime.UtcNow;
        var data = envelope.TryGetProperty("data", out var d) ? d.Clone() : default;

        if (!string.IsNullOrEmpty(_options.WebhookAppId) && !string.Equals(_options.WebhookAppId, appId, StringComparison.Ordinal))
        {
            _logger.LogWarning("smartsupp webhook app_id mismatch from {RemoteIp}", remoteIp);
            return Unauthorized();
        }

        _logger.LogInformation("smartsupp webhook event={Event} account={AccountId} app={AppId}", eventName, accountId, appId);

        try
        {
            await _mediator.Send(new ProcessWebhookEventRequest
            {
                EventName = eventName,
                Timestamp = timestamp,
                AccountId = accountId,
                AppId = appId,
                Data = data,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "smartsupp webhook downstream processing failed event={Event} app={AppId} bodySize={Size}",
                eventName, appId, rawBody.Length);
            // Acknowledge anyway — retries would likely fail too; operator can use "Sync now".
        }

        return Ok();
    }

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? TryGetUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : null;
}
```

- [ ] **Step 2: Write the integration tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppWebhookControllerTests : IClassFixture<SmartsuppWebhookFactory>
{
    private const string Secret = "test-shared-secret";

    private readonly SmartsuppWebhookFactory _factory;

    public SmartsuppWebhookControllerTests(SmartsuppWebhookFactory factory)
    {
        _factory = factory;
    }

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpRequestMessage BuildRequest(string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/smartsupp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature != null)
            request.Headers.Add("X-Smartsupp-Hmac", signature);
        return request;
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenSignatureMissing()
    {
        var client = _factory.CreateClient();
        var body = "{\"event\":\"conversation.created\",\"data\":{}}";

        var response = await client.SendAsync(BuildRequest(body, signature: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenSignatureWrong()
    {
        var client = _factory.CreateClient();
        var body = "{\"event\":\"conversation.created\",\"data\":{}}";

        var response = await client.SendAsync(BuildRequest(body, signature: "deadbeef"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenSignatureValidAndKnownEvent()
    {
        var client = _factory.CreateClient();
        var body = """
            {
              "event": "conversation.created",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {
                "id": "c-int-1",
                "status": "open",
                "created_at": "2026-05-13T09:59:00Z",
                "updated_at": "2026-05-13T10:00:00Z"
              }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenUnknownEvent_AndSignatureValid()
    {
        var client = _factory.CreateClient();
        var body = """
            {
              "event": "conversation.exploded",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {}
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenJsonMalformed_AndSignatureValid()
    {
        var client = _factory.CreateClient();
        var body = "not-json-at-all";

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenAppIdConfiguredAndMismatched()
    {
        var factory = new SmartsuppWebhookFactory(webhookAppId: "expected-app");
        var client = factory.CreateClient();
        var body = """
            {
              "event": "conversation.created",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "wrong-app",
              "data": { "id": "c1", "status": "open", "created_at": "2026-05-13T10:00:00Z", "updated_at": "2026-05-13T10:00:00Z" }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class SmartsuppWebhookFactory : HebloWebApplicationFactory
{
    private readonly string? _webhookAppId;

    public SmartsuppWebhookFactory() : this(null) { }

    public SmartsuppWebhookFactory(string? webhookAppId)
    {
        _webhookAppId = webhookAppId;
    }

    protected override void ConfigureTestWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smartsupp:WebhookSecret"] = "test-shared-secret",
                ["Smartsupp:WebhookAppId"] = _webhookAppId,
            });
        });
    }
}
```

- [ ] **Step 3: Run the integration tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppWebhookControllerTests" --no-restore
```
Expected: all six tests PASS. (The full BE build must succeed first.)

- [ ] **Step 4: Verify dotnet format**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: PASS — no formatting issues. If any are reported, run `dotnet format` and re-stage.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs
git commit -m "feat(smartsupp): add webhook receiver controller"
```

---

## Task 9: Implement `RunManualSync` MediatR use case (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RunManualSyncHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncHandler.cs`

**Context:** Ports `SmartsuppSyncJob.ExecuteAsync` behaviour (paged search → upsert conversation → fetch messages → upsert messages, with a contact cache per run). No watermark persistence — counts returned in response. `RunManualSyncResponse` MUST extend `BaseResponse` (required by `SmartsuppController.HandleResponse<T>`).

- [ ] **Step 1: Create the request DTO**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncRequest : IRequest<RunManualSyncResponse>
{
    public DateTime? Since { get; set; }
}
```

- [ ] **Step 2: Create the response DTO**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncResponse : BaseResponse
{
    public int ConversationsProcessed { get; set; }
    public int MessagesProcessed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    public RunManualSyncResponse() { }
    public RunManualSyncResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 3: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RunManualSyncHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RunManualSyncHandlerTests
{
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private RunManualSyncHandler CreateHandler() =>
        new(_apiClient.Object, _repo.Object, NullLogger<RunManualSyncHandler>.Instance);

    private static SmartsuppConversationData MakeConv(string id, DateTime updatedAt, string? contactId = null) =>
        new()
        {
            Id = id,
            Status = "open",
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            ContactId = contactId,
        };

    private void SetupRepoDefaults()
    {
        _repo.Setup(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_DefaultsSinceToSevenDaysAgo_WhenNotProvided()
    {
        // Arrange
        var recent = MakeConv("c-recent", DateTime.UtcNow.AddDays(-2));   // within 7d default
        var stale = MakeConv("c-stale", DateTime.UtcNow.AddDays(-10));    // outside default

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [recent, stale] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert — only the recent conversation is upserted
        response.Success.Should().BeTrue();
        response.ConversationsProcessed.Should().Be(1);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-recent"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-stale"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HonoursSince_WhenProvided()
    {
        // Arrange
        var since = DateTime.UtcNow.AddHours(-1);
        var inRange = MakeConv("c1", DateTime.UtcNow.AddMinutes(-30));
        var outOfRange = MakeConv("c2", DateTime.UtcNow.AddHours(-2));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [inRange, outOfRange] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest { Since = since }, CancellationToken.None);

        // Assert
        response.ConversationsProcessed.Should().Be(1);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CountsMessagesAcrossConversations()
    {
        // Arrange
        var c1 = MakeConv("c1", DateTime.UtcNow.AddMinutes(-10));
        var c2 = MakeConv("c2", DateTime.UtcNow.AddMinutes(-5));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [c1, c2] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>
                  {
                      new() { Id = "m1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubType = "contact" },
                      new() { Id = "m2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubType = "agent" },
                  });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c2", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>
                  {
                      new() { Id = "m3", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubType = "contact" },
                  });
        SetupRepoDefaults();

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert
        response.ConversationsProcessed.Should().Be(2);
        response.MessagesProcessed.Should().Be(3);
    }

    [Fact]
    public async Task Handle_PagesThroughAllResults()
    {
        // Arrange — first call returns cursor, second returns null
        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = "cursor", Items = [MakeConv("c1", DateTime.UtcNow.AddMinutes(-1))] });
        _apiClient.Setup(c => c.SearchConversationsAsync("cursor", 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [MakeConv("c2", DateTime.UtcNow.AddMinutes(-2))] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert
        response.ConversationsProcessed.Should().Be(2);
        _apiClient.Verify(c => c.SearchConversationsAsync("cursor", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClampsSinceToThirtyDaysAgo_WhenTooDeep()
    {
        // Arrange — caller asks for 90 days back, must be clamped to 30
        var deepSince = DateTime.UtcNow.AddDays(-90);
        var withinThirty = MakeConv("c-within", DateTime.UtcNow.AddDays(-20));
        var beyondThirty = MakeConv("c-beyond", DateTime.UtcNow.AddDays(-40));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [withinThirty, beyondThirty] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest { Since = deepSince }, CancellationToken.None);

        // Assert
        response.ConversationsProcessed.Should().Be(1);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-within"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-beyond"), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 4: Run the failing tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~RunManualSyncHandlerTests" --no-restore
```
Expected: COMPILE FAILURE — `RunManualSyncHandler` does not exist yet.

- [ ] **Step 5: Implement the handler**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/RunManualSyncHandler.cs`:

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
                if (item.UpdatedAt <= since)
                    continue;

                var msgCount = await ProcessConversationAsync(item, startedAt, contactCache, cancellationToken);
                conversationsProcessed++;
                messagesProcessed += msgCount;
            }

            await _repository.SaveChangesAsync(cancellationToken);
            cursor = page.After;

        } while (cursor is not null);

        var completedAt = DateTime.UtcNow;
        _logger.LogInformation(
            "smartsupp manual sync completed conversations={Conversations} messages={Messages}",
            conversationsProcessed, messagesProcessed);

        return new RunManualSyncResponse
        {
            ConversationsProcessed = conversationsProcessed,
            MessagesProcessed = messagesProcessed,
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
            _logger.LogWarning(ex,
                "smartsupp manual sync failed to fetch messages for conversation {ConversationId}",
                data.Id);
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

        var status = data.Status?.ToLowerInvariant() == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        var conversation = new SmartsuppConversation
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

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~RunManualSyncHandlerTests" --no-restore
```
Expected: all five tests PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/ \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/RunManualSyncHandlerTests.cs
git commit -m "feat(smartsupp): add RunManualSync MediatR handler"
```

---

## Task 10: Expose manual sync via `SmartsuppController.RunSync`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`

- [ ] **Step 1: Add the action**

Open `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`. Add an import for the new use case at the top:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;
```

Then append the new action just before the closing brace of the controller class (after `GetConversation`):

```csharp
[HttpPost("sync")]
[ProducesResponseType(typeof(RunManualSyncResponse), StatusCodes.Status200OK)]
public async Task<ActionResult<RunManualSyncResponse>> RunSync(
    [FromBody] RunManualSyncRequest? request,
    CancellationToken cancellationToken = default)
{
    var result = await _mediator.Send(request ?? new RunManualSyncRequest(), cancellationToken);
    return HandleResponse(result);
}
```

(Body is optional — operator typically POSTs without one and accepts the 7-day default.)

- [ ] **Step 2: Verify build and existing tests still pass**

Run:
```bash
cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Smartsupp" --no-restore
```
Expected: build PASSES; all Smartsupp tests PASS (verifier, handler, repository guard, manual sync handler, controller integration tests, existing list/get tests).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs
git commit -m "feat(smartsupp): expose POST /api/smartsupp/sync for manual backfill"
```

---

## Task 11: Regenerate frontend OpenAPI client and add `useTriggerSmartsuppSync` hook

**Files:**
- Modify: `frontend/src/api/hooks/useSmartsupp.ts`
- Auto-regenerated: `frontend/src/api/generated/api-client.ts` (via build)

**Context:** Per `docs/development/api-client-generation.md`, the TS client regenerates as part of the standard frontend build. The mutation hook in `useSmartsupp.ts` calls the generated client method for `POST /api/smartsupp/sync`. We do not edit the generated client by hand.

- [ ] **Step 1: Trigger client regeneration via frontend build**

Run:
```bash
cd frontend && npm run build
```
Expected: build runs OpenAPI generation as part of the pipeline; `frontend/src/api/generated/api-client.ts` (or wherever the client lives — same path used by `useSmartsuppConversations`) now contains a method for `POST /api/smartsupp/sync` (e.g. `smartsupp_Sync` or similar). The build should still PASS (any unrelated TS errors must be fixed before continuing).

If the existing `useSmartsupp.ts` hooks use the raw `fetch` pattern (they do — see `apiFetch` helper) rather than the generated method, the mutation hook below will follow the same convention.

- [ ] **Step 2: Add the `useTriggerSmartsuppSync` mutation hook**

Open `frontend/src/api/hooks/useSmartsupp.ts`. Add `useMutation` and `useQueryClient` to the import line at the top:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
```

Then add the response type below the existing `GetConversationResponse` interface (around line 38):

```typescript
export interface RunManualSyncResponse {
  success: boolean;
  conversationsProcessed: number;
  messagesProcessed: number;
  startedAt: string;
  completedAt: string;
}
```

Then add the mutation hook at the end of the file:

```typescript
export function useTriggerSmartsuppSync() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await (apiClient as any).http.fetch(`${baseUrl}/api/smartsupp/sync`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: "{}",
      });
      if (!response.ok) {
        throw new Error(`Smartsupp sync failed: ${response.status} ${response.statusText}`);
      }
      return (await response.json()) as RunManualSyncResponse;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["smartsupp", "conversations"] });
    },
  });
}
```

- [ ] **Step 3: Verify frontend type-checks and lints**

Run:
```bash
cd frontend && npm run build && npm run lint
```
Expected: PASS on both.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useSmartsupp.ts
git commit -m "feat(smartsupp): add useTriggerSmartsuppSync mutation hook"
```

---

## Task 12: Add "Sync now" button to `SmartsuppChatsPage`

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`

- [ ] **Step 1: Wire up the mutation and render the button**

Replace the entire contents of `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` with:

```tsx
import React, { useState } from "react";
import { useSmartsuppConversations, useTriggerSmartsuppSync } from "../../../../api/hooks/useSmartsupp";
import { useToast } from "../../../../contexts/ToastContext";
import ConversationList from "../ConversationList";
import ConversationDetail from "../ConversationDetail";

const SmartsuppChatsPage: React.FC = () => {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState<"Open" | "Resolved">("Open");

  const { data, isLoading } = useSmartsuppConversations(status);
  const { showSuccess, showError } = useToast();
  const syncMutation = useTriggerSmartsuppSync();

  const conversations = data?.items ?? [];
  const selectedConversation = conversations.find((c) => c.id === selectedId) ?? null;

  const handleSyncClick = () => {
    syncMutation.mutate(undefined, {
      onSuccess: (result) => {
        showSuccess(
          "Synchronizace dokončena",
          `Konverzace: ${result.conversationsProcessed} • zprávy: ${result.messagesProcessed}`,
        );
      },
      onError: (error) => {
        showError("Synchronizace selhala", error instanceof Error ? error.message : "Neznámá chyba");
      },
    });
  };

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex items-center justify-end px-4 py-2 border-b border-gray-200 bg-white">
        <button
          type="button"
          onClick={handleSyncClick}
          disabled={syncMutation.isPending}
          className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-md border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {syncMutation.isPending ? (
            <>
              <svg
                className="animate-spin h-4 w-4 text-gray-500"
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
              >
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path
                  className="opacity-75"
                  fill="currentColor"
                  d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"
                />
              </svg>
              Synchronizuji…
            </>
          ) : (
            <>Sync now</>
          )}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden bg-white rounded-lg shadow-sm border border-gray-200">
        <div className="w-96 flex-shrink-0 overflow-hidden">
          <ConversationList
            conversations={conversations}
            selectedId={selectedId}
            status={status}
            isLoading={isLoading}
            onSelect={setSelectedId}
            onStatusChange={(s) => {
              setStatus(s);
              setSelectedId(null);
            }}
          />
        </div>

        <div className="flex-1 overflow-hidden">
          {selectedConversation ? (
            <ConversationDetail conversationId={selectedId!} conversation={selectedConversation} />
          ) : (
            <div className="flex items-center justify-center h-full text-gray-400 text-sm">
              Vyberte konverzaci
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default SmartsuppChatsPage;
```

- [ ] **Step 2: Verify frontend builds and lints**

Run:
```bash
cd frontend && npm run build && npm run lint
```
Expected: PASS on both.

- [ ] **Step 3: Smoke-test the button in a dev browser**

Start the dev server: `cd frontend && npm start` (and the backend if not already running per `docs/development/setup.md`).
Open the Smartsupp chats page (`/customer-support/smartsupp` or per your router). Verify:
1. **Sync now** button is visible in the header.
2. Clicking it disables the button, shows the spinner.
3. On success, the success toast appears with the counts.
4. The conversation list refetches automatically.
5. On failure (you can simulate by temporarily breaking the secret in `secrets.json` so the BE returns 401 — but the sync endpoint itself is authenticated, so failure path test is: trigger the BE handler to throw, or simply assert toast wiring via code review here), the error toast surfaces a readable message.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx
git commit -m "feat(smartsupp): add Sync now button on chats page"
```

---

## Task 13: Final validation across the whole change set

**Files:** none (verification only)

- [ ] **Step 1: Backend build is clean**

Run: `cd backend && dotnet build`
Expected: PASS, zero warnings related to Smartsupp.

- [ ] **Step 2: All backend tests pass**

Run: `cd backend && dotnet test`
Expected: PASS — including all Smartsupp-related suites (`SmartsuppHmacVerifierTests`, `ProcessWebhookEventHandlerTests`, `RunManualSyncHandlerTests`, `SmartsuppWebhookControllerTests`, `SmartsuppRepositoryUpdatedAtGuardTests`, plus the pre-existing `ListConversationsHandlerTests`, `GetConversationHandlerTests`, `SmartsuppApiClientTests`).

- [ ] **Step 3: Backend is formatted**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: PASS.

- [ ] **Step 4: Frontend build + lint clean**

Run: `cd frontend && npm run build && npm run lint`
Expected: PASS on both.

- [ ] **Step 5: Grep for leftover references to deleted symbols**

Run:
```bash
cd backend && grep -RIn 'SmartsuppSyncJob\|SmartsuppSyncState\|GetOrCreateSyncStateAsync\|SetSyncWatermarkAsync\|PollIntervalMinutes' src/ test/
```
Expected: zero hits. (`PollIntervalMinutes` is also gone because we dropped it from options.)

- [ ] **Step 6: Confirm `X-Smartsupp-Hmac` is filtered from logs**

Run:
```bash
grep -n 'X-Smartsupp-Hmac' backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs
```
Expected: one match inside the `sensitiveHeaders` array.

- [ ] **Step 7: Confirm `appsettings.json` has the new keys and not the dropped one**

Run:
```bash
grep -A 4 '"Smartsupp"' backend/src/Anela.Heblo.API/appsettings.json
```
Expected output contains `WebhookSecret` and `WebhookAppId`, does NOT contain `PollIntervalMinutes`.

- [ ] **Step 8: Optional — confirm the new migration is the last one and applies cleanly**

Run:
```bash
cd backend && dotnet ef migrations list \
  --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
```
Expected: the new `RemoveSmartsuppSyncState` migration appears at the bottom (pending) — applying it locally (`dotnet ef database update`) should succeed because the `DROP TABLE IF EXISTS` is idempotent.

- [ ] **Step 9: Final commit (if anything required formatting)**

If `dotnet format` made changes:
```bash
git add -A
git commit -m "chore: apply dotnet format"
```

Otherwise this task is verification-only — no commit.

---

## Self-Review

**Spec coverage map:**

| Requirement | Task(s) |
|-------------|---------|
| FR-1 Webhook receiver endpoint | Task 8 |
| FR-2 HMAC-SHA256 verification | Task 5 (verifier), Task 8 (controller call site) |
| FR-3 Optional `app_id` verification | Task 8 |
| FR-4 Event dispatch via MediatR | Task 6 |
| FR-5 Idempotency + out-of-order safety | Task 4 (timestamp guard) |
| FR-6 Configuration / secrets | Task 3 |
| FR-7 Retire recurring job + state model | Task 1, Task 2 (migration) |
| FR-8 Manual sync use case | Task 9 |
| FR-9 Manual sync HTTP endpoint | Task 10 |
| FR-10 "Sync now" UI button | Task 11 (hook), Task 12 (button) |
| FR-11 Observability | Task 6 (handler), Task 7 (header), Task 8 (controller) |
| NFR-1 Performance (raw body cap, sync stream) | Task 8 (`RequestSizeLimit`), Task 9 (pages) |
| NFR-2 Security | Task 5, Task 7, Task 8 |
| NFR-3 Reliability (idempotent, never bubble 5xx) | Task 4, Task 8 (catch-all) |
| NFR-4 Maintainability (vertical slice, DTO classes) | Tasks 5–9 |
| NFR-5 Operational (env URL registration) | Out-of-scope for code; deferred to runbook |

**Arch-review amendments incorporated:**
- (1) `RunManualSyncResponse` extends `BaseResponse` — Task 9, Step 2. ✓
- (2) `SmartsuppSyncStateConfiguration.cs` deleted — Task 1, Step 1. ✓
- (3) `X-Smartsupp-Hmac` added to `IsSensitiveHeader` — Task 7. ✓
- (4) `[RequestSizeLimit(1_048_576)]` on the webhook action — Task 8. ✓
- (5) HMAC verifier in its own file (public static, no `InternalsVisibleTo` needed) — Task 5. ✓
- (6) Frontend uses `useMutation` + `useQueryClient` to invalidate `smartsupp.conversations`; toast via `useToast` — Tasks 11–12. ✓
- (7) `Since` clamped to `UtcNow - 30 days` — Task 9, Step 5 (`ResolveSince`); test in Step 3. ✓
- (8) `Unspecified` helper retained because `SmartsuppRepository` still needs it for other date handling — Task 1, Step 3. ✓

**Placeholder scan:** none — every step contains the actual code or command.

**Type consistency scan:**
- `RunManualSyncResponse` properties (`ConversationsProcessed`, `MessagesProcessed`, `StartedAt`, `CompletedAt`) are referenced identically in Tasks 9, 10, 11, 12.
- `ProcessWebhookEventRequest` field names (`EventName`, `Timestamp`, `AccountId`, `AppId`, `Data`) match between Tasks 6 and 8.
- `SmartsuppHmacVerifier.Verify(byte[], string?, string)` signature matches between Tasks 5 and 8.
- `useTriggerSmartsuppSync` hook name matches between Tasks 11 and 12.
- `SMARTSUPP_QUERY_KEYS.conversations(status)` key used in Task 11's `invalidateQueries` matches the prefix used by the existing list query (line 55 of `useSmartsupp.ts`), so `["smartsupp", "conversations"]` invalidation matches any status arg.

All consistent.
