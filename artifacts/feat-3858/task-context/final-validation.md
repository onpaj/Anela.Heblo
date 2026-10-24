### task: final-validation
- `dotnet build backend` and `dotnet format backend --verify-no-changes` clean.
- All tests touched pass (Adapters.Flexi.Tests, Anela.Heblo.Tests filters above, plus `Shared/CostPools` and `Features/Catalog` suites that construct ledger consumers).
- Re-read the diff: every changed line traces to FR-1..FR-6; no timeout/circuit-breaker edits; no secrets or amounts in logs.
