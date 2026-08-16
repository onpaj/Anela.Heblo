# Move Smartsupp Contact Enrichment Out Of SmartsuppRepository Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `SmartsuppRepository`'s dependency on `ISmartsuppApiClient` by moving the "fetch a missing contact via Smartsupp REST and stage it locally" decision into a new Application-layer `ISmartsuppContactEnricher`, called by every webhook reaction (and `RefreshOrphanContactsHandler`) immediately before `UpsertConversationAsync`, with zero observable behavior change.

**Architecture:** Three-task sequence, each leaving the solution compiling and green: (1) add the new `ISmartsuppContactEnricher` / `SmartsuppContactEnricher` alongside a new `ISmartsuppRepository.ContactExistsAsync` read, fully tested in isolation, without touching any existing call site; (2) wire all 7 reaction classes (6 direct + `ConversationReplyReactionBase`, whose 3 subclasses need constructor passthrough updates) plus `RefreshOrphanContactsHandler` to call the enricher before upserting; (3) delete `ISmartsuppApiClient`, `TryFetchAndStageContactAsync`, and `MapContactDataToEntity` from `SmartsuppRepository`, and fix every test that still constructs it with the old 3-argument constructor.

**Tech Stack:** .NET 8, C#, EF Core (Npgsql provider), MediatR, xUnit, Moq, FluentAssertions, NSubstitute (integration test file only).

---

## File Structure

| File | Responsibility | Action |
|------|----------------|--------|
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs` | New interface + `SmartsuppContactEnricher` implementation — owns the "resolve or clear ContactId" decision | **Create** |
| `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` | Repository contract | **Modify** — add `ContactExistsAsync` |
| `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` | Persistence-only implementation | **Modify** — add `ContactExistsAsync`; later remove `ISmartsuppApiClient`, `TryFetchAndStageContactAsync`, `MapContactDataToEntity`, wipe branch |
| `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` | DI registration | **Modify** — register `ISmartsuppContactEnricher` |
| `.../Reactions/ConversationOpenedReaction.cs`, `ConversationRatedReaction.cs`, `ConversationClosedReaction.cs`, `ConversationClosedByContactReaction.cs`, `ConversationAgentAssignedReaction.cs`, `ConversationAgentUnassignedReaction.cs`, `ConversationReplyReactionBase.cs` | Webhook reactions that call `UpsertConversationAsync` | **Modify** — inject enricher, call before upsert |
| `.../Reactions/ConversationContactRepliedReaction.cs`, `ConversationAgentRepliedReaction.cs`, `ConversationBotRepliedReaction.cs` | Thin subclasses of `ConversationReplyReactionBase` | **Modify** — constructor passthrough only |
| `.../UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs` | Re-triggers enrichment for orphaned rows | **Modify** — call enricher after re-attaching `ContactId` |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs` | Unit tests for the new enricher | **Create** |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUnknownContactFetchTests.cs` | REST-fetch-on-miss behavior tests (moving) + orphan-listing test (staying) | **Modify** — delete the 4 REST-behavior tests (ported into the new file above), keep `ListOrphanContactConversationIdsAsync_ReturnsOnlyConversationsWithNoNameOrEmail` |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs` | Reaction unit tests | **Modify** — add enricher mock to constructor calls |
| `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs` | Hosts `SmartsuppRepositoryTestFactory` + denorm/delivery tests | **Modify** — drop `apiClient` parameter from the factory |
| `backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs` | Real-Postgres integration tests | **Modify** — drop `apiClient` argument from `CreateRepository` |

Task 1 alone is a complete, tested, compiling increment — it adds a new class nobody calls yet.
Task 2 rewires all callers to use it, still with the old `SmartsuppRepository` REST path present as
dead code (both paths behave identically during this window, so nothing regresses). Task 3 deletes
the dead code and finishes the migration.

---

### task: add-smartsupp-contact-enricher

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` (add one method only — nothing removed yet)
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs`

#### Goal

Satisfy FR-3 from `spec.r1.md`: a new `ISmartsuppContactEnricher` that resolves a conversation's
`ContactId` against the local `SmartsuppContacts` table, fetching-and-staging it via
`ISmartsuppApiClient` only when not already known locally, and clearing `ContactId` (fail-open) on
any REST failure or null result — exactly mirroring today's `SmartsuppRepository.TryFetchAndStageContactAsync`
behavior. This task does not change any existing call site; the new class is inert until Task 2.

#### Context you need before touching code

- **The existence check must query the DB, not the incoming DTO.** `SmartsuppPayloadMapper.MapConversation`
  already sets `ContactName`/`ContactEmail` straight from webhook JSON fields (`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs:49-50`).
  If the enricher decided "already enriched" based on those DTO fields being non-null, it would skip
  fetching-and-persisting a brand-new contact into `SmartsuppContacts` whenever Smartsupp happens to
  inline the name/email on the event — silently starving that table. Use a real existence read
  instead (`ContactExistsAsync`, added below).
