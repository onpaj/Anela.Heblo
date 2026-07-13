# Specification: Remove dead code — `GetJournalIndicatorsAsync` and `JournalIndicatorDto`

## Summary
An architecture review found that `IJournalRepository.GetJournalIndicatorsAsync` — a 49-line aggregation method, its interface declaration, and the associated `JournalIndicatorDto` — have zero production callers. This is a pure removal task: delete the unused interface member, its implementation, its supporting domain type, its unused DTO, and the integration tests that exist solely to exercise it. No behavior visible to any consumer changes.

## Background
The Journal module's repository interface (`IJournalRepository`) declares `GetJournalIndicatorsAsync`, intended to return per-product journal aggregation data (`JournalIndicatorSnapshot`: direct entry count, last entry date, "has recent entries" flag). A repo-wide grep confirms no MediatR handler, controller, or application service calls this method — the only references outside its own definition/implementation are its integration tests and a single XML doc-comment cross-reference. The `JournalIndicatorDto` in `Application/Features/Journal/Contracts/` was evidently scaffolded to eventually expose this data over the API but was never wired to any handler, and never generated into a real OpenAPI response.

Carrying this code has a real, ongoing cost even though it does nothing:
- Every test double / mock of `IJournalRepository` must implement or stub the method.
- Future maintainers must read and reason about non-trivial join/group-by aggregation logic (`JournalRepository.cs:154-202`) that no code path ever exercises.
- `JournalIndicatorDto` occupies OpenAPI contract surface for a response that is never produced.
- Four integration tests (`JournalRepositoryIntegrationTests.cs`) exist purely to validate a method nothing calls, adding to CI time and maintenance burden for zero product value.

This is a YAGNI cleanup: if per-product journal indicators become a real product requirement, the aggregation logic can be re-implemented (or restored from git history) once there is a concrete consumer (e.g., a handler or UI feature that needs it).

## Functional Requirements

### FR-1: Remove `GetJournalIndicatorsAsync` from the repository contract
Delete the method signature from `IJournalRepository`.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` no longer declares `GetJournalIndicatorsAsync` (currently lines 31-34).
- No other member of `IJournalRepository` is modified.

### FR-2: Remove the implementation from `JournalRepository`
Delete the concrete implementation of `GetJournalIndicatorsAsync` in the EF Core repository.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` no longer contains the `GetJournalIndicatorsAsync` method body (currently lines 154-202).
- The `RecentEntriesDays` constant (`JournalRepository.cs:12`) is removed as well, since it is used exclusively by the deleted method and would otherwise become unused dead code itself.
- All other methods on `JournalRepository` (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`, `ApplySort`, etc.) are unchanged.
- The file still compiles and satisfies the (now-reduced) `IJournalRepository` interface.

### FR-3: Remove the now-orphaned `JournalIndicatorSnapshot` domain type
`JournalIndicatorSnapshot` (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`) exists solely as the return-value shape for `GetJournalIndicatorsAsync`. Once FR-1/FR-2 land, it has no remaining reference anywhere in `backend/src/`. Per the same YAGNI rationale as the rest of this cleanup, delete it rather than leave an orphaned type behind.

**Acceptance criteria:**
- `JournalIndicatorSnapshot.cs` is deleted.
- A repo-wide search for `JournalIndicatorSnapshot` returns no remaining references in `backend/src/` or `backend/test/`.

### FR-4: Remove the unused `JournalIndicatorDto`
Delete the DTO that was scaffolded to wrap this data for API exposure but was never referenced by any controller or handler.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` is deleted.
- A repo-wide search for `JournalIndicatorDto` returns no remaining references in `backend/src/`.
- No change to generated OpenAPI/TypeScript client is required as a follow-up beyond the normal build-time regeneration, since this DTO was never part of any endpoint's request/response contract (confirmed: zero controller/handler references).

### FR-5: Remove integration tests that exist solely to exercise the deleted method
`backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` contains four `[Fact]` test methods whose only purpose is to validate `GetJournalIndicatorsAsync`. These will fail to compile once FR-1/FR-2 land and have no value once the method they test is gone.

**Acceptance criteria:**
- The following test methods are deleted from `JournalRepositoryIntegrationTests.cs`:
  - `GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount` (~line 197)
  - `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator` (~line 252)
  - `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries` (~line 268)
  - `GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount` (~line 769)
- No other test in the file (e.g., `GetEntriesByProductAsync` tests, sort-matrix tests) is altered.
- The remaining tests in the file still compile and pass unchanged.

### FR-6: Verify no residual references
After the above deletions, confirm the codebase has no dangling references to the removed symbols.

**Acceptance criteria:**
- `grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/` returns zero matches.
- `dotnet build` succeeds with no errors or new warnings introduced by this change.
- The full Journal test suite (unit + integration) passes.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a deletion of unreferenced code with no runtime execution path; no performance impact of any kind.

### NFR-2: Security
Not applicable — no auth, data-sensitivity, or attack-surface change. If anything, removing an unused DTO marginally reduces OpenAPI surface area exposed to introspection, which is a (negligible) net positive.

## Data Model
No data model changes. `JournalIndicatorSnapshot` was a transient, in-memory read-model projection (a `readonly record struct`) — not a persisted entity, not mapped by EF Core, and has no database table, migration, or schema impact. Its removal has zero effect on the database.

## API / Interface Design
No API changes. `JournalIndicatorDto` was never referenced by any MediatR handler or MVC controller, so no endpoint, request, or response contract is affected. No OpenAPI spec entries reference this DTO today, and none will need to be removed from the generated spec beyond what falls out naturally from deleting the C# type and rebuilding.

## Dependencies
None. This change is self-contained within `backend/src/Anela.Heblo.Domain/Features/Journal/`, `backend/src/Anela.Heblo.Persistence/Journal/`, `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/`, and `backend/test/Anela.Heblo.Tests/Features/Journal/`. It requires no coordination with the frontend, no database migration, and no changes to CI/CD or infrastructure.

## Out of Scope
- Re-implementing journal indicator aggregation for a real consumer. If a future feature needs per-product journal counts/recency, it should be designed and built fresh against an actual UI/API requirement, not restored from this deleted code as-is.
- Any other `IJournalRepository` methods, DTOs, or tests not named above — this task touches only the confirmed-dead surface identified by the architecture review.
- Regenerating the frontend TypeScript API client — not required since `JournalIndicatorDto` was never part of a generated endpoint contract, but the standard build-time client regeneration will naturally reflect its absence if it had been (defensive step, not expected to produce any diff).

## Open Questions
None.

## Status: COMPLETE
