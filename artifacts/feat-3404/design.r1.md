# Design — feat-3404

## Approach
Single surgical edit: remove the two commented-out `RegisterRefreshTask` blocks (lines 244–270) and the surrounding blank lines, leaving the active cost-source refresh tasks below intact.

## Change
- **File:** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- **Delete:** lines 244–270 (the blank line before the first comment block through the blank line after the second `);`)

No new files, no renaming, no interface changes.
