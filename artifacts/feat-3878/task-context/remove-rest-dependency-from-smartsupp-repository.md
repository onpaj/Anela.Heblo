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
