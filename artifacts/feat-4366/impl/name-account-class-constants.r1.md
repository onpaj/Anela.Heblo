# Implementation: name-account-class-constants

## What was implemented
Added private consts `ExpenseAccountClassPrefix` ("5") and `IncomeAccountClassPrefix` ("6") with a Czech Chart of Accounts comment in FinancialAnalysisService and replaced the four StartsWith literals in CalculatePeriodTotals. Bumped verified_at in the process doc and regenerated the process index.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` — consts + usages
- `docs/processes/calc-financial-overview.md` — verified_at bump

## Tests
Existing FinancialOverview tests (46) pass unchanged.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~FinancialOverview`

## Notes
Pure refactor, no behaviour change.

## PR Summary
Replaced magic account-class strings "5"/"6" in FinancialAnalysisService with named constants.

### Changes
- `FinancialAnalysisService.cs` — named constants for expense/income account class prefixes

## Status
DONE