- **`DateTimeKind.Utc` handling must be preserved verbatim when moving `MapContactDataToEntity`.**
  `SmartsuppRepository.cs:330-335` has a load-bearing comment: raw SQL via `ExecuteSqlInterpolatedAsync`
  types a bare `DateTime` as `timestamp with time zone` and rejects `Kind=Unspecified`. Move the
  comment along with the code — do not drop it, and do not "clean it up."
  ```csharp
  // Timestamps MUST be DateTimeKind.Utc: UpsertContactAsync writes them via
  // ExecuteSqlInterpolated, which types a bare DateTime as `timestamp with time zone`
  // and rejects Kind=Unspecified at the Npgsql layer. The webhook contact path
  // (SmartsuppPayloadMapper.MapContact) already produces Utc; this REST-staged path
  // must match, otherwise the enclosing conversation upsert throws and the conversation
  // is dropped (observed for Facebook Messenger contacts fetched on demand).
  ```
- **Catch `Exception` broadly, not a narrower type.** `SmartsuppRepository.cs:312` catches `Exception`
  (not `HttpRequestException`) around the REST call — this is the existing fail-open contract. Do not
  narrow it; that would be a silent behavior change (see arch-review.r1.md's Specification Amendments).
- **The two "gave up" log messages differ slightly in today's code** — only the exception path logs
  a warning (`SmartsuppRepository.cs:316-318`); the "REST returned null" path is silent. This plan adds
  an explicit warning for the null case too (spec FR-3 step 4 calls for it) — this is a deliberate,
  small, in-scope improvement to operability, not a behavior change to persisted data, since it's
  logging-only.
- **`SmartsuppContact` and `SmartsuppConversation` live in `Anela.Heblo.Domain.Features.Smartsupp`** —
  same namespace as `ISmartsuppRepository` and `ISmartsuppApiClient`; no new project reference needed
  from `Anela.Heblo.Application` (it already references `Anela.Heblo.Domain`).
- **`ISmartsuppRepository.UpsertContactAsync` already exists** and is already called from the
  Application layer today (`ContactUpsertWithBackfillReactionBase.cs:19`) — reuse it verbatim, do not
  add a second write path.

#### Implementation steps

- [ ] **Step 1: Add `ContactExistsAsync` to `ISmartsuppRepository`**

In `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`, the interface
currently ends with:

```csharp
    Task UpdateVisitorCacheAsync(
        string conversationId,
        string? userAgent,
        string? os,
        string? browser,
        string? browserVersion,
        int? visitsCount,
        DateTime fetchedAt,
        CancellationToken cancellationToken);
}
```

Change to:

```csharp
    Task UpdateVisitorCacheAsync(
        string conversationId,
        string? userAgent,
        string? os,
        string? browser,
        string? browserVersion,
        int? visitsCount,
        DateTime fetchedAt,
        CancellationToken cancellationToken);

    Task<bool> ContactExistsAsync(string contactId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement `ContactExistsAsync` in `SmartsuppRepository`**

In `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`, the class currently ends
with:

```csharp
    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);
}
```

Change to:

```csharp
    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);

    public async Task<bool> ContactExistsAsync(string contactId, CancellationToken cancellationToken) =>
        await _db.SmartsuppContacts
            .AsNoTracking()
            .AnyAsync(c => c.Id == contactId, cancellationToken);
}
```

- [ ] **Step 3: Build to confirm the interface addition compiles**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
```

Expected: `Build succeeded.` with **0 Error(s)**. (No other class implements `ISmartsuppRepository`
in production code, so nothing else needs updating yet — confirm with the grep below.)

```bash
grep -rln "ISmartsuppRepository" backend/src | grep -v Reactions
```

Expected: exactly `ISmartsuppRepository.cs` and `SmartsuppRepository.cs` (plus any `UseCases/*Handler.cs`
files that merely consume it — those don't implement it, so they're unaffected by an interface addition).

