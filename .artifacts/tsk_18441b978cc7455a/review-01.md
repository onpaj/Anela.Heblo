# Review — Atomic, idempotent Receive for TransportBoxes

## Verdict: done

## What was checked

Read plan-01.md, design-01.md, architecture-01.md, and development-01.md, then independently
verified the actual diff (`git show HEAD`) against all four, and re-ran verification myself rather
than trusting the development step's report:

- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (251 pre-existing warnings, none in changed
  files beyond one pre-existing nullability warning in a test helper's call site unrelated to this
  change).
- `dotnet test` filtered to `ChangeTransportBoxState|StockUpProcessingServiceTests|
  LogisticsStockOperationAdapterTests|TransportBoxUniquenessTests|GiftPackageManufactureServiceTests`
  — **52/52 passed**, including both new real-Postgres integration tests
  (`Receive_SaveChangesFails_RollsBackBoxAndStockUpOperationsTogether` and
  `Receive_Retried_ExistingStockUpOperationIsSkippedAndMissingOnesAreCreated`), run against an actual
  Testcontainers Postgres instance, not mocked.
- `dotnet format --verify-no-changes` on all 9 changed files — clean, no diff.

## Conformance

- **Root cause addressed correctly.** `ChangeTransportBoxStateHandler.HandleReceived`
  (`ChangeTransportBoxStateHandler.cs:246`) now calls the new `StageOperationAsync` instead of
  `CreateOperationAsync`. `StageOperationAsync` (`StockUpProcessingService.cs:44-68`) does
  `GetByDocumentNumberAsync` dedup + `AddAsync` only — no `SaveChangesAsync`. The box's own
  `SaveChangesAsync` at `ChangeTransportBoxStateHandler.cs:135` is still the only commit point in the
  Received path, and because `HandleReceived` runs earlier in the same `Handle` method
  (`:108-114`, before `:126`/`:135`) against the same scoped `ApplicationDbContext`, both the staged
  `StockUpOperation` rows and the box's state transition land in one `SaveChangesAsync` call, hence
  one implicit EF transaction. This collapses the two-transaction bug into one, exactly as
  design-01.md specified.
- **Idempotency**: `DocumentNumber`-based dedup means a retry after a rolled-back or partially-failed
  attempt is a no-op for already-present rows rather than hitting the unique-index violation the
  finding called out. Verified end-to-end by the real-Postgres
  `Receive_Retried_ExistingStockUpOperationIsSkippedAndMissingOnesAreCreated` test (pre-existing row
  for one product, handler run once, exactly one row per product, pre-existing row's `Id` unchanged).
- **Atomicity on failure**: verified end-to-end by
  `Receive_SaveChangesFails_RollsBackBoxAndStockUpOperationsTogether` — forces the single
  `SaveChangesAsync` to throw via an interceptor, asserts box stays `InTransit` **and** zero
  `StockUpOperation` rows exist. This is the concrete scenario the finding described (inventory
  committed, box stuck) and it's now closed.
- **Architecture-01.md's required test-design correction was applied**: the failure-injection
  exception is a plain `InvalidOperationException` (not transient-classified) and the test's
  `DbContextOptions` is built the bare way with no `.ExecutionStrategy(...)` configured — matching
  the existing `GetStockUpOperationsSummaryIntegrationTests` pattern, so `PollyExecutionStrategy`'s
  retry layer cannot mask the test. Confirmed by reading the test file directly
  (`ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs:112-121,173-199`).
- **Scope discipline**: `CreateOperationAsync` is untouched and still used by
  `GiftPackageManufactureService` (confirmed via `dotnet test ... GiftPackageManufactureServiceTests`
  passing unmodified) and by the four other transitions that don't touch
  `_stockOperationService` at all — matches the plan/design's explicit scope boundary. A regression
  guard (`Handle_QuarantineToReceived_NeverCallsNonIdempotentCreateOperationAsync`) was added to catch
  any future accidental revert to the non-idempotent method.
- **No unrelated changes**: diff is limited to the two service interfaces/implementations, the
  adapter, the handler's two call sites (method name + log text), and their tests. No schema change,
  no new abstraction, no `IUnitOfWork`/explicit transaction introduced — consistent with the
  approved design's "narrowest fix" rationale (relying on EF's implicit per-`SaveChanges` transaction
  since both repositories share one scoped `DbContext`).

## Minor observations (non-blocking)

- `StageOperationAsync`'s dedup check (`GetByDocumentNumberAsync`) queries via EF's normal query path,
  which won't see other not-yet-saved `Add`ed entities in the same change tracker. This is a non-issue
  here since each product code within one box produces one document number and there's no loop that
  could stage the same `DocumentNumber` twice within a single request — confirmed by reading the
  aggregation logic (`ChangeTransportBoxStateHandler.cs:232-238`, grouped by `ProductCode`).
- `GiftPackageManufactureService`'s structurally similar (N-way, arguably worse) non-atomicity issue
  remains open, as correctly flagged as an explicit non-goal by design-01.md/architecture-01.md — not
  a gap in this task's scope.

No functional requirement, architectural constraint, or explicitly-required test is missing, and no
correctness bug was found in the implementation or its tests.
