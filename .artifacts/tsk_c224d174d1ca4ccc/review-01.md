# Review: Remove SafeMarginCalculator / SalesCost dead code

## Scope of the diff (verified via `git diff HEAD~1`)

- **Deleted** `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs` (68 lines, includes the nested `MarginCalculationResult` in the `Catalog` namespace).
- **Deleted** `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SalesCost.cs` (9 lines).
- **Deleted** `backend/test/Anela.Heblo.Tests/Features/Catalog/SafeMarginCalculatorTests.cs` (224 lines).
- **Modified** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — removed exactly one line: `services.AddTransient<SafeMarginCalculator>();`. No other lines touched.

Total: 4 files changed, 302 deletions, 0 non-deletion insertions (module comment line excluded). Perfectly surgical — matches plan-01.md / design-01.md / architecture-01.md's four-file, one-line-removal shape with no scope creep.

## Independent verification performed this step

1. **Diff inspection** — confirmed the `CatalogModule.cs` change is a single-line removal, nothing else touched.
2. **Reference sweep** — `grep`/`Grep` for `SafeMarginCalculator` and `\bSalesCost\b` across `backend/`: zero hits anywhere (source or test). Confirms no dangling reference was left behind.
3. **File existence** — `Glob` for `SafeMarginCalculator*.cs` and `SalesCost.cs` under `backend/`: no matches, confirming all three targeted files are actually gone.
4. **Build** — `dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj`: **0 errors**, 155 pre-existing warnings unrelated to this change (one MSB3073 warning from an unrelated post-build codegen tool exiting non-zero, also pre-existing/environmental, not an error).
5. **`CompositionRootTests`** (`ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices`) — the direct positive control for "did removing a DI registration break the container": **1/1 passed**.
6. **`dotnet format --verify-no-changes`** on `Anela.Heblo.Application.csproj` — exit code 0, no formatting drift.
7. **Broader regression check** — ran the full `Features.Catalog` + `Features.Analytics` test filter (822 tests, 7m33s): 814 passed, 8 failed. All 8 failures are `PostgresSharedContainerFixture`/Testcontainers-originated (confirmed via stack trace — `DotNet.Testcontainers.Resource.DisposeAsync` → HTTP connection to a Docker daemon that isn't reachable in this sandbox), matching exactly what development-01.md reported as pre-existing/environmental. No failure relates to `MarginCalculationService`, `MarginLevel`, or the Analytics `MarginCalculationResult`.

## Assessment against spec / architecture / design

- **Conformance to spec**: the finding asked to either remove the dead `SafeMarginCalculator`+`SalesCost` (with DI line and test) or consolidate behind one authoritative calculator. The team chose removal, which is a valid resolution the finding explicitly offered, and it's justified by the plan's confirmation of zero production callers.
- **Architecture adherence**: architecture-01.md's specific guardrail (delete `SafeMarginCalculator.cs` by file path rather than symbol-based tooling, to avoid ambiguity with the unrelated, live `MarginCalculationResult` in `Analytics/Services/`) was followed — the diff shows only the Catalog file was touched; the Analytics `MarginCalculationResult` file is untouched and its tests still pass.
- **Completeness**: all four items called out in plan/design/architecture were removed together (calculator + nested result type, DTO, DI line, dedicated test file). No orphaned reference left behind.
- **Correctness**: no logic change to any surviving code path — this is pure deletion. `MarginCalculationService`/`MarginLevel` (the authoritative pipeline) is untouched and its own tests still pass. DI container still validates cleanly.

No functional requirement is unmet, no architecture conflict, no missing required test (deletion of the dead component's own test is exactly what's called for — there is no remaining production code path to add coverage for), and no correctness bug was found.

```json
{"outcome": "done", "summary": "Pure deletion diff (SafeMarginCalculator.cs, SalesCost.cs, SafeMarginCalculatorTests.cs, one DI line) verified: zero remaining references anywhere in backend/, files confirmed removed, dotnet build succeeds (0 errors), dotnet format clean, CompositionRootTests DI-validation passes, and a full Catalog+Analytics regression run (822 tests) shows only pre-existing Docker/Testcontainers-environment failures unrelated to this change. Matches plan/design/architecture exactly with no scope creep."}
```
