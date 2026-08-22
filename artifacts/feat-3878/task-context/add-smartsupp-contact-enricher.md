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