- [ ] **Step 4: Create `ISmartsuppContactEnricher` and `SmartsuppContactEnricher`**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs`:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure;

/// <summary>
/// Resolves a SmartsuppConversation's ContactId to a locally-persisted SmartsuppContact, fetching
/// and staging it via the Smartsupp REST API when it is not already known locally. On any failure
/// to resolve (REST error or REST returns null), clears conversation.ContactId so the caller
/// persists an unlinked conversation (fail-open — matches pre-refactor SmartsuppRepository
/// behaviour; see #3878).
/// </summary>
public interface ISmartsuppContactEnricher
{
    Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);
}

public sealed class SmartsuppContactEnricher : ISmartsuppContactEnricher
{
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<SmartsuppContactEnricher> _logger;

    public SmartsuppContactEnricher(
        ISmartsuppApiClient apiClient,
        ISmartsuppRepository repository,
        ILogger<SmartsuppContactEnricher> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken)
    {
        if (conversation.ContactId is null)
            return conversation;

        var existsLocally = await _repository.ContactExistsAsync(conversation.ContactId, cancellationToken);
        if (existsLocally)
            return conversation;

        // Smartsupp webhooks reference contacts by id without inlining the name/email
        // and we cannot rely on a contact.* event arriving — pull the record via REST so
        // the FK link survives and the conversation row carries the display name.
        SmartsuppContactData? data;
        try
        {
            data = await _apiClient.GetContactAsync(conversation.ContactId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail open: webhook still saves the conversation without the contact link.
            // The orphan backfill job can pick it up later when Smartsupp REST is healthy.
            _logger.LogWarning(ex,
                "smartsupp: failed to fetch contact {ContactId} while upserting conversation; continuing without link",
                conversation.ContactId);
            conversation.ContactId = null;
            return conversation;
        }

        if (data is null)
        {
            _logger.LogWarning(
                "smartsupp: contact {ContactId} not found via REST while upserting conversation; continuing without link",
                conversation.ContactId);
            conversation.ContactId = null;
            return conversation;
        }

        var contact = MapContactDataToEntity(data, conversation.SyncedAt);
        await _repository.UpsertContactAsync(contact, cancellationToken);

        conversation.ContactName ??= contact.Name;
        conversation.ContactEmail ??= contact.Email;
        return conversation;
    }

    // Timestamps MUST be DateTimeKind.Utc: UpsertContactAsync writes them via
    // ExecuteSqlInterpolated, which types a bare DateTime as `timestamp with time zone`
    // and rejects Kind=Unspecified at the Npgsql layer. The webhook contact path
    // (SmartsuppPayloadMapper.MapContact) already produces Utc; this REST-staged path
    // must match, otherwise the enclosing conversation upsert throws and the conversation
    // is dropped (observed for Facebook Messenger contacts fetched on demand).
    internal static SmartsuppContact MapContactDataToEntity(SmartsuppContactData data, DateTime syncedAt) =>
        new()
        {
            Id = data.Id,
            Email = data.Email,
            Name = data.Name,
            Phone = data.Phone,
            Note = data.Note,
            BannedAt = data.BannedAt is { } bannedAt ? DateTime.SpecifyKind(bannedAt, DateTimeKind.Utc) : null,
            BannedBy = data.BannedBy,
            GdprApproved = data.GdprApproved,
            TagsJson = data.TagsJson,
            PropertiesJson = data.PropertiesJson,
            CreatedAt = DateTime.SpecifyKind(data.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(data.UpdatedAt, DateTimeKind.Utc),
            SyncedAt = DateTime.SpecifyKind(syncedAt, DateTimeKind.Utc),
        };
}
```

- [ ] **Step 5: Register `ISmartsuppContactEnricher` in DI**

In `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`, the top of
`AddSmartsuppModule` currently reads:

```csharp
        services.AddScoped<ISmartsuppRepository, SmartsuppRepository>();
        services.AddScoped<ISmartsuppPresenceRepository, SmartsuppPresenceRepository>();
```

Change to:

```csharp
        services.AddScoped<ISmartsuppRepository, SmartsuppRepository>();
        services.AddScoped<ISmartsuppContactEnricher, SmartsuppContactEnricher>();
        services.AddScoped<ISmartsuppPresenceRepository, SmartsuppPresenceRepository>();
```

