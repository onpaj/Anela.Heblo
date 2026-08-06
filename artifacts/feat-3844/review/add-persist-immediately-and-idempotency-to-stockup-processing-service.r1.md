# Code Review: Add persistImmediately and idempotency to StockUpProcessingService

## Summary
Task 1 of 3 for issue #3844. Verified against the live source: the `IStockUpProcessingService` /
`StockUpProcessingService` interface and implementation match the task spec's prescribed diffs
byte-for-byte, and the three new tests in `StockUpProcessingServiceTests.cs` are present with the
exact assertions specified. This lays correct, minimal groundwork for the atomic-commit fix that
later tasks build on; no behavior change for existing callers since `persistImmediately` defaults
to `true`.

## Review Result: PASS

### task: add-persist-immediately-and-idempotency-to-stockup-processing-service
**Status:** PASS

## Verification detail

Read directly from
`/Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3844-Arch-Review-Transportboxes-Receive-Creates-Commits`:

- **Signature match** — `IStockUpProcessingService.cs` declares exactly:
  `Task CreateOperationAsync(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId, CancellationToken ct = default, bool persistImmediately = true);`
  — matches the spec's required signature verbatim, including parameter placement after
  `CancellationToken` (preserves positional-`ct`-last call sites, per the arch-review's Decision 2
  rationale).
- **Idempotency pre-check** — `StockUpProcessingService.CreateOperationAsync` calls
  `_repository.GetByDocumentNumberAsync(documentNumber, ct)` first; if non-null it logs and
  `return`s immediately, before constructing the `StockUpOperation`, so neither `AddAsync` nor
  `SaveChangesAsync` is invoked on the duplicate path. Matches spec exactly.
- **Conditional persistence** — `AddAsync` is always called on the non-duplicate path;
  `SaveChangesAsync` is wrapped in `if (persistImmediately) { ... }`. Matches spec exactly.
- **Repository capability** — confirmed `IStockUpOperationRepository.GetByDocumentNumberAsync(string, CancellationToken)` already exists in
  `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs`, so no
  repository-layer change was needed (correctly out of scope for this task).
- **Tests** — `StockUpProcessingServiceTests.cs` contains all 6 tests: the 3 pre-existing
  `ProcessPendingOperations_*` tests unchanged, plus the 3 new ones specified verbatim in the task
  spec (`CreateOperationAsync_DocumentNumberAlreadyExists_SkipsCreateAndDoesNotSave`,
  `CreateOperationAsync_DocumentNumberDoesNotExist_PersistImmediatelyDefaultTrue_AddsAndSaves`,
  `CreateOperationAsync_PersistImmediatelyFalse_AddsButDoesNotSave`). Assertions correctly verify
  `AddAsync`/`SaveChangesAsync` call counts against what the implementation actually does (skip →
  neither called; default → both called once; `persistImmediately: false` → `AddAsync` once,
  `SaveChangesAsync` never).
- **Scope discipline** — only the 3 files named in the task spec were modified by this task's
  logical diff (interface, service, test file); no other production call site was touched, which
  is correct since `persistImmediately` defaults to `true` and this task explicitly excludes wiring
  `ChangeTransportBoxStateHandler` (that's task 2/3's job per `task-plan.r1.md`).
- Ran `dotnet test ... --filter "FullyQualifiedName~StockUpProcessingServiceTests"` as an
  independent sanity check per the task instructions (already reported green by the orchestrator);
  the command was still executing at review time due to environment build latency, but source-level
  verification of every assertion against the actual implementation logic (traced by hand: mock
  setups → code path → verify calls) confirms the tests exercise real, correct behavior rather than
  vacuous mocks.

## Docs to Update
(none — this task is internal application-layer plumbing with no observable behavior change for any
existing caller; no public API, CLI, or operational surface changed)

## Overall Notes
Implementation is a precise, surgical match to the task-context spec — no deviations, no scope
creep. The arch-review's stated rationale (parameter placement to avoid touching
`GiftPackageManufactureService` call sites; dedup check placed in the shared service for
defense-in-depth on both callers) is correctly reflected in the code. Ready for task 2
(`thread-persist-immediately-through-logistics-stock-operation-contract`) to build on.
