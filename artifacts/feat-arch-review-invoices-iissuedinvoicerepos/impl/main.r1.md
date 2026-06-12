All three tasks complete. Here's the output summary:

---

# Implementation: Remove Dead Query Methods from `IIssuedInvoiceRepository`

## What was implemented

Removed 5 unused `Find*` query methods from `IIssuedInvoiceRepository` (interface declarations + XML doc comments), their EF Core implementations in `IssuedInvoiceRepository`, and the 7 unit tests + 1 orphan private helper that exercised them. Pure deletion — no behavior changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` — removed 5 Find* method declarations with XML doc comments; 4 retained members remain
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` — removed 5 Find* method bodies; all retained members and `using` directives untouched
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — removed 7 `[Fact]` methods + `SetLastSyncTime` private helper; `CreateTestSyncData` and 10 retained test methods untouched

## Tests

All 10 remaining tests in `IssuedInvoiceRepositoryTests` pass. Full backend suite passes with 0 errors. Test count dropped by exactly 7 from baseline (as expected).

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/Anela.Heblo.sln --no-build
grep -rnE "FindBySyncStatusAsync|FindByInvoiceDateRangeAsync|FindByCustomerNameAsync|FindWithCriticalErrorsAsync|FindStaleInvoicesAsync" backend/
# → no output expected
```

## Notes

- Commit `ebb10632` on branch `feat-arch-review-invoices-iissuedinvoicerepos`, 253 lines deleted, 0 added
- One pre-existing build warning (unrelated to this change) was present before and after — not introduced by this PR
- `docs/superpowers/plans/2026-06-11-remove-dead-issuedinvoicerepository-find-methods.md` remains untracked (not committed per surgical-diff requirement)

## PR Summary

Removed 5 speculative `Find*` query methods from `IIssuedInvoiceRepository` that had no production callers — all live filtering already flows through `GetPaginatedAsync`. This narrows the interface contract to the 4 methods actually used, eliminating YAGNI surface area that forced any test double or alternative implementation to stub dead APIs.

The change is a pure deletion across 3 files: interface declarations, EF Core implementations, and the unit tests that covered the removed methods. The `SetLastSyncTime` reflection-based helper (only used by the now-deleted stale-invoice test) was removed alongside. Build, formatting, and full test suite all clean.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` — removed 5 `Find*` declarations and their XML doc-comment blocks
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` — removed 5 `Find*` implementation bodies
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — removed 7 `[Fact]` test methods and the `SetLastSyncTime` private helper

## Status
DONE