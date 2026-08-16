# Implementation: add-transport-box-state-rules

## What was implemented

Introduced `TransportBoxStateRules`, the single domain-level definition of transport-box
code occupancy (deny-list: only `Closed` and `Stocked` release a box's code; every other
state — present or future — occupies it). Added an XML doc comment to
`ITransportBoxRepository.IsBoxCodeActiveAsync` clarifying that "active" means "occupying the
code" (includes `Error` and `Quarantine`). No call sites were rewired — this task only
introduces the type and its tests, per scope. Added a drift-guard test suite that will fail
loudly if a future eleventh `TransportBoxState` member is added without a deliberate
classification decision.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` — new static class with a private `CodeReleasingStates` array (`{Closed, Stocked}`), public `OccupiesCode(TransportBoxState)` bool method, and public `OccupiesCodePredicate` EF-composable `Expression<Func<TransportBox, bool>>`. No dependency beyond `System.Linq.Expressions`.
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs` — added an XML doc comment above `IsBoxCodeActiveAsync`; signature is byte-identical to before.
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs` — new test file, namespace `Anela.Heblo.Tests.Domain.Logistics`.

## Tests

`TransportBoxStateRulesTests.cs` contains three tests, all asserting only through the public surface (`OccupiesCode` / `OccupiesCodePredicate`), never against the private `CodeReleasingStates`:

1. `EveryTransportBoxState_IsClassifiedByOccupiesCode` — exhaustive drift guard: hard-coded expected-occupancy map for all ten current `TransportBoxState` members, iterated via `Enum.GetValues<TransportBoxState>()` so a future eleventh member fails on a missing dictionary key rather than being silently skipped.
2. `ReleasingSet_IsExactlyClosedAndStocked` — asserts `OccupiesCode` is `false` only for `Closed`/`Stocked` and `true` for every other enum value, derived from `Enum.GetValues<TransportBoxState>()` rather than a separate hard-coded list.
3. `OccupiesCodePredicate_AgreesWithOccupiesCode_ForEveryState` — compiles `OccupiesCodePredicate` once, then for every enum value builds a `TransportBox` forced into that state via reflection (`PropertyInfo.GetSetMethod(nonPublic: true)`, since `State` has a private setter and some values like `InSwap` are unreachable through guarded transitions) and checks the compiled predicate agrees with `OccupiesCode`.

## How to verify

```bash
cd /home/user/worktrees/feature-3887-Arch-Review-Transportboxes-Box-Code-Uniqueness-Is
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxStateRulesTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
dotnet format Anela.Heblo.sln
git status --porcelain   # only intended files touched
```

Results obtained during implementation:
- Build: succeeded, exit code 0 (only pre-existing warnings in unrelated files, e.g. `GetFinancialOverviewHandlerTests.cs`, `RecalculatePurchasePriceHandlerTests.cs` — none from the new/modified files).
- `TransportBoxStateRulesTests`: 3/3 passed.
- Regression sweep (`TransportBox&Category!=Integration`): 208/208 passed.
- `dotnet format`: exit code 0, made no changes (no diff introduced).
- `grep -n "CodeReleasingStates" backend/src/.../TransportBoxStateRules.cs` shows it declared `private static readonly` and used only within that file; the test file references the name only inside assertion-message string literals, never as a member access.

Note: the solution file (`Anela.Heblo.sln`) lives at the repo root, not inside `backend/`, so
`dotnet build`/`dotnet format`/`dotnet test` were run against `Anela.Heblo.sln` from the repo
root (equivalent commands to those in the task context, adjusted for the actual solution
location).

## Notes

- No deviations from the task-context code sketches — `TransportBoxStateRules.cs`, the
  `ITransportBoxRepository` doc comment, and the test file were written to match the provided
  sketches exactly (test file additionally includes the required `using` directives and
  namespace declaration, which the sketch omitted for brevity).
- Only ran `dotnet build`/`format`/`test` from the repository root against `Anela.Heblo.sln`
  since no project/solution file exists directly under `backend/`; this is the same build the
  task-context commands were intended to exercise.
- Left `artifacts/feat-3887/state.json` (already modified in the working tree before this task
  started, unrelated to this task's file list) out of the commit.
- Call sites (`ChangeTransportBoxStateHandler`/`TransportBoxRepository`/`OpenOrResumeBoxByCodeHandler`) are intentionally untouched — rewiring them is explicitly out of scope for this task per the task-context Goal section.

## Status
DONE
