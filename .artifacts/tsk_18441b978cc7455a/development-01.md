# Development — Atomic, idempotent Receive for TransportBoxes

Implements design-01.md exactly, including the architecture-01.md test-design correction (non-transient
failure-injection exception, bare `DbContextOptions` with no execution strategy configured).

## Summary of the fix

`ChangeTransportBoxStateHandler.HandleReceived` used to call `CreateOperationAsync`, which did
`AddAsync` + an immediate `SaveChangesAsync` per product — committing `StockUpOperation` rows in their
own transaction *before* the box's own state transition was saved in a second, later
`SaveChangesAsync`. A failure between the two left inventory stocked up but the box permanently wedged
in `InTransit`/`Reserve`/`Quarantine`, and retrying threw an unhandled unique-constraint violation on
`DocumentNumber`.

The fix adds a new `StageOperationAsync` method that idempotently stages a `StockUpOperation`
(`AddAsync` only, no `SaveChangesAsync`) after checking `GetByDocumentNumberAsync` for an existing row.
`HandleReceived` now calls `StageOperationAsync` instead of `CreateOperationAsync`. Because
`ITransportBoxRepository` and `IStockUpOperationRepository` share the same scoped `ApplicationDbContext`,
the staged operations sit in the change tracker until the handler's existing final
`_repository.SaveChangesAsync(cancellationToken)` runs — at which point EF Core's implicit
per-`SaveChanges` transaction commits the box and all its `StockUpOperation` rows together, or rolls
both back together on any failure. No explicit `BeginTransactionAsync`/`IUnitOfWork` was introduced, so
the app's custom `PollyExecutionStrategy` is unaffected (only *user-initiated* transactions need
execution-strategy wrapping).

`CreateOperationAsync` itself is untouched and still used by `GiftPackageManufactureService` (out of
scope per the plan/design — flagged there as a candidate follow-up finding, not fixed here).

## Files changed

**Production code**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs` — added
  `StageOperationAsync` to the interface.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs` —
  implemented `StageOperationAsync`: `GetByDocumentNumberAsync` pre-check (log + no-op if found), else
  `AddAsync` with no `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs`
  — added `StageOperationAsync` to the interface.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs`
  — added the `StageOperationAsync` pass-through (same enum-mapping shape as the existing
  `CreateOperationAsync`).
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
  — `HandleReceived` now calls `StageOperationAsync` instead of `CreateOperationAsync`; log messages
  updated from "Created"/"Successfully created" to "Staged" to reflect the new no-save semantics.

**Tests**
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` —
  all `CreateOperationAsync` setups/verifies for the Received path switched to `StageOperationAsync`;
  added `Handle_QuarantineToReceived_NeverCallsNonIdempotentCreateOperationAsync` guarding against
  silently reverting to the old non-idempotent method.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs`
  — added `StageOperationAsync_*` tests mirroring the existing `CreateOperationAsync_*` coverage
  (enum mapping, parameter pass-through, unknown-source exception).
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs` — added
  `StageOperationAsync_NoExistingDocument_AddsWithoutSaving` and
  `StageOperationAsync_ExistingDocument_SkipsWithoutAddingOrThrowing`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`
  (new) — real-Postgres integration tests (Testcontainers, same `PostgresSharedContainerFixture` pattern
  as `GetStockUpOperationsSummaryIntegrationTests`), proving the property that can't be proven with
  mocks:
  - `Receive_SaveChangesFails_RollsBackBoxAndStockUpOperationsTogether` — an EF `SaveChangesInterceptor`
    throws a plain `InvalidOperationException` (a type `TransientErrorClassifier` never retries, and the
    context is built the bare way with no `.ExecutionStrategy(...)` configured — the architecture
    review's required correction, so the test fails for the right reason regardless of retry-layer
    details) on the handler's single `SaveChangesAsync` call. Asserts the box stays `InTransit` and
    **zero** `StockUpOperation` rows exist — proving staged-but-uncommitted operations roll back with
    the box, not the old bug's failure mode where operations were already durable.
  - `Receive_Retried_ExistingStockUpOperationIsSkippedAndMissingOnesAreCreated` — pre-seeds one
    `StockUpOperation` row (simulating a legacy wedge / post-rollback retry), then runs Receive for a
    three-product box. Asserts success, box `Received`, exactly one row per product, and the
    pre-existing row is untouched (same `Id`) rather than duplicated or throwing on the unique index.

## Verification performed

- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (pre-existing warnings only; the
  `AccessMatrixGen` MSB3073 warning is pre-existing/unrelated to this change and does not fail the
  build).
- `dotnet format` on all changed files — no changes needed.
- `dotnet test` filtered to `ChangeTransportBoxStateHandlerTests` — 18/18 passed.
- `dotnet test` filtered to `LogisticsStockOperationAdapterTests|StockUpProcessingServiceTests|GiftPackageManufactureServiceTests|TransportBoxUniquenessTests`
  — 27/27 passed (confirms `GiftPackageManufactureService`'s unmodified `CreateOperationAsync` usage and
  the existing InMemory-DB uniqueness/handler tests are unaffected).
- `dotnet test` filtered to `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` (real Postgres via
  Testcontainers/podman) — 2/2 passed.

## How to verify

From `backend/`:
```bash
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxState|FullyQualifiedName~StockUpProcessingServiceTests|FullyQualifiedName~LogisticsStockOperationAdapterTests|FullyQualifiedName~TransportBoxUniquenessTests|FullyQualifiedName~GiftPackageManufactureServiceTests"
```
The Postgres integration tests in `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` require a
working Docker/Podman socket (Testcontainers spins up `postgres:16`); they're tagged
`[Trait("Category", "Integration")]` and run in the same `PostgresIntegration` xunit collection as the
existing `GetStockUpOperationsSummaryIntegrationTests`.

## Scope confirmation

Matches design-01.md §4: `GiftPackageManufactureService` (still calls unmodified `CreateOperationAsync`),
`TransportBoxCompletionService`, and the other five state transitions are untouched — none of them call
`_stockOperationService`, so they're structurally unaffected. No schema changes, no new
request/response contract, no data migration for pre-existing wedged boxes (out of scope per plan/design).
