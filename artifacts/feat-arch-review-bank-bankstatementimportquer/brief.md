## Module
Bank

## Finding
`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` defines a `BankStatementImportQueryDto` class (with `Id`, `StatementDate`, `ImportDate`, `Skip`, `Take`, `OrderBy`, `Ascending` properties) that is never referenced anywhere in the codebase. A project-wide search confirms the only occurrence of the type name is the file that declares it.

The query contract that is actually used is `GetBankStatementListRequest` (in `UseCases/GetBankStatementList/`), which supersedes this DTO.

## Why it matters
Dead types in `Contracts/` create confusion about which contract is authoritative for callers. The incomplete field set (`TransferId`, `Account`, `DateFrom`, `DateTo`, `ErrorsOnly` are absent) makes this class an incorrect partial view of the real request shape, which could mislead future developers.

## Suggested fix
Delete `BankStatementImportQueryDto.cs`. No other file references it, so the deletion is safe.

---
_Filed by daily arch-review routine on 2026-06-03._