Both reviews pass. The implementation is complete and committed.

---

# Implementation: Remove unused `BankStatementImportQueryDto` dead code

## What was implemented

Deleted the orphaned `BankStatementImportQueryDto` class from the Bank module's `Contracts/` directory and corrected the one documentation file that still referenced it (`docs/features/comgate.md`). The active query contract `GetBankStatementListRequest` and all related handlers, validators, and controllers were left completely untouched.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` — **deleted** via `git rm`
- `docs/features/comgate.md` — "Query API: GET /api/bank-statements" section rewritten to reference `GetBankStatementListRequest` with all 11 actual properties and their defaults

## Tests

No new tests were required (dead-code deletion). The full backend test suite was run: 4,373 tests pass. The 38 pre-existing failures are Docker-dependent integration tests unrelated to this change.

## How to verify

```bash
# Confirm DTO file is gone
ls backend/src/Anela.Heblo.Application/Features/Bank/Contracts/

# Confirm zero references anywhere
grep -rn "BankStatementImportQueryDto" .

# Confirm build still passes
dotnet build backend/Anela.Heblo.sln

# Confirm doc was updated correctly
grep -A 15 "Query API: GET /api/bank-statements" docs/features/comgate.md
```

## Notes

The spec's "Out of Scope" clause ("no docs reference `BankStatementImportQueryDto`") was factually incorrect — `docs/features/comgate.md:176` did reference it. Per the arch-review's Decision 3, the doc fix was included in the same atomic commit to avoid leaving a reference to a deleted type. The planning document `docs/superpowers/plans/2026-06-03-remove-bank-statement-import-query-dto.md` still mentions the type name by design (it's historical context).

## PR Summary

Removes the unused `BankStatementImportQueryDto` class from `Features/Bank/Contracts/` and corrects the stale documentation in `docs/features/comgate.md`. The DTO had zero references outside its own file; the authoritative query contract `GetBankStatementListRequest` (a strict superset) continues unchanged. The doc fix is included in the same commit to prevent any state where the docs reference a deleted type.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` — deleted via `git rm`
- `docs/features/comgate.md` — "Query parametry" subsection updated from `BankStatementImportQueryDto` (3 properties) to `GetBankStatementListRequest` (all 11 actual properties with defaults)

## Status

DONE