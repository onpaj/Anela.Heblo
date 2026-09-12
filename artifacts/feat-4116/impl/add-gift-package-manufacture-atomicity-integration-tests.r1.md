# Implementation: add-gift-package-manufacture-atomicity-integration-tests

## What was implemented

Added a new xUnit integration test class, `GiftPackageManufactureAtomicityIntegrationTests`, that proves
`GiftPackageManufactureService.CreateManufactureAsync` and `DisassembleGiftPackageAsync` (FR-1/FR-2) commit
the `GiftPackageManufactureLog` row, its `GiftPackageManufactureItem` children, and every `StockUpOperation`
row as a single atomic unit — or none of them — against a **real Postgres instance** (via the shared
`PostgresSharedContainerFixture` Testcontainers fixture), not EF InMemory or Moq. The `DbContext` in every
test is built with `PollyExecutionStrategy` configured exactly as production does
(`PersistenceModule.AddPersistenceServices`), so the tests also exercise FR-3 acceptance criterion (c): that
`BaseRepository.ExecuteInTransactionAsync`'s `Context.Database.CreateExecutionStrategy().ExecuteAsync(...)`
pattern works under a retrying execution strategy without throwing
`InvalidOperationException: ... does not support user-initiated transactions`.

Implemented the plan's file exactly as specified (all 7 steps in one pass, since this run is non-interactive):
schema setup, the Polly-configured `CreateContext` helper, `SetupTwoIngredientBom` test-data builder, one
happy-path + one rollback test per method (4 tests total).

**Investigation of the KNOWN RISK (task instructions section B):** Before running anything, I decompiled the
installed EF Core 8.0.8 (`Microsoft.EntityFrameworkCore`/`.Relational` 8.0.8, via `ilspycmd`) to trace exactly
where `InvalidOperationException: The configured execution strategy '{strategy}' does not support
user-initiated transactions...` (`CoreStrings.ExecutionStrategyExistingTransaction`) is thrown. It is thrown
only from `ExecutionStrategy.OnFirstExecution()` — a method defined on the **abstract base class**
`Microsoft.EntityFrameworkCore.Storage.ExecutionStrategy`, guarded by `RetriesOnFailure && Database.CurrentTransaction != null`,
and only ever invoked from that base class's own `ExecuteImplementationAsync`/`Execute` internals.
`PollyExecutionStrategy` implements `IExecutionStrategy` **directly** (it does not derive from
`ExecutionStrategy`), and its own `Execute`/`ExecuteAsync` overrides never call `OnFirstExecution()` — so this
specific guard can never fire through `PollyExecutionStrategy`, regardless of nesting. This was then confirmed
empirically: all 4 tests, including both rollback tests that call `Context.Database.BeginTransactionAsync()`
from inside `strategy.ExecuteAsync(...)` under `PollyExecutionStrategy`, pass without that exception ever
being thrown. **Conclusion: the KNOWN RISK did not materialize; no production code change to
`PollyExecutionStrategy` was needed or made.**

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs` (new, 378 lines) — the four integration tests, schema setup, and helpers.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — **temporarily** modified for the non-vacuity experiment (see below), then restored byte-for-byte before committing. `git diff` on this file is empty in the final state; it is not part of the commit.

## Tests

All in `GiftPackageManufactureAtomicityIntegrationTests`, `[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]` (excluded from CI by the existing `Category!=Integration` filter, as intended):

1. `CreateManufactureAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether` — happy path for a 2-ingredient BOM (qty 5): asserts exactly 1 log row with 2 consumed items, and 3 `StockUpOperation` rows (2 ingredient stock-downs at -10/-7, 1 output stock-up at +5) with the correct `GPM-{id:000000}-{code}` document-number format.
2. `CreateManufactureAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether` — injects a non-transient `InvalidOperationException` on the 4th (last) of the 4 `SaveChangesAsync` calls the method makes for a 2-ingredient BOM; asserts zero `GiftPackageManufactureLog`, zero `GiftPackageManufactureItem`, and zero `StockUpOperation` rows survive — proving the first 3 already-flushed-but-uncommitted writes roll back together with the failing 4th.
3. `DisassembleGiftPackageAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether` — happy path for disassembly (qty 3): asserts 1 Disassembly-type log row with 2 items, and 3 stock ops (1 package stock-down at -3, 2 component stock-ups at +6/+4) with `GPD-{id:000000}-{code}` format.
4. `DisassembleGiftPackageAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether` — same shape as test 2, for disassembly; asserts zero rows of all three kinds survive.

## Test results

Command:
```
cd /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Build: `0 Error(s)` (234 pre-existing warnings elsewhere in the test project, unrelated to this change).

Test output (verbatim):
```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 s - Anela.Heblo.Tests.dll (net8.0)
```

`dotnet format ... --include <this file> --verify-no-changes` exited 0 (no formatting issues).

## Non-vacuity evidence

Per instructions section C, before declaring done I ran the mandatory no-transaction experiment:

**What was changed (temporarily, never committed):** In `GiftPackageManufactureService.cs`, both
`return await _giftPackageRepository.ExecuteInTransactionAsync(async ct => { ... }, cancellationToken);`
call sites (in `CreateManufactureAsync` and `DisassembleGiftPackageAsync`) were replaced with a direct
invocation of the same delegate body outside any transaction:
```csharp
return await ((Func<CancellationToken, Task<GiftPackageManufactureDto>>)(async ct =>
{
    ... unchanged body ...
}))(cancellationToken);
```
(and the `GiftPackageDisassemblyDto` equivalent for disassembly), preserving every line inside the delegate
untouched.

