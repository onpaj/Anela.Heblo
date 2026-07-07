## Module
Bank

## Finding
`IBankStatementImportRepository` defines three methods without a `CancellationToken` parameter, while every other method on the same interface accepts one:

- `GetByIdAsync(int id)` — `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs:15`
- `AddAsync(BankStatementImport bankStatement)` — line 16
- `UpdateAsync(BankStatementImport bankStatement)` — line 25

Their implementations in `BankStatementImportRepository.cs` (lines 80–99, 128–141) call `FindAsync`, `SaveChangesAsync` without propagating any cancellation signal.

`ImportBankStatementHandler` calls `AddAsync` and `UpdateAsync` inside a per-statement processing loop (`ImportBankStatementHandler.cs:185–186`) and holds a cancellation token from the MediatR pipeline that it cannot pass through. `GetBankStatementByIdHandler` similarly cannot propagate its `cancellationToken` to `GetByIdAsync`.

## Why it matters
Violates interface consistency: every other method on the same interface is cancellable. In an import run that processes many statements against a slow DB or external service, a client request cancellation cannot interrupt individual inserts/updates. The asymmetry also makes the contract harder to mock uniformly in tests.

## Suggested fix
Add `CancellationToken cancellationToken = default` to `GetByIdAsync`, `AddAsync`, and `UpdateAsync` in both `IBankStatementImportRepository` and `BankStatementImportRepository`. Pass the token to `FindAsync(new object[] { id }, cancellationToken)` and `SaveChangesAsync(cancellationToken)`.

---
_Filed by daily arch-review routine on 2026-07-01._