Add the using at the top of the file (alphabetically among the existing `Anela.Heblo.Application.Features.Smartsupp.*` usings):

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
```

- [ ] **Step 6: Write `SmartsuppContactEnricherTests.cs`**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs`,
porting the four REST-behavior tests from `SmartsuppRepositoryUnknownContactFetchTests.cs` onto the
new class, with `ISmartsuppRepository` mocked instead of a live `ApplicationDbContext`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppContactEnricherTests
{
    private static SmartsuppConversation MakeConversation(string id, string contactId, DateTime updatedAt) =>
        new()
        {
            Id = id,
            ContactId = contactId,
            Status = SmartsuppConversationStatus.Open,
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    private static SmartsuppContactData MakeContactData(string id, string? name = null, string? email = null) =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
        };

    private static SmartsuppContactEnricher CreateSut(
        Mock<ISmartsuppApiClient> apiClient,
        Mock<ISmartsuppRepository> repository) =>
        new(apiClient.Object, repository.Object, NullLogger<SmartsuppContactEnricher>.Instance);

    [Fact]
    public async Task EnrichContactAsync_FetchesContactViaRest_WhenLocalContactMissing()
    {
        // Arrange
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeContactData("ct-unknown", name: "Michaela", email: "michaela@example.com"));

        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-unknown", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert
        apiClient.Verify(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.UpsertContactAsync(
            It.Is<SmartsuppContact>(c => c.Id == "ct-unknown" && c.Name == "Michaela"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.ContactId.Should().Be("ct-unknown");
        result.ContactName.Should().Be("Michaela");
        result.ContactEmail.Should().Be("michaela@example.com");
    }

    [Fact]
    public async Task EnrichContactAsync_WipesContactId_WhenRestReturnsNull()
    {
        // Arrange
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppContactData?)null);

        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-gone", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert — REST attempted; ContactId wiped because REST returned null (fail-open)
        apiClient.Verify(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()), Times.Never);
        result.ContactId.Should().BeNull();
        result.ContactName.Should().BeNull();
        result.ContactEmail.Should().BeNull();
    }

    [Fact]
    public async Task EnrichContactAsync_WipesContactId_WhenRestThrows()
    {
        // Arrange — REST blows up (e.g., 500). Webhook must still persist the conversation.
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-broken", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Smartsupp 500"));

        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-broken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-broken", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act — fail-open: REST exception is caught and ContactId is cleared.
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert
        result.ContactId.Should().BeNull();
        result.ContactName.Should().BeNull();
        repository.Verify(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnrichContactAsync_DoesNotCallRest_WhenContactAlreadyKnownLocally()
    {
        // Arrange — happy path: contact already synced via contact.acquired earlier.
        var apiClient = new Mock<ISmartsuppApiClient>(MockBehavior.Strict);
        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-known", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-known", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act — strict mock: any unexpected REST call would fail.
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert
        result.ContactId.Should().Be("ct-known");
        repository.Verify(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnrichContactAsync_ReturnsUnchanged_WhenContactIdIsNull()
    {
        // Arrange
        var apiClient = new Mock<ISmartsuppApiClient>(MockBehavior.Strict);
        var repository = new Mock<ISmartsuppRepository>(MockBehavior.Strict);
        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", contactId: null!, new DateTime(2026, 6, 8, 10, 0, 0));
        incoming.ContactId = null;

        // Act
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert — no repository or REST calls at all.
        result.Should().BeSameAs(incoming);
    }
}
```

- [ ] **Step 7: Run the new tests**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SmartsuppContactEnricherTests"
```

Expected: **Passed! - Failed: 0, Passed: 5**.

- [ ] **Step 8: Full build + full test suite (nothing else should be affected yet)**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Smartsupp"
```

Expected: `Build succeeded.` with 0 errors; all Smartsupp tests still pass (no existing test touches
`ISmartsuppContactEnricher` yet, so none should change outcome).

- [ ] **Step 9: Commit**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs \
        backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs \
        backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs
git commit -m "feat(smartsupp): add ISmartsuppContactEnricher (#3878)"
```

---

### task: wire-reactions-to-contact-enricher

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationOpenedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationRatedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedByContactReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentAssignedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentUnassignedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationReplyReactionBase.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationContactRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationBotRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`

#### Goal

Satisfy FR-4 from `spec.r1.md`: every call site that relies on `UpsertConversationAsync`'s implicit
contact enrichment now calls `ISmartsuppContactEnricher.EnrichContactAsync` explicitly first. After
this task, `SmartsuppRepository`'s own REST-fetch path is dead code (still present, but no caller
reaches the "contact not found locally" branch through a path that needed it) — Task 3 deletes it.

#### Context you need before touching code

- **`ConversationReplyReactionBase` is the base class for 3 sealed subclasses** — `ConversationContactRepliedReaction`,
  `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction` — each with a single
  pass-through constructor (`: base(repository) { }`). Adding a constructor parameter to the base
  requires updating all 3 subclass constructors too, or the solution won't compile.
- **Only the conversation-upsert branch of `ConversationReplyReactionBase.HandleAsync` needs
  enrichment** — the message-only branch (when only `msgEl` is present, no `convEl`) must not call
  the enricher; that branch never touches `ContactId`.
- **`RefreshOrphanContactsHandler` already injects `ISmartsuppApiClient` and `ISmartsuppRepository`
  directly** — do not remove those; it still needs `ISmartsuppApiClient` for its own
  `GetConversationAsync` re-discovery call (spec is explicit: only `SmartsuppRepository` loses the
  dependency, not every Smartsupp Application-layer class). Add `ISmartsuppContactEnricher` as a
  fourth constructor dependency.
- **`ConversationReactionsTests.cs` has a single shared `Mock<ISmartsuppRepository> _repo` field**
  used by all 20 tests. Add a sibling `Mock<ISmartsuppContactEnricher> _enricher` field and update
  every affected reaction constructor call. Default the mock's `EnrichContactAsync` to return the
  input conversation unchanged (pass-through), so existing assertions on `UpsertConversationAsync`'s
  argument continue to hold without every test needing its own enricher setup.

#### Implementation steps

- [ ] **Step 1: `ConversationOpenedReaction`**

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationOpenedReaction.cs`
currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationOpenedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationOpenedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.opened";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationOpenedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationOpenedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.opened";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 2: `ConversationRatedReaction`**

`.../ConversationRatedReaction.cs` currently reads:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationRatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationRatedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.rated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);

        if (ctx.Data.TryGetProperty("rating_value", out var rv) && rv.ValueKind == JsonValueKind.Number)
            conversation.Rating = rv.GetInt32();

        conversation.RatingText = SmartsuppPayloadMapper.TryGetString(ctx.Data, "rating_text");
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationRatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationRatedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.rated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);

        if (ctx.Data.TryGetProperty("rating_value", out var rv) && rv.ValueKind == JsonValueKind.Number)
            conversation.Rating = rv.GetInt32();

        conversation.RatingText = SmartsuppPayloadMapper.TryGetString(ctx.Data, "rating_text");
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 3: `ConversationClosedReaction`**

