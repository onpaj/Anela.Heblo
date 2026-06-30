# Architecture Review: Fix Recurring Jobs E2E Tests

## Skip Design: true

This change is confined entirely to a single E2E test file. There are no new visual components, no UI changes, no API changes, and no backend changes. Design phase is not required.

## Architectural Fit Assessment

This is a test maintenance fix, not a feature. The change aligns perfectly with the existing E2E testing patterns in this codebase. The recurring-jobs test file uses the standard Playwright page-object pattern already established in the suite — no new patterns are introduced.

The root cause (hard-coded counts becoming stale as the application grows) is a well-known test brittleness problem. The proposed fix (`toBeGreaterThanOrEqual`) is the idiomatic solution.

## Proposed Architecture

### Component Overview

No new components. The only file touched is:

```
frontend/test/e2e/core/recurring-jobs-management.spec.ts
```

### Key Design Decisions

#### Decision 1: Assert minimum count, not exact count
**Options considered:**
1. Assert exact count `toBe(24)` — would break again when the next job is added
2. Assert `toBeGreaterThanOrEqual(24)` — tolerates future growth
3. Assert `toBeGreaterThan(0)` — too permissive, doesn't verify the full list loads

**Chosen approach:** `toBeGreaterThanOrEqual(24)` — documents the current minimum while tolerating future additions.

**Rationale:** The minimum of 24 provides a meaningful lower bound that catches regressions (e.g., the entire list failing to load), while not requiring test updates for every new job added.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify only:
- `frontend/test/e2e/core/recurring-jobs-management.spec.ts`

### Interfaces and Contracts

No interface changes. The `GET /api/recurring-jobs` endpoint contract is unchanged.

### Data Flow

No data flow changes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Minimum of 24 becomes stale if jobs are removed | Low | Comment in test explains how to update the minimum |
| FlexiAnalyticsSyncJob conditionally registered (23 vs 24) | Low | Staging confirmed to return 24; if this ever changes, the test will surface it |

## Specification Amendments

None. The spec is complete and accurate.

## Prerequisites

None. The change requires only editing the test file and running the E2E suite against staging to verify.
