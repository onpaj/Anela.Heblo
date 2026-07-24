# Implementation: remove-unused-orgchart-config-param

## What was implemented
Removed the unused `IConfiguration configuration` parameter from `AddOrgChartAdapter` (a DI-registration extension method that never read the parameter) and removed the now-unnecessary `using Microsoft.Extensions.Configuration;` directive. Updated the single call site in `Program.cs` to drop the now-obsolete `builder.Configuration` argument.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs` — dropped the unused `IConfiguration configuration` parameter and its stray `using` directive; signature simplified to `AddOrgChartAdapter(this IServiceCollection services)`.
- `backend/src/Anela.Heblo.API/Program.cs` — line 128 changed from `builder.Services.AddOrgChartAdapter(builder.Configuration);` to `builder.Services.AddOrgChartAdapter();`.

## Tests
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (1 pre-existing unrelated warning from an access-matrix code-gen post-build step that predates this change).
- `dotnet format Anela.Heblo.sln --include <the two changed files>` — no changes needed.
- `dotnet test Anela.Heblo.sln` — 76-161 failures depending on run, all pre-existing and environmental: Testcontainers/Docker-backed integration tests (`PostgresSharedContainerFixture`, `*SqlShapeTests`, `*RealDatabase*`) fail because no Docker daemon is available in this sandbox, and `*IntegrationTests` under `Flexi`/`Shoptet` adapters fail because they require live external API access. `grep -rn "AddOrgChartAdapter" backend/ --include="*.cs"` confirms exactly two matches (definition + call site) and no OrgChart-related test references either symbol. No failure is related to `AddOrgChartAdapter` or `OrgChartAdapterServiceCollectionExtensions`.

## How to verify
```bash
grep -rn "AddOrgChartAdapter" backend/ --include="*.cs"
git diff backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs backend/src/Anela.Heblo.API/Program.cs
cd backend && dotnet build ../Anela.Heblo.sln
```

## Notes
The task plan's suggested `cd backend && dotnet build`/`dotnet format`/`dotnet test` commands don't work as written because the `.sln` file lives at the repo root, not under `backend/`; ran them against `Anela.Heblo.sln` from the repo root instead. No other deviations — the diff matches the plan's exact target code.

## PR Summary
Removed a dead `IConfiguration configuration` parameter from `AddOrgChartAdapter`, a DI-registration extension method that never read it (the inline comment called it "reserved for future base-URL configuration" — pure YAGNI speculation). Updated the method's one call site in `Program.cs` to match the simplified signature. No behavior change; this is a signature-only cleanup.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs` — removed unused parameter and its `using` directive
- `backend/src/Anela.Heblo.API/Program.cs` — updated call site to drop the removed argument

## Status
DONE
