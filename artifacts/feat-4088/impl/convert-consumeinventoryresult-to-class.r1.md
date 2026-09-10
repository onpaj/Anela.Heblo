# Implementation: convert-consumeinventoryresult-to-class

## What was implemented

Converted `ConsumeInventoryResult` from a `sealed record` with a positional
constructor to a `sealed class` with an init-only `Outcome` property and
three static factory methods (`Success()`, `InventoryNotFound()`,
`InsufficientStock()`), per the project's CLAUDE.md rule that DTO/contract
types must be classes, never C# records (the OpenAPI client generator
mishandles record parameter order). Updated the one production call site
(`ManufactureInventoryReservationAdapter.TryConsumeAsync`) that constructed
this type positionally, replacing all three `new ConsumeInventoryResult(...)`
calls with the corresponding factory method calls. No other logic in the
adapter (item-not-found check, try/catch branching, success path) was
touched.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs` — `ConsumeInventoryResult` changed from `sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome)` to a `sealed class` with `Outcome { get; init; }` and static factories `Success()`, `InventoryNotFound()`, `InsufficientStock()`. The `ConsumeInventoryOutcome` enum above it is unchanged.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` — the three `new ConsumeInventoryResult(ConsumeInventoryOutcome.X)` construction expressions in `TryConsumeAsync` replaced with `ConsumeInventoryResult.X()`.

## Tests

No new tests were required or added — this task is a mechanical type
conversion plus updating its call sites in `Anela.Heblo.Application`; the
task context explicitly scopes converting the test project's usages to a
separate task (task 2), not this one.

## How to verify

```bash
cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
grep -rn "new ConsumeInventoryResult(" backend/src/
```

- Build succeeds: `Build succeeded.` with `0 Error(s)` (138 pre-existing,
  unrelated warnings remain — none introduced by this change).
- The grep returns no matches — no remaining positional-constructor calls
  to `ConsumeInventoryResult` anywhere under `backend/src/`.
- `git diff` against the pre-change files matches exactly the before/after
  snippets specified in the task context, confirming no unintended edits.

## Notes

`dotnet format` was run scoped to the two touched files
(`--include ConsumeInventoryResult.cs ManufactureInventoryReservationAdapter.cs`)
and produced no output — no reformatting was needed; the code already
matched the project's formatting conventions. The two files were staged
and committed on the current branch in a standalone commit
(`aec4684`, "Convert ConsumeInventoryResult from record to class per
CLAUDE.md DTO rule") containing exactly those two files, per the task
context's step 7. No deviations from the task context.

## PR Summary
Converted the Logistics-owned `ConsumeInventoryResult` contract type from a `sealed record` to a `sealed class` with static factory methods, per the project's CLAUDE.md rule that DTO/contract types must be classes (OpenAPI client generators mishandle record parameter order). Updated the sole production call site in `ManufactureInventoryReservationAdapter.TryConsumeAsync` to use the new factories instead of the positional constructor.

The change is purely mechanical: the enum and all branching logic are untouched, only the type declaration and the three construction expressions changed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs` — record → sealed class with `Outcome { get; init; }` and `Success()`/`InventoryNotFound()`/`InsufficientStock()` static factories
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` — three `new ConsumeInventoryResult(...)` calls replaced with the corresponding factory method calls

## Status
DONE
