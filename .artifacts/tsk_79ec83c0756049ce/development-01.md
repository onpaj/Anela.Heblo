# Development: Move AnalyticsRepository out of Persistence

Implemented per architecture-01.md's corrected target path (flat `Features/Analytics/`, not `Features/Analytics/Infrastructure/`), which supersedes plan-01.md / design-01.md on that one point.

## Changes

1. **Moved** `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` → `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsRepository.cs` (via `git mv`, preserving history). Only the `namespace` line changed: `Anela.Heblo.Persistence.Features.Analytics` → `Anela.Heblo.Application.Features.Analytics`. All members, bodies, and doc comments are byte-for-byte unchanged.

2. **`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`**
   - Removed `using Anela.Heblo.Persistence.Features.Analytics;` (no replacement needed — `AnalyticsRepository` is now in the same namespace as the module itself).
   - Updated the stale comment `// Repository (implementation lives in the Persistence layer)` → `// Repository`.
   - DI registration line unchanged: `services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();`.

3. **`backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs`**
   - Stayed in place (already directly under `Features/Analytics/`, no `Infrastructure/` segment to mirror).
   - Swapped `using Anela.Heblo.Persistence.Features.Analytics;` → `using Anela.Heblo.Application.Features.Analytics;`.

4. `backend/src/Anela.Heblo.Persistence/Features/Analytics/` is now an empty directory (git doesn't track empty dirs — nothing further to do).

No logic, contract, or DI-behavior change. `IAnalyticsRepository` (Domain) and the three source interfaces (`IAnalyticsProductSource`, `IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`) are untouched. No `.csproj` changes — `Anela.Heblo.Application.csproj`'s `ProjectReference` to `Anela.Heblo.Persistence.csproj` stays, as it's needed by ~20 other modules in the Application layer unrelated to this change (verified via grep).

## Verification performed

- `grep -rn "Persistence.Features.Analytics" backend/src backend/test` → no remaining references (namespace fully vacated).
- `grep -rln "AnalyticsRepository" backend/src backend/test` → confirmed every other Analytics consumer (5 UseCase handlers, `InvoiceImportStatisticsTile`, and their tests) depends on the `IAnalyticsRepository` **interface** only, never the concrete class — so only `AnalyticsModule.cs` and `AnalyticsRepositoryTests.cs` needed edits.
- Confirmed no namespace/type collision: `Anela.Heblo.Application.Features.Analytics` already holds `AnalyticsModule`; C# permits multiple types per namespace across files (mirrors `CatalogModule`/`CatalogRepository` coexisting today).
- Confirmed `.csproj` files use default SDK-style globs (only a few unrelated `Compile Remove` entries in Persistence, none touching this file) — the move requires no project-file edits.
- `git diff --stat` shows exactly 3 files changed, 3 insertions(+), 4 deletions(-) — matches the minimal diff architecture-01.md specified.

**Not run: `dotnet build` / `dotnet format` / `dotnet test`.** No .NET SDK, `dotnet`, `docker`, or `podman` binary is available in this sandbox (`which dotnet` → not found; no dotnet install under common paths; podman aliased but binary absent). This is a pure namespace/location relocation with no logic changes, verified statically as above, but **CI/the next reviewer should run `dotnet build && dotnet format --verify-no-changes && dotnet test --filter FullyQualifiedName~Analytics`** before merge to get compiler-level confirmation.

## How to verify

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test --filter FullyQualifiedName~Analytics
```

Expect: build succeeds, format reports no changes, and `AnalyticsRepositoryTests.StreamProductsWithSalesAsync_DelegatesToProductSource` (plus all other Analytics-module tests) pass unchanged.
