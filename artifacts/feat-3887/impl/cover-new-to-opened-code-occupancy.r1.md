# Implementation: cover-new-to-opened-code-occupancy

## What was implemented

Added test-only coverage pinning the end-to-end consequence of the shared
`TransportBoxStateRules.OccupiesCodePredicate` fix on the `New → Opened` path
(FR-3) — the path the admin UI drives. Two new `[Fact]` tests were added to
`TransportBoxUniquenessTests`, which wires the real `TransportBoxRepository`
over an in-memory `ApplicationDbContext` into the real
`ChangeTransportBoxStateHandler`. No production code was touched;
`HandleNewToOpened` already calls `IsBoxCodeActiveAsync` (which now consumes
the shared predicate from earlier tasks in this feature) and needed no change.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — added two `[Fact]` test methods:
  - `OpenTransportBox_WhenCodeHeldByQuarantinedBox_ShouldPreventDuplicate` — builds a box in `Quarantine` holding code `B001` (via `Open(...)` then `ToQuarantine(...)`), then attempts to open a fresh `New` box with the same code through the real handler and asserts the response is rejected with `TransportBoxDuplicateActiveBoxFound` and `Params["code"] == "B001"`.
  - `OpenTransportBox_WhenCodeHeldByErroredBox_ShouldPreventDuplicate` — same shape, using a box in `Error` state (via `Open(...)` then `Error(...)`).

  Both tests assert the handler response only, per the task's amendment A3 — they do not assert that the rejected `New` box's tracked `Code` is `null`, since `AssignBoxCodeIfAny` mutates the tracked entity before the guard runs and a correct implementation legitimately carries the rejected code on the shared `ApplicationDbContext`'s tracked instance. Step 2 (capturing the InMemory database name to re-read through a second `ApplicationDbContext`) was not needed since no persistence assertion was added.

## Tests

- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` now has 7 tests (5 pre-existing, unmodified, + 2 new). All 7 pass.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` (21 tests, mocked repository) verified unaffected — still passes unmodified, as expected since it mocks `ITransportBoxRepository` and the repository-level fix is invisible to it.

## How to verify

```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxUniquenessTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
```

Results obtained:
- Build: 0 errors (240 pre-existing warnings in unrelated files, unchanged by this task).
- `TransportBoxUniquenessTests`: 7/7 passed.
- `ChangeTransportBoxStateHandlerTests`: 21/21 passed.

`dotnet format Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` run from the repo root produced no output (no formatting issues).

## Notes

- No production files were modified — `ChangeTransportBoxStateHandler.cs` is untouched, matching the acceptance criteria.
- Confirmed `TransportBoxRepository.IsBoxCodeActiveAsync` already filters with `.Where(TransportBoxStateRules.OccupiesCodePredicate)` (landed by the prior `consume-rule-in-transport-box-repository` task), so the new tests exercise the fixed behavior end-to-end through the real handler and repository.
- `dotnet build`/`dotnet format` were run against the solution file at the repo root (`Anela.Heblo.sln`), not inside `backend/`, since no solution file exists directly under `backend/`.

## PR Summary
Adds two end-to-end tests pinning FR-3 of the TransportBox code-uniqueness fix: opening a `New` box with a code already held by a `Quarantine` or `Error` box is now correctly rejected as a duplicate via the real `ChangeTransportBoxStateHandler` + `TransportBoxRepository` stack. Test-only change; no production code modified. All 5 pre-existing tests in the file remain unmodified and passing, plus the 21 mocked-repository handler tests are unaffected.

### Changes
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — added `OpenTransportBox_WhenCodeHeldByQuarantinedBox_ShouldPreventDuplicate` and `OpenTransportBox_WhenCodeHeldByErroredBox_ShouldPreventDuplicate`

## Status
DONE
