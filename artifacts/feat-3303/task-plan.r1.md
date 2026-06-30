# Task Plan: Unit Tests for BlockOrderProcessingHandler

## Summary

The implementation requested by this issue already exists. The single task below verifies and documents this finding.

---

### task: verify-existing-tests

**Goal:** Confirm the existing `BlockOrderProcessingHandlerTests.cs` covers all required scenarios and passes CI.

**Context:**
- Test file: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`
- Added in commit `dfcbebc` on 2026-06-18
- Covers: happy path (empty remark), happy path (appended remark), invalid source state guard, status fetch throws, status update throws, remark fetch throws (non-cancel), remark update throws (non-cancel), `OperationCanceledException` propagation

**Files to create/modify:** None — verification only.

**Implementation steps:**
1. Run `dotnet test --filter "FullyQualifiedName~BlockOrderProcessingHandlerTests"` and confirm all 10 tests pass.
2. Write the impl artifact documenting the result.

**Tests to write:** N/A — tests already exist.

**Acceptance criteria:**
- `dotnet test` reports `Passed: 10, Failed: 0`
- All 8 functional requirements from the spec are covered by the existing tests
