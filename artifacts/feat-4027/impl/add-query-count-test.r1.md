# Implementation: add-query-count-test

## What was implemented
Added a new xUnit regression test, `GetConsumptionHistoryQueryCountTests`, that wraps a real
`PackingMaterialRepository` in a `CountingRepositoryWrapper` (in-memory EF Core provider) and
asserts that `GetConsumptionHistoryHandler.Handle` never calls `IPackingMaterialRepository
.GetAllAsync` and calls `GetMaterialNamesByIdsAsync` exactly once, with the returned id set
being a subset of (and no larger than) the page's distinct `PackingMaterialId`s. This is the
documented "red" checkpoint for a later task that swaps the handler from `GetAllAsync` to the
targeted `GetMaterialNamesByIdsAsync` lookup. No production code was touched, per the task's
explicit instruction.

Before writing the file, the proposed test content from the task-context file was verified
against the current codebase:
- `GetConsumptionHistoryHandler.cs` — confirmed it still calls `_repository.GetAllAsync(...)`
  at line 48 (the handler swap has **not** yet landed), so the test is expected to fail red,
  as the task anticipated.
- `IPackingMaterialRepository.GetMaterialNamesByIdsAsync(IEnumerable<int>, CancellationToken)`
  — signature matches exactly what the proposed test's wrapper implements/delegates to (this
  method was already added by the earlier `add-repository-method` task).
- `PackingMaterialRepository` — constructor `PackingMaterialRepository(ApplicationDbContext)`
  matches, and it implements the full `IPackingMaterialRepository` interface referenced by the
  wrapper's delegation methods.
- `PackingMaterialsListQueryCountTests.cs` — confirmed as the pattern this new test follows
  (same `CountingRepositoryWrapper`-around-real-repository technique, same in-memory-DB
  rationale comment, same file location/namespace).
- `PackingMaterial` / `PackingMaterialConsumption` constructors, `MaterialConsumptionHistoryFilter`,
  `GetConsumptionHistoryRequest`, `GetConsumptionHistoryResponse`, and
  `MaterialConsumptionHistoryItemDto.PackingMaterialId` — all matched the proposed test code
  exactly.

No adaptation was needed — the task-context's proposed test file compiled and ran as-is against
the current codebase.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs` — new test file, added verbatim from the task-context file. Contains one `[Fact]`
  (`Handle_NeverCallsGetAllAsync_AndCallsGetMaterialNamesByIdsAsyncExactlyOnceWithPageScopedIds`)
  and a private `CountingRepositoryWrapper : IPackingMaterialRepository` that counts calls to
  `GetAllAsync` / `GetMaterialNamesByIdsAsync` and captures the last ids passed to
  `GetMaterialNamesByIdsAsync`, delegating everything else to a real `PackingMaterialRepository`
  backed by EF Core's in-memory provider.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs`
  — proves `GetConsumptionHistoryHandler.Handle` (a) never calls `GetAllAsync` and (b) calls
  `GetMaterialNamesByIdsAsync` exactly once with an id set that is a subset of, and no larger
  than, the current page's distinct material ids.

Actual `dotnet test` output (filtered to this test class):

```
[xUnit.net 00:00:05.13]     Anela.Heblo.Tests.Features.PackingMaterials.GetConsumptionHistoryQueryCountTests.Handle_NeverCallsGetAllAsync_AndCallsGetMaterialNamesByIdsAsyncExactlyOnceWithPageScopedIds [FAIL]
  Failed Anela.Heblo.Tests.Features.PackingMaterials.GetConsumptionHistoryQueryCountTests.Handle_NeverCallsGetAllAsync_AndCallsGetMaterialNamesByIdsAsyncExactlyOnceWithPageScopedIds [2 s]
  Error Message:
   Expected countingRepository.GetAllAsyncCallCount to be 0 because GetConsumptionHistoryHandler must never load the full packing-materials table, but found 1 (difference of 1).
  ...
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: < 1 ms - Anela.Heblo.Tests.dll (net8.0)
```

This matches the task's expected outcome exactly: 1 failed, failure on the
`GetAllAsyncCallCount.Should().Be(0, ...)` assertion, because
`GetConsumptionHistoryHandler.Handle` still calls `GetAllAsync` (not yet swapped to
`GetMaterialNamesByIdsAsync`).

## How to verify
```bash
cd /home/user/worktrees/feature-4027-Arch-Review-Packingmaterials-Getconsumptionhistory
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryQueryCountTests"
```
Expect `Failed: 1` with the `GetAllAsyncCallCount` assertion as the failure reason, until the
handler-swap task (next task) lands and changes `GetConsumptionHistoryHandler` to call
`GetMaterialNamesByIdsAsync` instead of `GetAllAsync`, at which point this test should turn
green.

## Notes
No deviations from the task-context's proposed test file were required — every type, namespace,
and method signature it referenced (including `IPackingMaterialRepository
.GetMaterialNamesByIdsAsync`, landed by the earlier `add-repository-method` task) matched the
current codebase exactly, so the file was written verbatim. Only the one new test file was
created; `GetConsumptionHistoryHandler.cs` and all other production code were left untouched, as
instructed. The test fails for exactly the expected reason (red `GetAllAsyncCallCount`
assertion), confirming the handler swap has not yet been done.

An unrelated file, `artifacts/feat-4027/state.json`, appears modified in the working tree
(pre-existing pipeline state, not touched by this task) — it was left unstaged/uncommitted per
the instruction to only commit the new test file.

## Status
DONE
