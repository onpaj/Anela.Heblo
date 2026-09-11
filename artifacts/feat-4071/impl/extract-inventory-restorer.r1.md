# Implementation: extract-inventory-restorer

## What was implemented
Extracted the body of `ChangeTransportBoxStateHandler.RestoreInventoryForItemsAsync` (formerly
lines 307–328) unchanged into a new standalone collaborator, `TransportBoxInventoryRestorer`,
behind a new `ITransportBoxInventoryRestorer` interface. Per arch-review Decision 3, this is
NOT an `ITransportBoxTransitionSideEffect` — it isn't dispatched by `(from, to)`; it runs
unconditionally on the Opened→New rollback path, alongside the transition itself.

As instructed, `ChangeTransportBoxStateHandler.cs` was left untouched — it still contains its
own private `RestoreInventoryForItemsAsync` method, and the handler does not yet call the new
class. Wiring is deferred to the `refactor-handler-orchestration` task. No DI registration was
added either, consistent with the other extracted side-effect classes (`NewToOpenedSideEffect`,
`OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`, `ReceivedSideEffect`), none of which are
registered in DI yet.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxInventoryRestorer.cs` — new interface, `RestoreAsync(IReadOnlyList<TransportBoxItem> items, string userName, DateTime timestamp, int boxId, string? boxCode, CancellationToken cancellationToken)`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/TransportBoxInventoryRestorer.cs` — new class implementing the interface; iterates items, skips any with `SourceInventoryId == null`, and calls `IInventoryReservationService.RestoreAsync` for the rest — logic copied verbatim from the handler's private method.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxInventoryRestorerTests.cs` — new unit test file (2 tests).

## Tests
`TransportBoxInventoryRestorerTests.cs`:
- `RestoreAsync_ItemWithSourceInventoryId_CallsRestore` — an item with `SourceInventoryId = 42` triggers exactly one `IInventoryReservationService.RestoreAsync` call with the expected parameters (inventoryId, amount as decimal, userName, timestamp, boxId, boxCode).
- `RestoreAsync_ItemWithoutSourceInventoryId_SkipsRestore` — an item with no `SourceInventoryId` results in `RestoreAsync` never being called.

Both pass: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxInventoryRestorerTests"
```
Also confirmed the affected test project builds cleanly (`dotnet test test/Anela.Heblo.Tests --filter ...` performs a full build of the referenced projects with no new errors/warnings beyond pre-existing ones).

## Notes
- Deviation from the illustrative task-context test: `TransportBoxItem.SourceInventoryId` has a
  private setter and is only assignable via the constructor (`sourceInventoryId` is the 8th,
  optional constructor parameter). The sample test's object-initializer syntax
  (`new TransportBoxItem(...) { SourceInventoryId = 42 }`) would not compile against the real
  type, so the test was adjusted to pass `42` as the constructor's `sourceInventoryId` argument
  instead: `new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null, null, 42)`.
  No production type was changed to accommodate the test, per the task's explicit guidance.
- `IInventoryReservationService.RestoreAsync` signature matched the illustrative code exactly
  (`inventoryId, amount, userName, timestamp, boxId, boxCode, cancellationToken`), so no other
  adaptation was needed there.
- Verified step 2 (failing build) for real: moved the two new implementation files aside,
  reran the filtered test, confirmed `error CS0246: The type or namespace name
  'TransportBoxInventoryRestorer' could not be found`, then restored the files and reran to
  confirm both tests pass.
- Did not run `dotnet format` across the repo, and did not modify `ChangeTransportBoxStateHandler.cs`,
  per task instructions.
- `artifacts/feat-4071/state.json` had a pre-existing uncommitted modification in the worktree
  (not made by this task); it was left out of the commit so only the three new files were staged.

## PR Summary
Extracts the Opened→New inventory-restoration logic that currently lives as a private method
(`RestoreInventoryForItemsAsync`) inside `ChangeTransportBoxStateHandler` into its own tested
collaborator, `TransportBoxInventoryRestorer`, implementing a new `ITransportBoxInventoryRestorer`
interface. This mirrors the pattern already established by the previously extracted transition
side effects (`NewToOpenedSideEffect`, `OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`,
`ReceivedSideEffect`) but is intentionally NOT modeled as an `ITransportBoxTransitionSideEffect`,
since it isn't dispatched by a `(from, to)` pair — it runs unconditionally whenever the handler
detects an Opened→New rollback with items carrying a `SourceInventoryId`.

The handler itself is untouched in this change; it is not yet wired to call the new class. That
wiring is scoped to a later task (`refactor-handler-orchestration`), so this change is purely
additive and carries no behavior change or regression risk.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxInventoryRestorer.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/TransportBoxInventoryRestorer.cs` (new)
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxInventoryRestorerTests.cs` (new)

## Status
DONE