**Build:** `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false` → `0 Error(s)`.

**Ran only the two rollback tests:**
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests&FullyQualifiedName~RollsBack"
```

**Observed output (verbatim, both tests FAILED as required):**
```
CreateManufactureAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether [FAIL]
  Expected (readContext.Set<GiftPackageManufactureLog>().CountAsync()) to be 0, but found 1 (difference of 1).

DisassembleGiftPackageAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether [FAIL]
  Expected (readContext.Set<GiftPackageManufactureLog>().CountAsync(x => x.OperationType == GiftPackageOperationType.Disassembly)) to be 0, but found 1 (difference of 1).

Total tests: 2
     Failed: 2
```
Both tests failed on the very first `Should().Be(0)` assertion (the log-row count), which FluentAssertions
short-circuits on — so the subsequent item/stock-op count assertions in the same test method did not execute
this run. The log-row evidence alone (1 survives without the transaction wrapper vs. 0 with it) is sufficient
to prove the assertion is not vacuous: the log created by save call #1 persisted even though a later save call
in the same logical operation failed, which is exactly the defect FR-1/FR-2/FR-3 fix and exactly what the
"expect 0" assertion is designed to catch.

**Restore:** `cp` the original file back (saved via `cp GiftPackageManufactureService.cs /tmp/....orig` before
the edit), then verified byte-for-byte via `git diff --stat` on that file, which produced **no output**
(confirmed empty).

**Rebuild + re-run full class after restore:**
```
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false   →  0 Error(s)
dotnet test  ... --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 s
```
All 4 tests pass again with the real transaction wrapper restored. `git status --short` at commit time shows
only the new test file as staged (`A`) plus the pre-existing, unrelated `artifacts/feat-4116/state.json`
modification that was already present before this task started — `GiftPackageManufactureService.cs` is not
in the diff.

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```
Requires Docker/Podman running locally (Testcontainers pulls/starts a `postgres:16` container; confirmed
working here via the podman socket at `/var/run/docker.sock` with `ryuk.disabled=true`).

## Notes

- **No production code changes were needed or made.** The task's "KNOWN RISK" section anticipated that
  `PollyExecutionStrategy` might reject `BeginTransactionAsync` because it doesn't derive from EF Core's
  abstract `ExecutionStrategy` and thus doesn't participate in that class's nesting/suspension mechanism. I
  traced this precisely (decompiling EF Core 8.0.8 with `ilspycmd`) and confirmed the specific guard that
  throws "does not support user-initiated transactions" (`ExecutionStrategy.OnFirstExecution()`) is a method
  on the abstract base class that `PollyExecutionStrategy` never calls (since it implements `IExecutionStrategy`
  directly rather than deriving from `ExecutionStrategy`) — so the guard structurally cannot fire through this
  code path. This was then verified empirically: all 4 tests pass, including the two that call
  `BeginTransactionAsync` from inside `PollyExecutionStrategy.ExecuteAsync`. This is worth flagging as a subtle
  characteristic of the current `PollyExecutionStrategy` design (it also means `RetriesOnFailure` is hardcoded
  `true` regardless of nesting, unlike EF's own strategies, which report `false` when nested under an outer
  strategy) — outside this task's scope to change, but noted for whoever reviews FR-3 next.
- Implemented the plan's test file verbatim (all 4 tests in one file/one commit, combining the plan's
  incremental steps 1–6 since this is a non-interactive single-pass run) — no deviations from the provided
  code.
- The rollback tests' assertions short-circuit on the first `Should().Be(0)` failure (log count), so the
  no-transaction experiment's observed evidence covers the log-row assertion specifically, not the
  item/stock-op counts in the same failing run. Given the log row is written first (save #1) and the failure
  is injected on save #4, a surviving log row is the strongest and most direct evidence of the exact defect
  being tested for; I consider this sufficient non-vacuity proof but flag the specific scope of the observed
  failure output for full transparency.
- Followed the coding-style rule library's "surgical changes" principle from CLAUDE.md: touched only the one
  new test file for the final commit; the temporary experiment edit was reverted, never staged, and confirmed
  clean via `git diff`.

## PR Summary

Adds `GiftPackageManufactureAtomicityIntegrationTests`, a real-Postgres integration test class that closes the
verification gap FR-1/FR-2/FR-3 (feat-4116, `wrap-create-manufacture-async-in-transaction` /
`wrap-disassemble-gift-package-async-in-transaction` / `add-execute-in-transaction-repository-method`) left
open: Moq-based unit tests can assert `ExecuteInTransactionAsync` was *called*, but only a real relational
database can prove the resulting `BEGIN`/`COMMIT`/`ROLLBACK` behavior is actually atomic. Four tests — one
happy-path and one Nth-`SaveChangesAsync`-fails rollback test per method — run against a Testcontainers
Postgres instance with the `DbContext` built exactly as production configures it (`PollyExecutionStrategy`
included), so they also exercise FR-3's acceptance criterion (c): that
`Context.Database.CreateExecutionStrategy().ExecuteAsync(...)` genuinely works under a retrying strategy. No
production code changes were required — investigation (EF Core decompilation + an empirical no-transaction
experiment, both detailed in the implementation notes) confirmed both that the anticipated
"does not support user-initiated transactions" risk does not apply to `PollyExecutionStrategy`'s specific
design, and that the rollback assertions are genuinely non-vacuous (they fail, with a surviving log row, when
the transaction wrapper is temporarily bypassed).

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs` — new file, 4 integration tests + schema/helper setup.

## Status
DONE
