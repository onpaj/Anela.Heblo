# Architecture Review: Unit Test Coverage for RefreshOrphanContactsHandler

## Skip Design: true
Pure backend unit-test coverage work against an existing MediatR handler. No new or changed UI, API contract, or visual component is involved.

## Architectural Fit Assessment
`RefreshOrphanContactsHandler` is a standard MediatR `IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>` in the Smartsupp vertical slice (`Features/Smartsupp/UseCases/RefreshOrphanContacts/`). It has four collaborators: `ISmartsuppRepository` (mockable interface), `ISmartsuppApiClient` (mockable interface), `ISmartsuppContactEnricher` (mockable interface), and `ApplicationDbContext` (concrete EF Core context, used directly for the local lookup via `_db.SmartsuppConversations.FirstOrDefaultAsync`).

This mixed dependency shape — interface-mocked collaborators plus a direct, un-abstracted `ApplicationDbContext` read — already exists elsewhere in this module (e.g. `ListWebhookAuditHandlerTests`, `GetWebhookAuditEntryHandlerTests` construct a real `ApplicationDbContext` with the EF Core in-memory provider rather than mocking it). The brief's suggested test approach — mock the three interfaces, use a real in-memory `ApplicationDbContext` — is therefore consistent with established convention in this test project. No new test infrastructure or pattern is required.

Existing sibling test `CloseConversationHandlerTests.cs` establishes the mocking convention for this feature area: Moq (`Mock<T>`), FluentAssertions (`.Should()`), xUnit (`[Fact]`), a `CreateHandler()` factory method, and per-scenario `Setup...` helper methods. The new test class should follow this exact shape.

## Proposed Architecture

### Component Overview
```
RefreshOrphanContactsHandlerTests (new)
        |
        v
RefreshOrphanContactsHandler (SUT, unchanged)
   |        |            |                  |
   v        v            v                  v
Mock<      Mock<      Mock<               ApplicationDbContext
ISmartsupp ISmartsupp ISmartsupp          (real, EF Core
Repository>ApiClient> ContactEnricher>    InMemory provider)
```

No production code component changes. The only new component is the test class itself.

### Key Design Decisions

#### Decision 1: DbContext strategy — real in-memory provider, not mocked
**Options considered:**
- Mock `ApplicationDbContext` / `DbSet<SmartsuppConversation>` directly (e.g. via a mocking-friendly wrapper).
- Use EF Core's `UseInMemoryDatabase` provider with a real `ApplicationDbContext` instance, seeded per test.

**Chosen approach:** Real in-memory provider, seeded via `ctx.SmartsuppConversations.Add(...)` + `SaveChangesAsync()` before constructing the handler, exactly as `ListWebhookAuditHandlerTests.CreateContext()` does (`new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"orphan_{Guid.NewGuid()}").Options`).

**Rationale:** `DbSet<T>` and `DbContext` are not designed for mocking (LINQ provider behavior does not mock cleanly), and this codebase already has an established, working convention for this exact case. It also makes the `ChangeTracker.Clear()` assertion (Decision 2) directly observable, which a mocked DbContext could not provide.

#### Decision 2: Verifying `ChangeTracker.Clear()` without a spy
**Options considered:**
- Wrap/spy `ApplicationDbContext` to intercept the `ChangeTracker.Clear()` call.
- Use the real in-memory context and assert on `ChangeTracker.Entries()` state directly, before and after the failing item.

**Chosen approach:** Use the real context's `ChangeTracker.Entries()` as the assertion surface. Sequence: seed a local `SmartsuppConversation` row for the failing ID, run the handler so that `local.ContactId = remote.ContactId` mutates a *tracked* entity, force `EnrichContactAsync` (or `UpsertConversationAsync`) to throw for that item, then assert `ctx.ChangeTracker.Entries().Should().BeEmpty()` (or, in a multi-item batch, that no `Modified`/`Added` entry remains for the failed conversation) after `Handle` returns.

**Rationale:** No production seam needs to change to make this observable — `ChangeTracker.Entries()` is public API on the real `DbContext` already used by the handler. This directly proves the corruption-guard fires, rather than merely proving a method was called (a spy would prove invocation but not the actual state-clearing effect the brief cares about).

#### Decision 3: Distinguishing the two `SkippedNoContactId` causes (FR-1 vs FR-2)
**Options considered:**
- Assert only the counter value (`SkippedNoContactId == 1`) for both paths — insufficient, since both branches increment the same counter and a bug that merges/misfires the branches would go undetected.
- Assert counter value *and* verify no downstream call happened for the specific case, using distinct API/DB setups per test so each test can only pass via its intended branch.

**Chosen approach:** Two separate `[Fact]` tests.
- FR-1 test: API returns a conversation with `ContactId == null`; seed the DB with **no** matching local row at all (so if the handler incorrectly reached the DB-lookup branch, a null exception or different code path would surface, not a false pass). Assert `SkippedNoContactId == 1` and that `_contactEnricher` / `_repository.UpsertConversationAsync` were never invoked.
- FR-2 test: API returns a conversation with a non-null `ContactId`; seed the DB with **no** matching local row for that ID (empty `SmartsuppConversations` set, or a row with a different `Id`). Assert `SkippedNoContactId == 1` and that `_contactEnricher` / `_repository.UpsertConversationAsync` were never invoked.

**Rationale:** Both tests already naturally exercise only their own branch given the handler's actual control flow (the `remote?.ContactId is null` check happens first and short-circuits before the DB is ever queried), so no additional instrumentation is needed — just correct, non-overlapping Arrange setup per test.

