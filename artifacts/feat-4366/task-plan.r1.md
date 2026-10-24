# Plan: Named account-class constants

### task: name-account-class-constants
**Files:** `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

- [ ] Add `ExpenseAccountClassPrefix` ("5") and `IncomeAccountClassPrefix` ("6") private consts with the Czech Chart of Accounts comment, beside the existing consts.
- [ ] Replace the four `StartsWith("5")` / `StartsWith("6")` literals in `CalculatePeriodTotals`.
- [ ] Run `dotnet build` and `dotnet format`; run the FinancialOverview tests (`dotnet test --filter FinancialOverview`) and confirm they pass unchanged.
- [ ] Bump `verified_at` in `docs/processes/calc-financial-overview.md` and run `python3 scripts/process-docs/check.py index`.