`.../ConversationClosedReaction.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationClosedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.closed";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = SmartsuppPayloadMapper.TryGetString(ctx.Data, "close_type");
        conversation.ClosedByAgentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationClosedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.closed";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = SmartsuppPayloadMapper.TryGetString(ctx.Data, "close_type");
        conversation.ClosedByAgentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 4: `ConversationClosedByContactReaction`**

`.../ConversationClosedByContactReaction.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedByContactReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationClosedByContactReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.closed_by_contact";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = "contact";
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedByContactReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationClosedByContactReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.closed_by_contact";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = "contact";
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 5: `ConversationAgentAssignedReaction`**

`.../ConversationAgentAssignedReaction.cs` currently reads:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationAgentAssignedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationAgentAssignedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 6: `ConversationAgentUnassignedReaction`**

`.../ConversationAgentUnassignedReaction.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentUnassignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationAgentUnassignedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.agent_unassigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        conversation.AssignedAgentIdsJson = null;
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentUnassignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationAgentUnassignedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.agent_unassigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        conversation.AssignedAgentIdsJson = null;
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 7: `ConversationReplyReactionBase`**

`.../ConversationReplyReactionBase.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;

    protected ConversationReplyReactionBase(ISmartsuppRepository repository) => Repository = repository;

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
            await Repository.UpsertConversationAsync(
                SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp), cancellationToken);

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    protected ConversationReplyReactionBase(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        Repository = repository;
        _contactEnricher = contactEnricher;
    }

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
        {
            var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
            conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
            await Repository.UpsertConversationAsync(conversation, cancellationToken);
        }

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
```

- [ ] **Step 8: Update the 3 `ConversationReplyReactionBase` subclasses**

`.../ConversationContactRepliedReaction.cs` currently reads:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationContactRepliedReaction : ConversationReplyReactionBase
{
    public ConversationContactRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "conversation.contact_replied";
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationContactRepliedReaction : ConversationReplyReactionBase
{
    public ConversationContactRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.contact_replied";
}
```

Apply the identical pattern to `.../ConversationAgentRepliedReaction.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
{
    public ConversationAgentRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.agent_replied";
}
```

And to `.../ConversationBotRepliedReaction.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationBotRepliedReaction : ConversationReplyReactionBase
{
    public ConversationBotRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.bot_replied";
}
```

- [ ] **Step 9: `RefreshOrphanContactsHandler`**

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
currently reads:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

public class RefreshOrphanContactsHandler
    : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RefreshOrphanContactsHandler> _logger;

    public RefreshOrphanContactsHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ApplicationDbContext db,
        ILogger<RefreshOrphanContactsHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _db = db;
        _logger = logger;
    }
```

and the body of the try block reads:

```csharp
                // Re-attach the contact_id Smartsupp still knows about and let UpsertConversationAsync
                // pull the contact via REST (same path as the runtime fix).
                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
```

Change the field/constructor block to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

public class RefreshOrphanContactsHandler
    : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppContactEnricher _contactEnricher;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RefreshOrphanContactsHandler> _logger;

    public RefreshOrphanContactsHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ISmartsuppContactEnricher contactEnricher,
        ApplicationDbContext db,
        ILogger<RefreshOrphanContactsHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _contactEnricher = contactEnricher;
        _db = db;
        _logger = logger;
    }
```

Change the try-block body to:

