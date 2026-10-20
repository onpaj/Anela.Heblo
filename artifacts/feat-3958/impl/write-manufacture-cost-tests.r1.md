# Implementation: write-manufacture-cost-tests

## What was implemented
Unit tests for `ManufactureCost` date-based MaterialCost switch and Total.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Catalog/ManufactureCostTests.cs` - 8 test methods (10 cases) covering before/after cutoff, midnight boundary, same-day, last second, Total, default.

## Tests
10 cases pass. Mutation check (`>` to `>=`) made `MaterialCost_ExactlyAtCutoffMidnight_UsesPurchasePrice` fail; reverted.

## How to verify
`cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureCostTests"`

## Notes
Implemented directly by the orchestrator (no subagent Task tool available). No production code changed.

## PR Summary
Adds unit tests locking in the ManufactureCost material-cost cutoff behaviour (strict greater-than at 2025-08-25 00:00).

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Catalog/ManufactureCostTests.cs` - new tests

## Status
DONE
