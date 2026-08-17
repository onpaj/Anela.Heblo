# Design: Unit Test Coverage for RefreshOrphanContactsHandler

## Component Design

### `RefreshOrphanContactsHandlerTests` (new)
Location: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`
Namespace: `Anela.Heblo.Tests.Features.Smartsupp`

Responsibility: exercise `RefreshOrphanContactsHandler.Handle` against the four gap scenarios from the spec (FR-1 through FR-4), plus a batch-continuation check, without modifying the handler itself.

Structure (mirrors `CloseConversationHandlerTests` in the same directory):
- **Fields:** `Mock<ISmartsuppRepository> _repo`, `Mock<ISmartsuppApiClient> _apiClient`, `Mock<ISmartsuppContactEnricher> _enricher`, `Mock<ILogger<RefreshOrphanContactsHandler>> _logger` — each a fresh `new()` per test instance (xUnit creates a new test class instance per `[Fact]`, so no shared mutable state between tests).
- **`CreateContext()`:** static factory returning a real `ApplicationDbContext` backed by `UseInMemoryDatabase($"orphan_{Guid.NewGuid()}")` — one throwaway database per test, per existing convention in `ListWebhookAuditHandlerTests`.
- **`CreateHandler(ApplicationDbContext db)`:** instance factory wiring the four mocks' `.Object` plus the passed-in real `db` into `new RefreshOrphanContactsHandler(...)`.
- **Per-scenario `Setup...` helpers:** small private methods that configure `_apiClient` / `_enricher` / `_repo` mocks for one branch, following the `SetupConversation(...)` pattern in `CloseConversationHandlerTests`.
- **Test methods (one `[Fact]` per row below):**

| Test | Covers | Key assertions |
|---|---|---|
| `Handle_IncrementsSkippedNoContactId_WhenRemoteContactIdIsNull` | FR-1 | `SkippedNoContactId == 1`; `_enricher.EnrichContactAsync` never called; `_repo.UpsertConversationAsync` never called |
| `Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound` | FR-2 | `SkippedNoContactId == 1`; no matching row seeded for the ID; enricher/upsert never called |
| `Handle_IsolatesFailure_WhenEnrichContactAsyncThrows` | FR-3 | `Failed == 1`; `FailedIds` contains the ID; `Updated == 0` for that item; `ChangeTracker.Entries()` empty after `Handle` returns; a second, successful item in the same batch is still `Updated` |
| `Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows` | FR-4 | Same assertions as FR-3, triggered via `_repo.UpsertConversationAsync` throwing instead |
| *(only if coverage confirms it's missing — see arch-review Risks)* `Handle_IncrementsUpdated_WhenItemProcessedSuccessfully` | happy path / coverage baseline | `Updated == 1`; response counters otherwise zero; `_repo.UpsertConversationAsync` and `_repo.SaveChangesAsync` each called once |

No new production classes, interfaces, or test-support/helper classes outside this one test file are introduced.

## Data Schemas

No database schema, API contract, or DTO changes. The only "schemas" relevant here are the **in-memory test fixture shapes** used to seed `ApplicationDbContext` and to stub mock return values — all using existing types unmodified:

### Seeded `SmartsuppConversation` row (local DB state)
```csharp
new SmartsuppConversation
{
    Id = "<conversation-id under test>",
    Status = SmartsuppConversationStatus.Open,   // or whatever satisfies non-null enum
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow,
    SyncedAt = DateTime.UtcNow,
    Messages = new(),
    // ContactId intentionally left null/unset pre-refresh — that's the "orphan" state
}
```
Seeded via `ctx.SmartsuppConversations.Add(...)` + `await ctx.SaveChangesAsync()`, then `ctx.ChangeTracker.Clear()` (test-side, before constructing the handler) so pre-handler tracking noise doesn't pollute the post-`Handle` `ChangeTracker.Entries()` assertion used for FR-3/FR-4.

For FR-2 (local conversation not found), no row is seeded for the ID under test — the context is either empty or seeded only with unrelated IDs.

### Mocked `SmartsuppConversationData` (remote API response shape)
```csharp
new SmartsuppConversationData
{
    Id = "<conversation-id under test>",
    ContactId = null,          // FR-1 case
    // or:
    ContactId = "contact-123", // FR-2/FR-3/FR-4/happy-path cases
}
```
Returned from `_apiClient.Setup(a => a.GetConversationAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(...)`.

### Response shape asserted (existing type, unchanged)
```csharp
RefreshOrphanContactsResponse
{
    Scanned,           // count of IDs from ListOrphanContactConversationIdsAsync
    Updated,           // successful items
    SkippedNoContactId,// FR-1 + FR-2 combined counter
    Failed,            // FR-3 + FR-4 combined counter
    FailedIds,         // List<string> of failed conversation IDs
}
```

No changes to `RefreshOrphanContactsRequest`, `RefreshOrphanContactsResponse`, `SmartsuppConversation`, `SmartsuppConversationData`, or any repository/API-client/enricher interface.
