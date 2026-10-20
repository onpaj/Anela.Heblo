# Architecture Review: computePercentage unit tests

## Skip Design: true
Test-only change, no UI.

## Architectural Fit Assessment
Fits existing convention: component tests live in `frontend/src/components/pages/__tests__/`, using Vitest + Testing Library. `computePercentage` is a pure exported helper in the component module, so it is tested by direct import. Verified the test file already does this (`import ManufactureBatchCalculator, { computePercentage } from '../ManufactureBatchCalculator'`) and contains 12 helper cases covering all guard branches and the happy path.

## Proposed Architecture
### Component Overview
No new components. One existing test file; one pure function.
### Key Design Decisions
#### Decision 1: Do not add duplicate tests
**Options:** (a) add a new test file; (b) extend the existing describe block; (c) add nothing if coverage is complete.
**Chosen:** (c), falling back to (b) only if a missing case is identified.
**Rationale:** The existing block already covers null, undefined, 0, negative, NaN, Infinity, -Infinity, 0 amount, rounding, negative amount. Duplicates add noise (CLAUDE.md: surgical changes).
#### Decision 2: Do not move the helper to a utils module
Out of scope; the export is sufficient for testing.

## Implementation Guidance
### Directory / Module Structure
`frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (existing).
### Interfaces and Contracts
`computePercentage(calculatedAmount: number, newBatchSize: number | null | undefined): string`.
### Data Flow
Not applicable.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Issue already satisfied, leading to an empty PR | Low | Implementer runs tests, records result, and reports; no code change required |
| Existing tests fail in the environment | Low | Fix only if the failure concerns computePercentage tests |

## Specification Amendments
Spec already reflects that tests exist.

## Prerequisites
`npm ci` in `frontend/`.
