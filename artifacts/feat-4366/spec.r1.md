# Specification: Name the Czech chart-of-accounts class prefixes in FinancialAnalysisService

## Summary
Replace the bare `"5"` / `"6"` literals in `FinancialAnalysisService.CalculatePeriodTotals` with named, documented constants. No behaviour change.

## Background
Account class 5 = expenses (náklady), class 6 = revenues (výnosy) in the Czech Chart of Accounts. The rule is currently implicit in four `StartsWith` calls.

## Functional Requirements

### FR-1: Named constants
Add `ExpenseAccountClassPrefix = "5"` and `IncomeAccountClassPrefix = "6"` as private consts in `FinancialAnalysisService`, with a comment explaining the Czech Chart of Accounts rule.
**Acceptance criteria:**
- No `StartsWith("5")` / `StartsWith("6")` literals remain in the file.
- `CalculatePeriodTotals` returns identical results for all inputs.

## Non-Functional Requirements
### NFR-1: Performance
Unchanged (consts are inlined by the compiler).
### NFR-2: Security
N/A.

## Data Model
None.

## API / Interface Design
None; private change only.

## Dependencies
None.

## Out of Scope
Extracting a Domain `LedgerAccountClass` type (no other usage of these literals exists in `backend/src`); adding class 4 logic.

## Open Questions
None.

## Status: COMPLETE
