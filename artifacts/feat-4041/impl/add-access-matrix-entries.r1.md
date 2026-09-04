# Implementation: add-access-matrix-entries

## What was implemented
Added two new `menuPaths` entries to `access-matrix.json`, granting the `Finance_MarginAnalysis` feature at `Read` level for the routes `/automation/invoice-import-statistics` and `/finance/bank-statements`. This matches the permission already enforced by `AnalyticsController`'s class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` (`backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:14`), so the frontend's access matrix correctly reflects backend authorization for these two new routes.

## Files created/modified
- `access-matrix.json` — inserted two new `menuPaths` entries immediately after the existing `/analytics/product-margin-summary` entry:
  - `{ "path": "/automation/invoice-import-statistics", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] }`
  - `{ "path": "/finance/bank-statements", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] }`

## Tests
N/A — no test files exist for this config file; JSON well-formedness was validated (see below).

## How to verify
1. `grep -n '"/automation/invoice-import-statistics"\|"/finance/bank-statements"' access-matrix.json` — confirm both new entries are present.
2. `python3 -c "import json; d = json.load(open('access-matrix.json')); print(len(d['menuPaths']))"` — confirms valid JSON; entry count is 50 (48 before this change, +2).
3. `git show --stat HEAD` / `git diff HEAD~1 -- access-matrix.json` — confirms the diff is exactly 2 added lines, 0 removed, no other hunks.

## Notes
No deviations from the task instructions. The diff is minimal (2 insertions, 0 deletions) and touches only `access-matrix.json`.

## PR Summary
This change adds two `menuPaths` entries to `access-matrix.json`, granting the pre-existing `Finance_MarginAnalysis` feature at `Read` level for the new `/automation/invoice-import-statistics` and `/finance/bank-statements` frontend routes. This aligns the access matrix with the backend authorization already enforced by `AnalyticsController`'s class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` attribute, ensuring the frontend menu/route guard correctly permits access consistent with the API.

### Changes
- `access-matrix.json` — added two new `menuPaths` entries after `/analytics/product-margin-summary`, both requiring `Finance_MarginAnalysis` at `Read` level.

## Status
DONE
