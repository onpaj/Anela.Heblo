# Implementation: docs-and-validate

## What was implemented
- docs/processes/module-dashboard.md and calc-dashboard-tiles.md: GET settings/data no longer persist; they return an in-memory default/back-fill projection via IUserDashboardSettingsReader; UserDashboardSettingsProvisioner creates the row and back-fills on first mutation. Known quirks, data flow and Code entry points updated; verified_at bumped to abe05bfd.
- `python3 scripts/process-docs/check.py index` regenerated INDEX.md (no diff).

## Validation
- dotnet build backend/test/Anela.Heblo.Tests: 0 errors.
- dotnet format --verify-no-changes on changed .cs files: clean.
- dotnet test --filter FullyQualifiedName~Features.Dashboard: 83 passed, 0 failed.
- grep IMediator / GetUserSettingsRequest in Features/Dashboard/Infrastructure and GetTileDataHandler: no matches.

## Status
DONE
