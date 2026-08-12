### task: encapsulate-refresh-orphan-contacts-lookup

**Goal**

Remove `RefreshOrphanContactsHandler`'s direct `ApplicationDbContext` dependency by adding a single
tracked by-id conversation lookup to the existing `ISmartsuppRepository`/`SmartsuppRepository`, and
drop the now-orphaned `_db.ChangeTracker.Clear()` call from the handler's catch block. No behavior,
HTTP contract, or schema change. Independent of the `create-smartsupp-webhook-audit-repository` task
— touches entirely different files.

**Context** (self-contained — you only read this section)

`RefreshOrphanContactsHandler` (`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/
RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`) already injects `ISmartsuppRepository` for
`ListOrphanContactConversationIdsAsync`, `UpsertConversationAsync`, and `SaveChangesAsync`. Its only
direct-context use is a bare tracked lookup:
```csharp
var local = await _db.SmartsuppConversations
    .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
```
`ISmartsuppRepository.GetConversationAsync` already exists but is `AsNoTracking()` and `Include`s
`Messages`/`Contact` — wrong shape here, since the handler needs a *tracked* bare entity so it can set
`local.ContactId`/`local.SyncedAt` in place before `UpsertConversationAsync`. Add a new,
narrowly-scoped method instead of overloading `GetConversationAsync`.

Full current handler (`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/
RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`):
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

    public async Task<RefreshOrphanContactsResponse> Handle(
        RefreshOrphanContactsRequest request,
        CancellationToken cancellationToken)
    {
        var response = new RefreshOrphanContactsResponse();
        var ids = await _repository.ListOrphanContactConversationIdsAsync(cancellationToken);
        response.Scanned = ids.Count;

        foreach (var conversationId in ids)
        {
            try
            {
                var remote = await _apiClient.GetConversationAsync(conversationId, cancellationToken);
                if (remote?.ContactId is null)
                {
                    response.SkippedNoContactId++;
                    continue;
                }

                var local = await _db.SmartsuppConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
                if (local is null)
                {
                    response.SkippedNoContactId++;
                    continue;
                }

                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                response.Updated++;
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.FailedIds.Add(conversationId);
                _logger.LogError(ex,
                    "smartsupp: orphan-contacts backfill failed for conversation {ConversationId}",
                    conversationId);
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "smartsupp orphan-contacts backfill done: scanned={Scanned} updated={Updated} skipped={Skipped} failed={Failed}",
            response.Scanned, response.Updated, response.SkippedNoContactId, response.Failed);

        return response;
    }
}
```

`_db.ChangeTracker.Clear()` in the catch block exists to stop a failed iteration's partially-tracked
`SmartsuppConversation` from poisoning the next iteration's lookup. It is dropped, not relocated: no
other `ISmartsuppRepository` consumer resets the tracker between calls, and re-exposing a
`ClearTracking()` escape hatch on the interface purely to preserve this one caller's defensive habit
re-introduces the exact EF-leaking-into-Application-layer problem this issue exists to close. The new
regression test below (`Handle_ContinuesToNextConversation_WhenOneFailsMidLoop`) is required to prove
this is safe.

**Files to create/modify**

- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`
  (no test file exists for this handler today — confirmed by search)

**Implementation steps**

1. **`ISmartsuppRepository.cs`** — add this method to the interface, immediately after
   `GetConversationAsync` (which ends at line 358 in the current file, right before
   `Task UpsertContactAsync(...)`):
   ```csharp
   /// <summary>
   /// Tracked, bare lookup by primary key (no Includes) — for callers that need to mutate the
   /// returned entity in place before calling UpsertConversationAsync/SaveChangesAsync. Use
   /// GetConversationAsync instead for read-only display (no-tracking, includes Messages/Contact).
   /// </summary>
   Task<SmartsuppConversation?> FindConversationByIdAsync(
       string conversationId,
       CancellationToken cancellationToken);
   ```

2. **`SmartsuppRepository.cs`** — add the implementation immediately after `GetConversationAsync`
   (currently lines 46–52):
   ```csharp
   public async Task<SmartsuppConversation?> FindConversationByIdAsync(
       string conversationId,
       CancellationToken cancellationToken) =>
       await _db.SmartsuppConversations
           .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
   ```
   (Tracked by default — no `.AsNoTracking()` — matching the original inline query exactly.)

