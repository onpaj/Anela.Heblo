## Module
Bank

## Finding
`BankStatementStatisticsSourceAdapter` in `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` (lines 1–105) directly injects `ApplicationDbContext` from `Anela.Heblo.Persistence` and executes EF Core LINQ queries against `_dbContext.BankStatements` inline. This bypasses the repository abstraction entirely — the Bank module already has `IBankStatementImportRepository` for exactly this purpose.

The pattern is especially visible at lines 34–53 and 57–77, where two near-identical LINQ query + gap-fill blocks run directly against the DbContext.

## Why it matters
The project guidelines state "Generic repository abstraction in Xcc, implementation in Persistence layer" as the required data-access pattern. Going around it means:
- Database access logic is split between the repository and this adapter, making queries harder to find, test, and optimise.
- The adapter carries an implicit EF Core dependency inside the Application layer that should be hidden behind an interface.
- The two near-identical query blocks (one per `BankStatementDateType` branch) are a duplication risk — a schema change must be applied in both.

## Suggested fix
Add a `GetDailyStatisticsAsync(DateTime startDate, DateTime endDate, BankStatementDateType dateType, CancellationToken ct)` method to `IBankStatementImportRepository` (Domain layer), implement it in `BankStatementImportRepository` (Persistence layer), and inject `IBankStatementImportRepository` into `BankStatementStatisticsSourceAdapter` instead of `ApplicationDbContext`. The gap-filling loop (lines 79–103) can stay in the adapter since it's pure in-memory logic.

---
_Filed by daily arch-review routine on 2026-06-27._
