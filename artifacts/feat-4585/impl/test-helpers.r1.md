# Implementation: test-helpers

## What was implemented
Unit tests for `isOperationStuck` and `canRetry` with fixed `now` and a `minutesAgo` helper. Covers Submitted (5/6 min boundary, no submittedAt, recent submittedAt with old createdAt), Pending (10/11 min boundary, old submittedAt ignored), Completed/Failed 1000 min old, and canRetry for all states plus undefined.

## Files created/modified
- `frontend/src/pages/__tests__/stockOperationsUtils.test.ts` - new, 14 tests

## Tests
14 pass. Mutation check (threshold 5 -> 6) made "is stuck at 6 minutes" fail; reverted.

## How to verify
`cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__/stockOperationsUtils.test.ts`

## Notes
Implemented directly by the orchestrator (no subagent spawn tool available in this run). eslint clean.

## PR Summary
Added unit tests for the stock operation stuck/retry helpers.

### Changes
- `frontend/src/pages/__tests__/stockOperationsUtils.test.ts` - boundary and state coverage

## Status
DONE
