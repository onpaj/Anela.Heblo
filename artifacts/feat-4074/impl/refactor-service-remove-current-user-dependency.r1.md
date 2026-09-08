# Implementation: refactor-service-remove-current-user-dependency

## What was implemented

Removed the `ICurrentUserService` dependency from `GiftPackageManufactureService` per
ADR-005 (identity resolution belongs in the MediatR handler, not the Application
service). `CreateManufactureAsync` and `DisassembleGiftPackageAsync` now accept a
plain `string userName` parameter (inserted before the trailing
`CancellationToken cancellationToken = default`) instead of resolving the current
user internally via `_currentUserService.GetCurrentUser().Name ?? "System"`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` — added `string userName` parameter to `CreateManufactureAsync` and `DisassembleGiftPackageAsync` signatures.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — removed `using Anela.Heblo.Domain.Features.Users;`, removed the `_currentUserService` field and constructor parameter, added `userName` parameter to both methods, replaced `_currentUserService.GetCurrentUser().Name ?? "System"` with the passed-in `userName` in both `GiftPackageManufactureLog` constructions.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — removed `using Anela.Heblo.Domain.Features.Users;`, removed `_currentUserServiceMock` field/instantiation/constructor-wiring, removed the `_currentUserServiceMock.Setup(...)` call in `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems`, and updated the `CreateManufactureAsync` call under test to pass `userId` directly as the new `userName` argument.

## Tests

`backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` covers `GiftPackageManufactureService` directly, including `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems`, which now exercises the new `userName` parameter and asserts `CreatedBy` is set from it (assertions unchanged, only the mock wiring changed). No new test file was needed — this task only changes signatures the existing test already covers.

## How to verify

This task is intentionally scoped to only the service/interface/test (the two calling
handlers are updated in separate tasks `inject-current-user-into-create-handler` and
`inject-current-user-into-disassemble-handler`, per
`artifacts/feat-4074/task-context/`). As a result, the solution does not build
end-to-end yet, and the test project (which references the Application project)
cannot execute yet either. This matches the task-context spec's Step 5 exactly,
which explicitly documents this as the expected intermediate state.

Verification performed:
- `dotnet build Anela.Heblo.sln` (from repo root, where the `.sln` lives) fails with
  exactly two errors, both `CS1503: Argument N: cannot convert from
  'System.Threading.CancellationToken' to 'string'` — one in
  `CreateGiftPackageManufactureHandler.cs:21` and one in
  `DisassembleGiftPackageHandler.cs:23`. No other errors. This is exactly the
  outcome the task-context spec (Step 5) predicts and requires.
- Grepped the whole `GiftPackageManufacture` feature folder and the test file for
  any remaining `_currentUserService` / `ICurrentUserService` / `CurrentUser(`
  references — none found; the dependency was fully removed from its intended
  scope.
- `dotnet test .../Anela.Heblo.Tests.csproj --filter
  "FullyQualifiedName~GiftPackageManufactureServiceTests"` currently cannot run
  (transitively blocked by the same two expected handler build errors, since the
  test project references the Application project). It will pass once the next
  two tasks update the handlers' call sites — no further change to the test file
  is anticipated at that point.

## Notes

- Followed the task-context file's code snippets verbatim; no deviations.
- The two handler call sites (`CreateGiftPackageManufactureHandler`,
  `DisassembleGiftPackageHandler`) are explicitly out of scope for this task per
  the task-context spec and are left with their now-stale 3-arg/4-arg calls,
  which is the expected, documented intermediate state — they are covered by the
  next two queued tasks.

## PR Summary
Removed `ICurrentUserService` from `GiftPackageManufactureService` (Application
layer) per ADR-005, which requires identity resolution to happen only inside
MediatR handlers. `CreateManufactureAsync` and `DisassembleGiftPackageAsync` now
take a `string userName` parameter supplied by the caller instead of resolving the
current user internally.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` — added `userName` parameter to both service methods
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — removed `ICurrentUserService` dependency, added `userName` parameter, uses it directly in `GiftPackageManufactureLog` construction
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — updated to the post-refactor constructor/method shape

## Status
DONE
