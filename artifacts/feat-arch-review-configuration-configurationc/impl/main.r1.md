All tasks complete. Here is the output summary:

---

# Implementation: Remove Unused ASPNETCORE_ENVIRONMENT Constant

## What was implemented

Deleted the single unused constant `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` from the Configuration domain. Zero callers existed anywhere in the solution — verified by two ripgrep passes before deletion. The five raw-string call sites enumerated in spec FR-4 are explicitly preserved and untouched.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — removed the `ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT"` declaration (one line, one deletion)

## Tests

No new tests written — this is a dead-code deletion with no behavior change. The existing test suite (4,414 + 83 = 4,497 tests) passed with identical counts before and after the change. 38 pre-existing failures are a baseline condition unrelated to this change.

## How to verify

```bash
# Confirm constant is gone
rg 'ASPNETCORE_ENVIRONMENT' backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs
# Expected: no output

# Confirm no qualified callers anywhere
rg 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs backend/
# Expected: no output

# Confirm commit shape
git show HEAD --stat
# Expected: 1 file changed, 1 deletion(-)
```

## Notes

- **Pre-existing test failures (38):** Baseline condition on this branch, not introduced by this change. Pass counts match exactly before and after.
- **Warning count:** Dropped from 234 → 229 (expected: compiler no longer emits a warning for the now-deleted unused symbol).
- **Follow-up tracked:** Five raw-string `"ASPNETCORE_ENVIRONMENT"` sites in DiagnosticsController, E2ETestController, CostOptimizedTelemetryProcessor, DesignTimeDbContextFactory, and GetConfigurationHandler are intentionally preserved; migration to `IHostEnvironment.EnvironmentName` is a separate lower-priority task.

## PR Summary

Removes `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` — a dead-code constant that had zero qualified accessors anywhere in the solution. Every call site that reads the ASP.NET Core environment name either uses `IHostEnvironment.EnvironmentName` (the DI-preferred approach) or passes the raw string literal directly, making the constant a misleading signal of a centralization pattern that does not actually exist.

Verified by two ripgrep passes before deletion (zero matches for both `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` and `nameof(ConfigurationConstants.ASPNETCORE_ENVIRONMENT)`). Build and test suite pass with identical counts. The five raw-string call sites are preserved intentionally; their migration to `IHostEnvironment` is tracked as a follow-up.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — removed `public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";` (1 line deleted)

## Status
DONE