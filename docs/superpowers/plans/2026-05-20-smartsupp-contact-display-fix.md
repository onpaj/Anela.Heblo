# Smartsupp Contact Display Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Smartsupp conversations showing "Neznámý" (Unknown) for contacts by preserving denorm fields on null updates, hydrating from the contact entity, and back-filling conversations when a contact arrives.

**Architecture:** Three targeted fixes to `SmartsuppRepository` + a new `BackfillConversationDenormFieldsAsync` method wired into the three contact reactions. No schema changes. Fix 1 prevents null overwrites; Fix 2 hydrates from the contact row at upsert time; Fix 3 propagates contact info to existing conversations when a contact arrives.

**Tech Stack:** .NET 8, EF Core (in-memory DB for tests), xUnit, FluentAssertions

---

## File Map

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` | Add `BackfillConversationDenormFieldsAsync` signature |
| `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` | Fix 1 (null-coalesce on update), Fix 2 (hydrate from contact), implement backfill method |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactCreatedReaction.cs` | Call `BackfillConversationDenormFieldsAsync` |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpdatedReaction.cs` | Call `BackfillConversationDenormFieldsAsync` |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactAcquiredReaction.cs` | Call `BackfillConversationDenormFieldsAsync` |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs` | Add `SmartsuppRepositoryDenormFieldTests` class |

---

### Task 1: Write failing tests for Fix 1 and Fix 2

Tests for null-preservation (Fix 1) and contact-hydration (Fix 2) in `UpsertConversationAsync`.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs`

- [ ] **Step 1: Add the test class at the bottom of the file**

Append this class to the end of `SmartsuppRepositoryUpdatedAtGuardTests.cs` (after the last `}`):

```csharp
public class SmartsuppRepositoryDenormFieldTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppConversation MakeConversation(
        string id,
        string? contactName = null,
        string? contactEmail = null,
        string? contactId = null) =>
        new()
        {
            Id = id,
            Status = SmartsuppConversationStatus.Open,
            ContactName = contactName,
            ContactEmail = contactEmail,
            ContactId = contactId,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Unspecified),
        };

    private static SmartsuppContact MakeContact(string id, string? name = null, string? email = null) =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
        };

    [Fact]
    public async Task UpsertConversationAsync_PreservesDenormFields_WhenNewValuesAreNull()
    {
        // Arrange
        await using var db = NewContext();
        var existing = MakeConversation("c1", contactName: "Jana Nováková", contactEmail: "jana@x.cz");
        db.SmartsuppConversations.Add(existing);
        await db.SaveChangesAsync();

        var repo = new SmartsuppRepository(db);
        var incoming = MakeConversation("c1", contactName: null, contactEmail: null);
        incoming.UpdatedAt = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.ContactName.Should().Be("Jana Nováková");
        stored.ContactEmail.Should().Be("jana@x.cz");
    }

    [Fact]
    public async Task UpsertConversationAsync_HydratesDenormFields_FromExistingContact()
    {
        // Arrange
        await using var db = NewContext();
        var contact = MakeContact("c-contact-1", name: "Petr Novák", email: "petr@x.cz");
        db.SmartsuppContacts.Add(contact);
        await db.SaveChangesAsync();

        var repo = new SmartsuppRepository(db);
        var incoming = MakeConversation("conv-1", contactName: null, contactEmail: null, contactId: "c-contact-1");

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.ContactName.Should().Be("Petr Novák");
        stored.ContactEmail.Should().Be("petr@x.cz");
        stored.ContactId.Should().Be("c-contact-1");
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppRepositoryDenormFieldTests" \
  --no-build 2>&1 | tail -20
```

Expected: Build succeeds, both tests FAIL (ContactName remains null, ContactEmail remains null).

---

### Task 2: Implement Fix 1 and Fix 2 in SmartsuppRepository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Replace the FK guard block and denorm field assignments**

In `UpsertConversationAsync`, replace lines 79–110 (from `if (conversation.ContactId is not null)` through the denorm assignments) with the following:

