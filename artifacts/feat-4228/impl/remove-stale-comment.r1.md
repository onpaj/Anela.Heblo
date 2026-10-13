# Implementation: remove-stale-comment

## What was implemented
Removed the stale comment `// Note: IMarginCalculationService is registered by CatalogModule and injected here` from `AnalyticsModule.cs`. Analytics has no dependency on any Catalog-registered `IMarginCalculationService` — it uses its own self-registered `IMarginCalculator`. The comment described a cross-module dependency that does not exist and was misleading to future readers.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — deleted the single stale comment line (line 32). No other lines touched; the `// Register refactored services for clean separation of concerns` comment above and both surrounding `AddScoped` calls are unchanged.

## Tests
No new tests — comment-only change with no behavioral impact. Ran the existing Analytics test suite to confirm no regression (see below).

## How to verify
```bash
grep -n "IMarginCalculationService" backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs; echo "exit:$?"
# -> no match, exit:1

grep -n "IMarginCalculator, MarginCalculator" backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
# -> one match, real registration untouched

dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
# -> Build succeeded. 0 Errors.

dotnet test --filter "FullyQualifiedName~Analytics"
# -> Passed! - Failed: 0, Passed: 151, Skipped: 0, Total: 151
```

`git diff` confirms exactly a single-line deletion, nothing else touched:
```diff
-        // Note: IMarginCalculationService is registered by CatalogModule and injected here
```

`dotnet format` was run against the file and reported no further changes needed.

## Notes
No deviations from the task-context spec. All 7 steps executed exactly as specified; every expected output matched.

## PR Summary
Removed a stale, misleading comment in `AnalyticsModule.cs` that claimed `IMarginCalculationService` is registered by `CatalogModule` and injected into Analytics. That service and cross-module dependency do not exist — Analytics computes margins via its own self-registered `IMarginCalculator`. This was a pure documentation/comment fix with no behavioral change; the existing Analytics test suite (151 tests) passes unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — removed the stale comment line

## Status
DONE
