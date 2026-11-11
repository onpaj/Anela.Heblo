# Architecture Review: ForceRefreshTaskHandler tests

## Verdict
Approved. Test-only change, no architectural impact.

## Placement
- New file: `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/ForceRefreshTaskHandlerTests.cs`, namespace `Anela.Heblo.Tests.Application.BackgroundRefresh`, mirroring `RunHydrationTierHandlerTests` (same folder, same `MakeSut` + `VerifyLogged` helper pattern).
- Handler depends only on `IBackgroundRefreshTaskRegistry` (Xcc) and `ILogger<>`; both are mockable with Moq. No DI container or DB needed.

## Risks
- Log verification with Moq on `ILogger.Log` needs the `It.IsAnyType` form used in the sibling test; copy it rather than inventing a new one.
- Spec open question: NotFound response leaves `Success == true` (BaseResponse default). Tests must not lock this in; do not assert `Success` on the NotFound path. Flag as a possible follow-up issue; production code stays untouched (surgical-changes rule).
- Catch ordering: `InvalidOperationException` before `Exception` means subclasses (e.g. `ObjectDisposedException`) map to NotFound; covered by FR-4.

## Compliance
- DTO rule (classes, not records): unchanged.
- Process docs: not applicable (no behavior change).
- Validation: `dotnet build`, `dotnet format`, `dotnet test --filter ForceRefreshTaskHandlerTests`.

## Status: COMPLETE