```csharp
    public async Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken)
    {
        SmartsuppContact? linkedContact = null;
        if (conversation.ContactId is not null)
        {
            linkedContact = await _db.SmartsuppContacts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conversation.ContactId, cancellationToken);

            if (linkedContact is null)
                conversation.ContactId = null;
        }

        conversation.ContactName ??= linkedContact?.Name;
        conversation.ContactEmail ??= linkedContact?.Email;

        var existing = await _db.SmartsuppConversations
            .FirstOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);

        if (existing is null)
        {
            _db.SmartsuppConversations.Add(conversation);
            return;
        }

        if (existing.UpdatedAt > conversation.UpdatedAt)
        {
            // Out-of-order event: stored state is fresher — skip update.
            return;
        }

        existing.Status = conversation.Status;
        existing.IsUnread = conversation.IsUnread;
        existing.IsOffline = conversation.IsOffline;
        existing.IsServed = conversation.IsServed;
        existing.ContactId = conversation.ContactId;
        existing.Subject = conversation.Subject;
        existing.ContactName = conversation.ContactName ?? existing.ContactName;
        existing.ContactEmail = conversation.ContactEmail ?? existing.ContactEmail;
        existing.ContactAvatarUrl = conversation.ContactAvatarUrl ?? existing.ContactAvatarUrl;
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
        existing.Rating = conversation.Rating;
        existing.RatingText = conversation.RatingText;
        existing.CloseType = conversation.CloseType;
        existing.ClosedByAgentId = conversation.ClosedByAgentId;
        existing.AssignedAgentIdsJson = conversation.AssignedAgentIdsJson;
        existing.Channel = conversation.Channel;
        existing.LastClosedAt = conversation.LastClosedAt;
    }
```

- [ ] **Step 2: Run the tests to verify they pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppRepositoryDenormFieldTests" 2>&1 | tail -20
```

Expected: Both tests PASS.

- [ ] **Step 3: Run the full test suite to check for regressions**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs
git commit -m "fix(smartsupp): preserve and hydrate conversation denorm contact fields"
```

---

### Task 3: Write failing test for Fix 3 (BackfillConversationDenormFieldsAsync)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs`

- [ ] **Step 1: Add the backfill test to `SmartsuppRepositoryDenormFieldTests`**

Inside the `SmartsuppRepositoryDenormFieldTests` class (which already exists from Task 1), add this test method:

```csharp
    [Fact]
    public async Task BackfillConversationDenormFieldsAsync_UpdatesConversations_WithContactNameAndEmail()
    {
        // Arrange
        await using var db = NewContext();
        var conv = MakeConversation("conv-backfill", contactName: null, contactEmail: null, contactId: "c-backfill");
        db.SmartsuppConversations.Add(conv);
        await db.SaveChangesAsync();

        var repo = new SmartsuppRepository(db);
        var contact = MakeContact("c-backfill", name: "Marie Svobodová", email: "marie@x.cz");

        // Act
        await repo.BackfillConversationDenormFieldsAsync(contact, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.ContactName.Should().Be("Marie Svobodová");
        stored.ContactEmail.Should().Be("marie@x.cz");
    }
```

- [ ] **Step 2: Build to verify the test file compiles (method doesn't exist yet — expect build failure)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -20
```

Expected: Build FAILS with `'SmartsuppRepository' does not contain a definition for 'BackfillConversationDenormFieldsAsync'`.

---

### Task 4: Add BackfillConversationDenormFieldsAsync to interface and repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Add method signature to ISmartsuppRepository**

In `ISmartsuppRepository.cs`, add this method after `UpsertContactAsync`:

```csharp
    Task BackfillConversationDenormFieldsAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken);