## Implementation Guidance

### Directory / Module Structure
New file: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`

Namespace: `Anela.Heblo.Tests.Features.Smartsupp` (matching sibling files in the same directory, e.g. `CloseConversationHandlerTests`).

No other files change.

### Interfaces and Contracts
No interface or contract changes. Tests consume the existing public surface:
- `RefreshOrphanContactsHandler(ISmartsuppRepository, ISmartsuppApiClient, ISmartsuppContactEnricher, ApplicationDbContext, ILogger<RefreshOrphanContactsHandler>)`
- `RefreshOrphanContactsRequest` (parameterless)
- `RefreshOrphanContactsResponse { Scanned, Updated, SkippedNoContactId, Failed, FailedIds }`
- `ISmartsuppRepository.ListOrphanContactConversationIdsAsync(CancellationToken)` — mock to return the batch of conversation IDs under test.
- `ISmartsuppRepository.UpsertConversationAsync(SmartsuppConversation, CancellationToken)` and `SaveChangesAsync(CancellationToken)` — mock as no-ops for the success path; mock `UpsertConversationAsync` to throw for FR-4.
- `ISmartsuppApiClient.GetConversationAsync(string, CancellationToken)` — mock per scenario.
- `ISmartsuppContactEnricher.EnrichContactAsync(SmartsuppConversation, CancellationToken)` — mock to return the conversation unchanged for success paths; mock to throw for FR-3.
- `ApplicationDbContext` — real instance, `UseInMemoryDatabase` with a unique database name per test (`Guid.NewGuid()` suffix, matching `ListWebhookAuditHandlerTests.CreateContext()`), seeded with `SmartsuppConversation` rows as each scenario requires.

Suggested test-class scaffolding (mirroring `CloseConversationHandlerTests`):
```csharp
public class RefreshOrphanContactsHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppContactEnricher> _enricher = new();
    private readonly Mock<ILogger<RefreshOrphanContactsHandler>> _logger = new();

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"orphan_{Guid.NewGuid()}").Options);

    private RefreshOrphanContactsHandler CreateHandler(ApplicationDbContext db) =>
        new(_repo.Object, _apiClient.Object, _enricher.Object, db, _logger.Object);

    // one [Fact] per FR-1..FR-4, plus a "continues after failure" batch test
}
```

### Data Flow
1. `_repository.ListOrphanContactConversationIdsAsync` (mocked) returns the batch of IDs to process — this is the single seam that controls which IDs the loop iterates.
2. For each ID: `_apiClient.GetConversationAsync` (mocked) → null-`ContactId` check (FR-1) → `_db.SmartsuppConversations.FirstOrDefaultAsync` (real, in-memory, seeded per test) → not-found check (FR-2) → `_contactEnricher.EnrichContactAsync` (mocked, can throw for FR-3) → `_repository.UpsertConversationAsync` + `SaveChangesAsync` (mocked, can throw for FR-4) → counters updated.
3. On any exception in the try block: `Failed++`, `FailedIds.Add(id)`, log, `_db.ChangeTracker.Clear()` (assert via `ctx.ChangeTracker.Entries()` on the same real context instance passed into the handler) — then loop continues to the next ID.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reusing one in-memory DB name across tests causes cross-test data bleed | Medium | Always generate a unique `UseInMemoryDatabase` name per test (`Guid.NewGuid()` suffix), per existing convention — do not share a static context. |
| FR-3/FR-4 "continue after failure" assertion is trivially satisfied by a batch where the second item also happens to succeed by coincidence | Low | Explicitly assert the second item's ID appears in neither `FailedIds` nor is double-counted, and that `Updated` reflects exactly the successful item(s), not just that the test didn't crash. |
| `ChangeTracker.Entries()` assertion is fragile if unrelated tracked entities exist from context setup/seeding calls | Low | Call `ctx.ChangeTracker.Clear()` (test-side) immediately after the seeding `SaveChangesAsync()`, before invoking the handler, so any pre-handler tracking noise is removed and the post-`Handle` assertion reflects only handler-caused state. |
| Coverage target (60%) not reached by just the four brief-listed cases if the happy path is also uncovered | Medium | Architect recommends the planner include a baseline "happy path" success test (single item, all collaborators succeed) if not already covered elsewhere — confirm via existing test suite/coverage report before assuming it's needed; if it already exists, skip it per the spec's Out-of-Scope note. |

## Specification Amendments
- **FR-2 clarification:** seed data must ensure the *only* difference from FR-1 is where the mismatch occurs (API-side null vs. DB-side missing row) — the architect's Decision 3 above documents concretely how to keep the two branches non-overlapping in test setup. No change to acceptance criteria, just implementation-level guidance now available to the planner.
- **New guidance for FR-3/FR-4 assertions:** use direct `ChangeTracker.Entries()` inspection instead of a spy/wrapper (spec left the mechanism open; this review resolves it — see Decision 2).
- **Coverage baseline check added** (see Risks table): before finalizing the task list, confirm whether a happy-path test already exists for this handler; if not, add one as a prerequisite to reliably clear the 60% threshold, since the four brief-listed branches alone cover only the error/skip paths, not the `Updated++` success line.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is additive test-only work against existing, already-registered dependencies (`ApplicationDbContext`, `ISmartsuppRepository`, `ISmartsuppApiClient`, `ISmartsuppContactEnricher` are all already used and testable in this project as demonstrated by sibling test files).
