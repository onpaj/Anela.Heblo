# Implementation: add-handler-tests

## What was implemented
Four new tests in CalculateBatchPlanHandlerTests covering the non-mutation contract of CalculateBatchPlanHandler: Product+MultiPhase (semiproduct code forwarded, original untouched), Product+SinglePhase, MultiPhase without SemiProduct ingredient, and a reflection-based test that every public read/write property except ProductCode and ManufactureType is copied to the forwarded request.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs`

## Tests
CalculateBatchPlan tests: 51 passed (47 before + 4 new).

## How to verify
`cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~CalculateBatchPlan`

## Notes
Developer/reviewer subagents were not spawned (no Task tool); orchestrator implemented and self-reviewed.

## PR Summary
Added regression tests proving the handler no longer mutates the incoming request.

### Changes
- `CalculateBatchPlanHandlerTests.cs` - 4 new tests and small helpers

## Status
DONE