```

The full interface should look like:

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

    Task BackfillConversationDenormFieldsAsync(
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

    Task UpdateMessageDeliveryStatusAsync(
        string messageId,
        string status,
        DateTime? deliveredAt,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Add BackfillConversationDenormFieldsAsync implementation to SmartsuppRepository**

In `SmartsuppRepository.cs`, add this method before `SaveChangesAsync`:

```csharp
    public async Task BackfillConversationDenormFieldsAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken)
    {
        var conversations = await _db.SmartsuppConversations
            .Where(c => c.ContactId == contact.Id)
            .ToListAsync(cancellationToken);

        foreach (var conv in conversations)
        {
            conv.ContactName = contact.Name ?? conv.ContactName;
            conv.ContactEmail = contact.Email ?? conv.ContactEmail;
        }
    }
```

- [ ] **Step 3: Run the backfill test to verify it passes**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BackfillConversationDenormFieldsAsync_UpdatesConversations_WithContactNameAndEmail" 2>&1 | tail -20
```

Expected: Test PASSES.

- [ ] **Step 4: Run the full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs
git commit -m "feat(smartsupp): add BackfillConversationDenormFieldsAsync to repository"
```

---

### Task 5: Wire BackfillConversationDenormFieldsAsync into contact reactions

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactCreatedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpdatedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactAcquiredReaction.cs`

- [ ] **Step 1: Update ContactCreatedReaction**

Replace the full file content:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactCreatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ContactCreatedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "contact.created";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        var contact = SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp);
        await _repository.UpsertContactAsync(contact, cancellationToken);
        await _repository.BackfillConversationDenormFieldsAsync(contact, cancellationToken);
    }
}
```

- [ ] **Step 2: Update ContactUpdatedReaction**

Replace the full file content:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactUpdatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ContactUpdatedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "contact.updated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        var contact = SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp);
        await _repository.UpsertContactAsync(contact, cancellationToken);
        await _repository.BackfillConversationDenormFieldsAsync(contact, cancellationToken);
    }
}
```

- [ ] **Step 3: Update ContactAcquiredReaction**

Replace the full file content:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ContactAcquiredReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ContactAcquiredReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "contact.acquired";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var contactEl = ctx.GetContact();
        if (contactEl is null) return;
        var contact = SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp);
        await _repository.UpsertContactAsync(contact, cancellationToken);
        await _repository.BackfillConversationDenormFieldsAsync(contact, cancellationToken);
    }
}
```

- [ ] **Step 4: Build to verify all reactions compile**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet build 2>&1 | tail -20
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Run full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 6: Run dotnet format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier/backend
dotnet format 2>&1 | tail -10
```

Expected: No output or only formatting applied messages.

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/montpellier
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactCreatedReaction.cs
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpdatedReaction.cs
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactAcquiredReaction.cs
git commit -m "fix(smartsupp): back-fill conversation denorm fields when contact arrives"
```

---

## Self-Review

**Spec coverage:**
- Fix 1 (preserve denorm on null update): Task 1 + Task 2 ✅
- Fix 2 (hydrate from contact at upsert time): Task 1 + Task 2 ✅
- Fix 3 (backfill conversations when contact arrives): Task 3 + Task 4 + Task 5 ✅
- All three contact reactions wired: Task 5 ✅
- Interface updated: Task 4 ✅
- Tests (three specified in spec): Task 1 + Task 3 ✅
- dotnet build + format: Task 5 ✅

**Optional Fix 4** (lazy-fetch from Smartsupp REST API) — explicitly deferred per spec.

**Re-link of orphaned conversations** (ContactId nullified by FK guard before contact arrived) — explicitly out of scope per spec.

**Placeholder scan:** No TBD, no TODO, no "implement later" — all steps contain concrete code.

**Type consistency:**
- `BackfillConversationDenormFieldsAsync(SmartsuppContact contact, CancellationToken cancellationToken)` — used consistently in interface (Task 4), implementation (Task 4), and all three reactions (Task 5).
- `MakeContact` helper — defined once in `SmartsuppRepositoryDenormFieldTests`, used in Task 1 and Task 3.
- `conv.ContactName` / `conv.ContactEmail` — consistent across repository and test assertions.
