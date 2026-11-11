# Architecture Review: Named account-class constants

## Skip Design: true
Backend-only refactor, no UI.

## Architectural Fit Assessment
Fits existing conventions: the class already declares private `const string` members (e.g. `MONTHLY_DATA_CACHE_KEY_PREFIX`). A grep of `backend/src` shows the `"5"`/`"6"` prefixes are used only in `CalculatePeriodTotals`, so a Domain-level type would be speculative.

## Proposed Architecture

### Component Overview
Single class `FinancialAnalysisService`; no new components.

### Key Design Decisions

#### Decision 1: Private consts vs. Domain type
**Options considered:** private consts; static `LedgerAccountClass` in Domain.
**Chosen approach:** private consts in the service.
**Rationale:** single use site, smallest change; promote later if reused.

## Implementation Guidance

### Directory / Module Structure
Edit only `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`.

### Interfaces and Contracts
No public contract changes.

### Data Flow
Unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Accidental behaviour change | Low | Existing FinancialOverview tests must pass unchanged |

## Specification Amendments
None.

## Prerequisites
None. Per CLAUDE.md, bump `verified_at` in `docs/processes/calc-financial-overview.md` (behaviour unchanged) and run `python3 scripts/process-docs/check.py index`.