```csharp
                // Re-attach the contact_id Smartsupp still knows about and let the contact
                // enricher pull the contact via REST (same path as the runtime fix, #3878).
                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                local = await _contactEnricher.EnrichContactAsync(local, cancellationToken);
                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
```

- [ ] **Step 10: Build**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
```

Expected: build **fails** at this point with constructor-argument-count errors in
`ConversationReactionsTests.cs` (the only place still constructing these reactions with one
argument) — that is expected and fixed in the next step. Confirm the failures are limited to that
one file:

```bash
dotnet build 2>&1 | grep -E "^.*error CS" | sed -E 's/^([^ (]+).*/\1/' | sort -u
```

Expected: only paths under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`.

- [ ] **Step 11: Fix `ConversationReactionsTests.cs`**

Add a shared enricher mock and update every affected constructor call. The field declarations at
the top of the class currently read:

```csharp
public class ConversationReactionsTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
```

Change to:

```csharp
public class ConversationReactionsTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppContactEnricher> _enricher = new();

    public ConversationReactionsTests()
    {
        _enricher
            .Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation c, CancellationToken _) => c);
    }
```

Add the using at the top of the file (alphabetically with the other `Anela.Heblo.Application.Features.Smartsupp.*` usings):

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
```

Then update every constructor call for the 9 affected reaction types. Every occurrence of:

```csharp
new ConversationOpenedReaction(_repo.Object)
```

becomes:

```csharp
new ConversationOpenedReaction(_repo.Object, _enricher.Object)
```

Apply the same `, _enricher.Object)` insertion to every other constructed reaction in this file that
takes `ISmartsuppRepository` alone: `ConversationClosedReaction`, `ConversationClosedByContactReaction`,
`ConversationContactRepliedReaction`, `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`,
`ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`, `ConversationRatedReaction`.
Leave `ConversationAgentJoinedReaction`, `ConversationAgentLeftReaction`, `ConversationMessageDeliveredReaction`,
and `ConversationMessageDeliveryFailedReaction` untouched — they use `ISmartsuppPresenceRepository`/
`ISmartsuppAgentCache` or don't call `UpsertConversationAsync`, and were not modified in Steps 1-9.

- [ ] **Step 12: Build and run the reaction tests**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConversationReactionsTests"
```

Expected: `Build succeeded.` with 0 errors; **Passed! - Failed: 0, Passed: 20**.

- [ ] **Step 13: Run the full Smartsupp test suite**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Smartsupp"
```

Expected: all tests pass. (`SmartsuppRepositoryUnknownContactFetchTests` and the Postgres integration
tests are untouched by this task and should still pass exactly as before — `SmartsuppRepository`
still has its old constructor and old REST path at this point, just unreachable from any reaction.)

- [ ] **Step 14: Commit**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs
git commit -m "feat(smartsupp): route contact enrichment through ISmartsuppContactEnricher (#3878)"
```

---

### task: remove-rest-dependency-from-smartsupp-repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUnknownContactFetchTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs`

#### Goal

Satisfy FR-1 and FR-2 from `spec.r1.md`: `SmartsuppRepository` no longer references
`ISmartsuppApiClient` at all. This is the final step that actually resolves the architecture
finding in issue #3878 — after this task, `Anela.Heblo.Persistence` has zero outbound-HTTP call
sites.

#### Context you need before touching code

- **`UpsertConversationAsync`'s local-contact hydration stays** — only the REST fallback and the
  wipe-on-miss branch are removed. The method still does its own `_db.SmartsuppContacts...FirstOrDefaultAsync`
  lookup for denormalization (an EF read, not an external call — this is not a layering violation
  and is unrelated to the issue).
- **Every remaining reference to `ISmartsuppApiClient` in `Anela.Heblo.Persistence` disappears in
  this task** — confirm with a grep in Step 5 below.
- **Do not touch the raw-SQL upsert text.** Only the C# control flow immediately above it changes.

#### Implementation steps

- [ ] **Step 1: Remove the field, constructor parameter, and wipe branch from `SmartsuppRepository`**

`backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` currently starts:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppRepository : ISmartsuppRepository
{
    private const int MaxOtherConversations = 20;

    private readonly ApplicationDbContext _db;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ILogger<SmartsuppRepository> _logger;

    public SmartsuppRepository(
        ApplicationDbContext db,
        ISmartsuppApiClient apiClient,
        ILogger<SmartsuppRepository> logger)
    {
        _db = db;
        _apiClient = apiClient;
        _logger = logger;
    }
```

Change to:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppRepository : ISmartsuppRepository
{
    private const int MaxOtherConversations = 20;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<SmartsuppRepository> _logger;

    public SmartsuppRepository(
        ApplicationDbContext db,
        ILogger<SmartsuppRepository> logger)
    {
        _db = db;
        _logger = logger;
    }
```

(`Microsoft.Extensions.Logging.Abstractions` is dropped because it was only used by
`NullLogger<SmartsuppRepository>` — check with `grep -n "NullLogger" backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`;
if any production usage remains, keep the using.)

- [ ] **Step 2: Simplify `UpsertConversationAsync`'s local-lookup block**

The method currently starts:

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
            {
                // Smartsupp webhooks reference contacts by id without inlining the name/email
                // and we cannot rely on a contact.* event arriving — pull the record via REST so
                // the FK link survives and the conversation row carries the display name.
                linkedContact = await TryFetchAndStageContactAsync(
                    conversation.ContactId, conversation.SyncedAt, cancellationToken);

                if (linkedContact is null)
                    conversation.ContactId = null;
            }
        }

        conversation.ContactName ??= linkedContact?.Name;
        conversation.ContactEmail ??= linkedContact?.Email;
```

Change to:

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
        }

        conversation.ContactName ??= linkedContact?.Name;
        conversation.ContactEmail ??= linkedContact?.Email;
```

Everything below this block (the `status` assignment and the raw-SQL `ExecuteSqlInterpolatedAsync`
call) is unchanged.

- [ ] **Step 3: Delete `TryFetchAndStageContactAsync` and `MapContactDataToEntity`**

The private methods currently at the bottom of the class read:

```csharp
    private async Task<SmartsuppContact?> TryFetchAndStageContactAsync(
        string contactId,
        DateTime syncedAt,
        CancellationToken cancellationToken)
    {
        SmartsuppContactData? data;
        try
        {
            data = await _apiClient.GetContactAsync(contactId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail open: webhook still saves the conversation without the contact link.
            // The orphan backfill job can pick it up later when Smartsupp REST is healthy.
            _logger.LogWarning(ex,
                "smartsupp: failed to fetch contact {ContactId} while upserting conversation; continuing without link",
                contactId);
            return null;
        }

        if (data is null)
            return null;

        var contact = MapContactDataToEntity(data, syncedAt);
        await UpsertContactAsync(contact, cancellationToken);
        return contact;
    }

    // Timestamps MUST be DateTimeKind.Utc: UpsertContactAsync writes them via
    // ExecuteSqlInterpolated, which types a bare DateTime as `timestamp with time zone`
    // and rejects Kind=Unspecified at the Npgsql layer. The webhook contact path
    // (SmartsuppPayloadMapper.MapContact) already produces Utc; this REST-staged path
    // must match, otherwise the enclosing conversation upsert throws and the conversation
    // is dropped (observed for Facebook Messenger contacts fetched on demand).
    internal static SmartsuppContact MapContactDataToEntity(SmartsuppContactData data, DateTime syncedAt) =>
        new()
        {
            Id = data.Id,
            Email = data.Email,
            Name = data.Name,
            Phone = data.Phone,
            Note = data.Note,
            BannedAt = data.BannedAt is { } bannedAt ? DateTime.SpecifyKind(bannedAt, DateTimeKind.Utc) : null,
            BannedBy = data.BannedBy,
            GdprApproved = data.GdprApproved,
            TagsJson = data.TagsJson,
            PropertiesJson = data.PropertiesJson,
            CreatedAt = DateTime.SpecifyKind(data.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(data.UpdatedAt, DateTimeKind.Utc),
            SyncedAt = DateTime.SpecifyKind(syncedAt, DateTimeKind.Utc),
        };

    public async Task UpdateVisitorCacheAsync(
```

Delete both methods entirely (they were moved to `SmartsuppContactEnricher` in Task 1), leaving:

```csharp
    public async Task UpdateVisitorCacheAsync(
```

- [ ] **Step 4: Build**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
```

Expected: build **fails** with constructor-argument-count errors — every remaining direct
`new SmartsuppRepository(...)` call site in the test project still passes an `apiClient` argument.
That's expected; fixed in the next two steps.

- [ ] **Step 5: Confirm no production code references `ISmartsuppApiClient` from Persistence**

```bash
grep -rln "ISmartsuppApiClient" backend/src/Anela.Heblo.Persistence
```

Expected: **no output**.

- [ ] **Step 6: Fix `SmartsuppRepositoryUpdatedAtGuardTests.cs`'s test factory**

The factory at the top of the file currently reads:

```csharp
internal static class SmartsuppRepositoryTestFactory
{
    public static SmartsuppRepository New(ApplicationDbContext db, ISmartsuppApiClient? apiClient = null) =>
        new(db, apiClient ?? Mock.Of<ISmartsuppApiClient>(), NullLogger<SmartsuppRepository>.Instance);
}
```

Change to:

```csharp
internal static class SmartsuppRepositoryTestFactory
{
    public static SmartsuppRepository New(ApplicationDbContext db) =>
        new(db, NullLogger<SmartsuppRepository>.Instance);
}
```

Every call site in this repo passes `db` only already (confirmed by
`grep -rn "SmartsuppRepositoryTestFactory.New" backend/test` returning only single-argument calls),
so no other line in this file or in `SmartsuppRepositoryContactConversationsTests.cs` needs editing.

- [ ] **Step 7: Fix `SmartsuppRepositoryUpsertIntegrationTests.cs`'s `CreateRepository` helper**

Currently reads:

```csharp
    private SmartsuppRepository CreateRepository(ApplicationDbContext? context = null)
    {
        var apiClient = Substitute.For<ISmartsuppApiClient>();
        return new SmartsuppRepository(
            context ?? _context,
            apiClient,
            NullLogger<SmartsuppRepository>.Instance);
    }
