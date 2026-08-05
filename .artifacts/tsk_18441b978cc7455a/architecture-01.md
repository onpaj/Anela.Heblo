# Architecture review — Atomic, idempotent Receive for TransportBoxes

## Verdict

**Approved, with one required fix to the testing design (§4) before implementation starts.** The
core mechanism — collapse the two `SaveChangesAsync` calls into one by making stock-up staging a
no-save operation on the shared `ApplicationDbContext` — is correct and is the narrowest fix
available in this codebase. Every factual claim the design rests on was checked against the current
source and holds. The one issue is that the proposed integration test for atomicity, as worded, could
silently pass for the wrong reason (or be non-deterministic) because of a retry layer the design
didn't fully account for in the test's mechanics — the underlying resilience *analysis* is correct,
only the *test recipe* needs a one-line correction.

## 1. Alignment with existing patterns — verified line-by-line

All claims in `design-01.md` were checked against the current code, not taken on faith:

| Claim | Verified against | Result |
|---|---|---|
| `ITransportBoxRepository`/`IStockUpOperationRepository` both wrap the same scoped `ApplicationDbContext` | `TransportBoxRepository`/`StockUpOperationRepository` both `: BaseRepository<T,TKey>(ApplicationDbContext context)`; `PersistenceModule.cs:99` registers `ApplicationDbContext` via `AddDbContext` (EF default = Scoped) | Confirmed |
| `AddAsync` stages only, no flush | `BaseRepository.cs:57-61` — `DbSet.AddAsync`, no `SaveChangesAsync` call | Confirmed |
| `StockUpProcessingService.CreateOperationAsync` commits immediately per call | `StockUpProcessingService.cs:36-37` — `AddAsync` then `SaveChangesAsync` in the same method | Confirmed |
| `GetByDocumentNumberAsync` already exists, no repo change needed | `IStockUpOperationRepository.cs:5`, implemented `StockUpOperationRepository.cs:18-22` | Confirmed |
| `TransportBoxTransition.ChangeStateAsync` is pure in-memory, no DB access | `TransportBoxTransition.cs:26-30` — invokes a delegate, `Task.FromResult(box)`, no I/O | Confirmed |
| Single `SaveChangesAsync` at `:135` is the only place the box is committed in the Received path | `ChangeTransportBoxStateHandler.cs:134-135` | Confirmed |
| `GiftPackageManufactureService` uses `CreateOperationAsync` (unchanged), not proven in scope | `GiftPackageManufactureService.cs:179,196,252,274` — four call sites, all `CreateOperationAsync` | Confirmed. Note: this service has the *same* split-commit shape one level worse (its own log entity is saved first at `:158-159`/`:240-241`, then N further per-ingredient `CreateOperationAsync` calls each self-committing) — correctly flagged as a candidate follow-up, correctly left untouched here since `ILogisticsStockOperationService.CreateOperationAsync` is not modified, only extended with a new method. |
| No MediatR pipeline behavior wraps transactions | Grepped all 22 `IPipelineBehavior` implementations in `backend/src` — all are per-feature validation/logging behaviors (Smartsupp, Leaflet, KnowledgeBase, generic `ValidationBehavior`/`ValidationResultBehavior`); none touch persistence or Logistics | Confirmed |
| Unique index on `DocumentNumber` is the concurrency backstop | `StockUpOperationConfiguration.cs:52-55` | Confirmed |
| `PollyExecutionStrategy` is the registered Npgsql execution strategy (not `EnableRetryOnFailure`) | `PersistenceModule.cs:108-116` — `npgsql.ExecutionStrategy(deps => new PollyExecutionStrategy(...))`; the class's own doc comment: *"EnableRetryOnFailure must not be used alongside this strategy — there is exactly one retry layer."* | Confirmed |
| Implicit (non-`BeginTransactionAsync`) `SaveChangesAsync` doesn't hit the "execution strategy doesn't support user-initiated transactions" EF guard | Standard EF Core behavior — that guard only fires on explicit `Database.BeginTransactionAsync`/`TransactionScope`; a single `SaveChangesAsync` call is wrapped and retried transparently through the configured strategy | Confirmed, correct EF semantics |
| `IStockUpProcessingService`/`ILogisticsStockOperationService` have exactly one implementation each | `LogisticsStockOperationAdapter` is the sole `ILogisticsStockOperationService`; `StockUpProcessingService` is the sole `IStockUpProcessingService` (both registered `AddTransient` in `CatalogModule.cs`) | Confirmed — adding `StageOperationAsync` to both interfaces is a contained, single-implementer change |

No invariant in the design conflicts with what's actually in the codebase. The "one shared DbContext,
one flush point" mechanism is real, not assumed.

## 2. The one required fix: Test A's failure-injection must not be retry-eligible

The design's resilience analysis (§6, "execution-strategy compatibility") is correct: because the fix
never calls `BeginTransactionAsync`, it never triggers EF's user-initiated-transaction restriction.
But that same resilience layer has a side effect the design's **Test A** (§5.1) doesn't account for:

`DbResiliencePipelineProvider.BuildPipeline` (`Anela.Heblo.Persistence/Infrastructure/Resilience/DbResiliencePipelineProvider.cs:27-30`)
configures Polly to retry (`MaxRetryAttempts = 3` by default, `DbResilienceOptions.cs:7`) any exception
where `TransientErrorClassifier.IsTransient(ex)` returns `true`. Checking the classifier
(`TransientErrorClassifier.cs:45-56`): a `PostgresException` with a connection-class SQLSTATE (`08*`),
a `SocketException`, `TimeoutException`, or `IOException` **are** treated as transient and retried;
a plain `Exception`/`InvalidOperationException` is **not** (falls through to `_ => false`).

Test A's own wording is: *"throws on the **first** `SavingChangesAsync`... simulating a transient DB
failure (e.g. a dropped connection)"*. Read literally, this describes exactly the exception shape
(`08*`/socket/timeout) that `PollyExecutionStrategy` **will** transparently retry — and since the
interceptor is specified to throw only on the first call, a retry means the second `SaveChangesAsync`
attempt succeeds normally. If the test's `DbContextOptions` for this one context happens to be wired
through `PersistenceModule` (with `.ExecutionStrategy(...)` configured) rather than built the same
bare way as the existing `GetStockUpOperationsSummaryIntegrationTests` pattern
(`DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options`, no execution
strategy configured), the test would silently stop testing what it claims to test: it would observe
`Success = true` and a fully-committed box instead of the intended rollback-and-fail path, and nobody
would notice because the assertions would just... pass a different scenario.

**Required correction before implementation:** either (a) keep the test's `DbContextOptions` built the
bare way (matching `GetStockUpOperationsSummaryIntegrationTests`'s existing pattern, which does not
configure an execution strategy and therefore never retries, transient-shaped exception or not), or (b)
if the test intentionally exercises the full `PersistenceModule`-wired context, make the interceptor
throw a non-transient exception type (plain `InvalidOperationException`, or a `23505`/`23503`/`23502`
Postgres SQLSTATE, both explicitly excluded by `TransientErrorClassifier`) so the failure surfaces
regardless of which `DbContextOptions` path is used. Either is a one-line change to the test spec; no
design or production-code change is implied. Recommend documenting the choice explicitly in the test's
comment so a future reader doesn't "fix" it into a transient-classified exception and reintroduce
retry-masking.

This is a testing-design nit that must be resolved before the test is written (so it fails for the
right reason), not a defect in the proposed production fix.

## 3. Other invariants checked, no issues found

- **DI lifetimes**: `IStockUpProcessingService`/`ILogisticsStockOperationService` are `AddTransient`
  (`CatalogModule.cs:58,103`). This doesn't matter for the fix — transient service instances still
  close over the same scoped `ApplicationDbContext` injected per-request, so staging via a transient
  service and committing via a different transient/scoped repository still hits one `SaveChanges` call.
- **`StockUpOperation` constructor** signature (`documentNumber, productCode, amount, sourceType,
  sourceId`) matches exactly what the design's `StageOperationAsync` snippet constructs — confirmed
  against `StockUpOperation.cs:26-31`.
- **Existing unit test impact**: `ChangeTransportBoxStateHandlerTests.cs:47-55` currently stubs
  `CreateOperationAsync` on the `ILogisticsStockOperationService` mock. Since `HandleReceived` will call
  `StageOperationAsync` instead, this stub becomes dead and must be replaced — already listed as an
  explicit to-do in design §5 item 3. No hidden fallout: no other test in the suite references
  `ChangeTransportBoxStateHandler` or mocks this interface for the Received path.
- **`TransportBoxCompletionService`**, **other five transitions**: confirmed structurally untouched —
  none call `_stockOperationService` at all (only `HandleReceived` does), so the change is fully
  contained to the Received path as claimed.

## 4. Prerequisites before implementation begins

1. Decide and lock in the Test A failure-injection exception type/DbContext wiring per §2 above —
   the simplest safe default is: build the test's context the same bare way as
   `GetStockUpOperationsSummaryIntegrationTests` (no `.ExecutionStrategy(...)`), and throw a plain
   `InvalidOperationException` from the interceptor. This sidesteps the retry question entirely rather
   than relying on classifier details staying stable.
2. No other blocking prerequisite. Schema, interfaces, and DI shape are all additive; no migration,
   no `IUnitOfWork`, no new abstraction needed.

## 5. Risks carried forward (informational, not blocking)

- **Pre-existing wedged boxes** (already-committed `StockUpOperation` rows with no matching `Received`
  box, created before this fix ships) are explicitly out of scope — confirmed reasonable, this is a
  forward-fix, not a backfill.
- **`GiftPackageManufactureService`** has a structurally similar (arguably worse — N+1 separate
  commits) non-atomicity pattern. Confirmed real by inspection (`GiftPackageManufactureService.cs:158-159,
  240-241` then per-ingredient `CreateOperationAsync` calls at `:179-185`, `:196-202`, `:252-258`,
  `:274-280`, each self-committing). Correctly deferred as a separate finding — do not silently expand
  this change to cover it.