3. **`RefreshOrphanContactsHandler.cs` — full replacement:**
   ```csharp
   using Anela.Heblo.Domain.Features.Smartsupp;
   using MediatR;
   using Microsoft.Extensions.Logging;

   namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

   public class RefreshOrphanContactsHandler
       : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
   {
       private readonly ISmartsuppRepository _repository;
       private readonly ISmartsuppApiClient _apiClient;
       private readonly ILogger<RefreshOrphanContactsHandler> _logger;

       public RefreshOrphanContactsHandler(
           ISmartsuppRepository repository,
           ISmartsuppApiClient apiClient,
           ILogger<RefreshOrphanContactsHandler> logger)
       {
           _repository = repository;
           _apiClient = apiClient;
           _logger = logger;
       }

       public async Task<RefreshOrphanContactsResponse> Handle(
           RefreshOrphanContactsRequest request,
           CancellationToken cancellationToken)
       {
           var response = new RefreshOrphanContactsResponse();
           var ids = await _repository.ListOrphanContactConversationIdsAsync(cancellationToken);
           response.Scanned = ids.Count;

           foreach (var conversationId in ids)
           {
               try
               {
                   var remote = await _apiClient.GetConversationAsync(conversationId, cancellationToken);
                   if (remote?.ContactId is null)
                   {
                       response.SkippedNoContactId++;
                       continue;
                   }

                   var local = await _repository.FindConversationByIdAsync(conversationId, cancellationToken);
                   if (local is null)
                   {
                       response.SkippedNoContactId++;
                       continue;
                   }

                   // Re-attach the contact_id Smartsupp still knows about and let UpsertConversationAsync
                   // pull the contact via REST (same path as the runtime fix).
                   local.ContactId = remote.ContactId;
                   local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                   await _repository.UpsertConversationAsync(local, cancellationToken);
                   await _repository.SaveChangesAsync(cancellationToken);

                   response.Updated++;
               }
               catch (Exception ex)
               {
                   response.Failed++;
                   response.FailedIds.Add(conversationId);
                   _logger.LogError(ex,
                       "smartsupp: orphan-contacts backfill failed for conversation {ConversationId}",
                       conversationId);
               }
           }

           _logger.LogInformation(
               "smartsupp orphan-contacts backfill done: scanned={Scanned} updated={Updated} skipped={Skipped} failed={Failed}",
               response.Scanned, response.Updated, response.SkippedNoContactId, response.Failed);

           return response;
       }
   }
   ```
   Note the constructor drops the `ApplicationDbContext db` parameter entirely (3 parameters instead
   of 4) and the `using Anela.Heblo.Persistence;` / `using Microsoft.EntityFrameworkCore;` imports are
   removed since nothing in the file references them any longer. The `catch` block no longer calls
   `_db.ChangeTracker.Clear()` — it has nothing to clear.

**Tests to write** (new file `backend/test/Anela.Heblo.Tests/Features/Smartsupp/
RefreshOrphanContactsHandlerTests.cs`)

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RefreshOrphanContactsHandlerTests
{
    private static SmartsuppConversation MakeConversation(string id) => new()
    {
        Id = id,
        Status = SmartsuppConversationStatus.Open,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SyncedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Handle_ReattachesContactId_ForEachOrphanWithARemoteContact()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });
        var local = MakeConversation("conv-1");
        repository.Setup(r => r.FindConversationByIdAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.Scanned.Should().Be(1);
        response.Updated.Should().Be(1);
        response.Failed.Should().Be(0);
        local.ContactId.Should().Be("contact-9");
        repository.Verify(r => r.UpsertConversationAsync(local, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsConversation_WhenRemoteHasNoContactId()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = null });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        repository.Verify(r => r.FindConversationByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsConversation_WhenLocalRowNoLongerExists()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });
        repository.Setup(r => r.FindConversationByIdAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation?)null);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        repository.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesToNextConversation_WhenOneFailsMidLoop()
    {
        // Regression test for dropping _db.ChangeTracker.Clear(): confirms a failure on the first
        // conversation in a batch does not prevent the second one from being processed and updated.
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-fail", "conv-ok" });

        var local = MakeConversation("conv-ok");
        repository.Setup(r => r.FindConversationByIdAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        repository.Setup(r => r.FindConversationByIdAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                new SmartsuppConversationData { Id = id, ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.Scanned.Should().Be(2);
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(1);
        local.ContactId.Should().Be("contact-9");
    }
}
```

**Acceptance criteria**

- `ISmartsuppRepository` gains exactly one new method (`FindConversationByIdAsync`), implemented in
  `SmartsuppRepository` as a tracked, `Include`-free lookup by primary key.
- `RefreshOrphanContactsHandler` no longer references `ApplicationDbContext`, `using
  Anela.Heblo.Persistence;`, or `using Microsoft.EntityFrameworkCore;` in any form; its constructor
  takes exactly `ISmartsuppRepository`, `ISmartsuppApiClient`, `ILogger<RefreshOrphanContactsHandler>`.
- The `catch` block no longer calls `_db.ChangeTracker.Clear()` (or any equivalent) and the new
  `Handle_ContinuesToNextConversation_WhenOneFailsMidLoop` test proves a mid-batch failure does not
  block subsequent conversations from updating.
- All four new tests in `RefreshOrphanContactsHandlerTests.cs` pass; existing loop semantics (`Scanned`,
  `Updated`, `SkippedNoContactId`, `Failed`, `FailedIds`) are unchanged from before the refactor.
- `cd backend && dotnet build` succeeds.
- `cd backend && dotnet format --verify-no-changes` reports no changes.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
  passes.