```

Change to:

```csharp
    private SmartsuppRepository CreateRepository(ApplicationDbContext? context = null)
    {
        return new SmartsuppRepository(
            context ?? _context,
            NullLogger<SmartsuppRepository>.Instance);
    }
```

Check whether `ISmartsuppApiClient`/`Substitute` (NSubstitute) is still used elsewhere in this file:

```bash
grep -n "ISmartsuppApiClient\|Substitute" backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs
```

If no other usage remains, remove the now-unused `using NSubstitute;` and
`using Anela.Heblo.Domain.Features.Smartsupp;` — only if `Anela.Heblo.Domain.Features.Smartsupp`
isn't needed for `SmartsuppConversation`/`SmartsuppContact` elsewhere in the file (it almost
certainly still is, for the entity types — check before removing that one).

- [ ] **Step 8: Remove the four REST-behavior tests from `SmartsuppRepositoryUnknownContactFetchTests.cs`**

Delete these four `[Fact]` methods (already ported to `SmartsuppContactEnricherTests.cs` in Task 1):
`UpsertConversationAsync_FetchesContactViaRest_WhenLocalContactMissing`,
`UpsertConversationAsync_WipesContactId_WhenRestReturnsNull`,
`UpsertConversationAsync_WipesContactIdAndLogsWarning_WhenRestThrows`,
`UpsertConversationAsync_DoesNotCallRest_WhenContactAlreadyInDb`.

Keep `ListOrphanContactConversationIdsAsync_ReturnsOnlyConversationsWithNoNameOrEmail` and the
`NewContext`/`MakeConversation`/`MakeContactData` helpers it depends on (delete `MakeContactData`
only if nothing else in the file still calls it after the four deletions — check first).

The class constructs its `SmartsuppRepository` without an `apiClient` mock now:

```csharp
var repo = new SmartsuppRepository(db, NullLogger<SmartsuppRepository>.Instance);
```

Update the remaining test's construction line if it currently passes a mocked `ISmartsuppApiClient`
(the orphan-listing test constructs it as `new SmartsuppRepository(db, Mock.Of<ISmartsuppApiClient>(), NullLogger<SmartsuppRepository>.Instance)`
— drop the middle argument).

- [ ] **Step 9: Build and run the full Smartsupp test suite**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
```

Expected: `Build succeeded.` with **0 Error(s)**.

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Smartsupp"
```

Expected: all tests pass, with the same total behavioral coverage as before this plan started
(REST-fetch-on-miss, fail-open on error, fail-open on null, no-call-when-known now live in
`SmartsuppContactEnricherTests`; orphan-listing, denorm hydration, COALESCE/UpdatedAt-guard SQL
behavior still live in the Persistence test files).

- [ ] **Step 10: Run the Postgres integration tests (if a local Postgres is available in this environment)**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SmartsuppRepositoryUpsertIntegrationTests"
```

Expected: passes if Postgres is reachable per this repo's existing integration-test setup
(`docs/testing/testing-strategy.md`); if the environment has no Postgres available, this step is
skipped here and must be verified in CI — do not mark this task done without at least confirming
the build compiles and the non-Postgres tests pass.

- [ ] **Step 11: Full solution build + format check (final gate for the whole plan)**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
dotnet format --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds with 0 errors/0 new warnings, `dotnet format` reports no changes needed,
and the full test suite passes.

- [ ] **Step 12: Confirm the issue's exact finding is resolved**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs
grep -rn "ISmartsuppApiClient" backend/src/Anela.Heblo.Persistence
```

Expected: **no output** — this is the acceptance bar from issue #3878 and `spec.r1.md` FR-1.

- [ ] **Step 13: Commit**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUnknownContactFetchTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs \
        backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs
git commit -m "refactor(smartsupp): remove ISmartsuppApiClient from SmartsuppRepository (closes #3878)"
```
