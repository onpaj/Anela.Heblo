All three commits are on the branch. Here is the implementation summary:

---

# Implementation: Persist scheduled DQT run records before execution

## What was implemented

Added `await _repository.SaveChangesAsync(cancellationToken)` between `AddAsync` and `_jobRunner.RunAsync` in all three scheduled DQT job classes. This ensures the `DqtRun` row is durably committed with `Status = Running` before the runner executes, so a process crash mid-run still leaves an audit trail — matching the existing behaviour of the manual `RunDqtHandler` path.

## Files created/modified

- `backend/src/.../Infrastructure/Jobs/InvoiceDqtJob.cs` — one line inserted at line 50
- `backend/src/.../Infrastructure/Jobs/ProductPairingDqtJob.cs` — one line inserted at line 50
- `backend/src/.../Infrastructure/Jobs/StockWriteBackDqtJob.cs` — one line inserted at line 50
- `backend/test/.../DataQuality/InvoiceDqtJobTests.cs` — new, 3 tests
- `backend/test/.../DataQuality/ProductPairingDqtJobTests.cs` — new, 3 tests
- `backend/test/.../DataQuality/StockWriteBackDqtJobTests.cs` — new, 3 tests

## Tests

Nine new unit tests across three new test files (first-ever coverage for these job classes). Each file asserts: (1) call order `AddAsync → SaveChangesAsync → RunAsync` when enabled, (2) disabled-job short-circuit with no persistence, (3) `cancellationToken` propagation into `SaveChangesAsync`.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DataQuality" --logger "console;verbosity=normal"
```
All 9 new tests plus all pre-existing DataQuality tests pass.

## Notes

38 pre-existing test failures exist in unrelated modules (Bank statements, Marketing Calendar) — none introduced by this change.

## PR Summary

Persist the `DqtRun` audit row before execution in all three scheduled DQT jobs (`InvoiceDqtJob`, `ProductPairingDqtJob`, `StockWriteBackDqtJob`). Previously, a process crash mid-run would leave no database record. The manual trigger path (`RunDqtHandler`) already persisted the `Running` row first — this change brings all scheduled jobs into parity.

Each job now follows the same `AddAsync → SaveChangesAsync → RunAsync` sequence. No schema changes, no API changes, no new dependencies. Adds the first unit-test file for each of the three job classes (9 tests total) asserting call order, disabled-job short-circuit, and cancellation-token propagation.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs` — inserted `SaveChangesAsync` before runner invocation
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` — same
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs` — same
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobTests.cs` — new test file
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs` — new test file
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtJobTests.cs` — new test file

## Status
DONE