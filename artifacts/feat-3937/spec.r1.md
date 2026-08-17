# Specification: Unit Test Coverage for RefreshOrphanContactsHandler

## Summary
`RefreshOrphanContactsHandler` (Smartsupp module) currently has 24.5% line coverage against a 60% threshold. Three execution paths are untested: the remote-contact-null skip path, the local-conversation-not-found skip path, and per-item exception isolation (including the `ChangeTracker.Clear()` EF-corruption guard). This specification defines the unit tests required to close the gap and lock in the handler's documented failure-isolation behavior.

## Background
The handler backfills/refreshes Smartsupp conversations whose local records are missing contact linkage ("orphan contacts"). It iterates a batch of remote conversation IDs, calls the Smartsupp API to resolve each conversation, looks up the matching local record, enriches contact data, and upserts. Because this loop processes items one at a time against a shared `DbContext`, a single item's failure must not corrupt tracking state for subsequent items — that's what `ChangeTracker.Clear()` on the failure path protects against. The counters produced (`SkippedNoContactId`, `Failed`, `Updated`, `FailedIds`) feed the operational summary log used to gauge backfill health; if the counters miscount, operators get a false picture of how the batch performed silently.

## Functional Requirements

### FR-1: Test — remote contact has no ContactId
When `_apiClient.GetConversationAsync` returns a response whose `ContactId` is `null`, the handler must not attempt a local lookup or enrichment for that item; it must increment `SkippedNoContactId` and proceed to the next item in the batch.
**Acceptance criteria:**
- Given a mocked `ISmartsuppApiClient.GetConversationAsync` returning a conversation response with `ContactId == null`, when the handler processes the batch, then `SkippedNoContactId` is incremented by exactly 1 for that item.
- No call is made to `ISmartsuppContactEnricher.EnrichContactAsync` or `ISmartsuppRepository.UpsertConversationAsync` for that item.
- Processing continues to the remaining items in the batch (verified via a batch containing at least one other, successfully-processed item).

### FR-2: Test — local conversation not found
When the remote contact ID resolves successfully but the corresponding local conversation cannot be found via the EF query, the handler must increment `SkippedNoContactId` and continue, without throwing.
**Acceptance criteria:**
- Given an in-memory `ApplicationDbContext` seeded without a matching local conversation row for the ID under test, when the handler processes that item, then `SkippedNoContactId` is incremented by exactly 1.
- This is verified as a path distinct from FR-1 (i.e., the test setup provides a non-null `ContactId` from the API but no matching local row), so the two skip causes are not conflated by a shared counter check alone — assert via a scenario that isolates this branch (e.g., only this path is exercised in the test, or intermediate state is asserted before the counter increment).
- No exception propagates out of the handler for this case.

### FR-3: Test — per-item exception isolation on enrichment failure
When `ISmartsuppContactEnricher.EnrichContactAsync` throws for an item, the handler must:
(a) increment `Failed` by 1,
(b) add the item's ID to `FailedIds`,
(c) call `_db.ChangeTracker.Clear()` before moving to the next item,
(d) NOT increment `Updated` for that item,
(e) continue processing subsequent items in the batch rather than aborting.
**Acceptance criteria:**
- Given a mocked `ISmartsuppContactEnricher.EnrichContactAsync` that throws for a specific conversation ID within a multi-item batch, when the handler runs, then `Failed == 1`, `FailedIds` contains that ID, and `Updated` does not count that item.
- The test verifies `ChangeTracker.Clear()` was invoked as a result of the failure (e.g., via a spy/wrapper on the context, or by asserting no stale tracked entity from the failed item leaks into the next item's save call).
- A subsequent, successful item in the same batch is still processed and its `Updated`/success counters reflect success — proving the loop did not abort after the failure.

### FR-4: Test — per-item exception isolation on repository upsert failure
When `ISmartsuppRepository.UpsertConversationAsync` throws for an item (after successful enrichment), the handler must exhibit the same isolation behavior as FR-3: increment `Failed`, record the ID in `FailedIds`, call `ChangeTracker.Clear()`, not increment `Updated`, and continue to the next item.
**Acceptance criteria:**
- Given a mocked `ISmartsuppRepository.UpsertConversationAsync` that throws for one item in a multi-item batch, when the handler runs, then `Failed`, `FailedIds`, and `Updated` reflect the same contract as FR-3 for that item.
- Batch processing continues to subsequent items after the throw.

## Non-Functional Requirements

### NFR-1: No behavior change
These are coverage-only additions. The handler's production code must not change as part of this work unless a test reveals a genuine discrepancy between documented behavior (per the brief) and actual behavior — if such a discrepancy is found, it must be flagged rather than silently "fixed" by adjusting the test to match unintended behavior.

### NFR-2: Test isolation and determinism
Tests must use mocked `ISmartsuppRepository`, `ISmartsuppApiClient`, and `ISmartsuppContactEnricher`, plus an in-memory `ApplicationDbContext` (per the brief's suggested approach), so they run deterministically without external dependencies (no live Smartsupp API calls, no real database).

### NFR-3: Coverage target
The change must raise `RefreshOrphanContactsHandler.cs` line coverage from 24.5% to at least the 60% filter threshold.

## Data Model
No new or changed data model. Tests exercise the existing `RefreshOrphanContactsHandler`, its counters (`SkippedNoContactId`, `Failed`, `Updated`, `FailedIds`), and existing collaborator interfaces (`ISmartsuppRepository`, `ISmartsuppApiClient`, `ISmartsuppContactEnricher`) plus `ApplicationDbContext` (in-memory provider for tests).

## API / Interface Design
No new public API. This is a test-only change targeting:
`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`

Test file location follows existing project test conventions (mirrored path under the corresponding test project — to be confirmed by the architect against actual repository layout).

## Dependencies
- Existing mocking framework already in use in the test project (e.g., Moq/NSubstitute — to be confirmed by architecture review against existing test conventions).
- EF Core in-memory provider (or existing test DB fixture pattern already used elsewhere in the codebase) for `ApplicationDbContext`.
- No new third-party dependencies expected.

## Out of Scope
- Any refactor of `RefreshOrphanContactsHandler` itself, beyond what's strictly necessary to make it testable (e.g., no new seams should be introduced unless the architecture review determines the class cannot otherwise be unit tested).
- Coverage of the handler's "happy path" (fully successful item processing) if already covered by existing tests — the brief only calls out the three gaps above; the architect/planner should confirm existing test coverage before assuming a happy-path test is needed too.
- Integration or E2E-level testing of the Smartsupp refresh flow.
- Any change to the operational log summary format itself.

## Open Questions

None. The brief specifies concrete test cases, exact counters/fields to assert, and a suggested mocking approach; no clarification is needed before implementation.

## Status: COMPLETE
