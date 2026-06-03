All 6 implementation commits are on the branch. Here is the implementation summary:

---

# Implementation: Consolidate Bank Import Job Classes

## What was implemented

Eliminated ~180 lines of duplicated boilerplate across `ComgateCzkImportJob`, `ComgateEurImportJob`, and `ShoptetPayImportJob` by extracting an abstract `BankImportJobBase` template class. Replaced all hardcoded account-name magic strings with compile-safe constants on `BankAccountNames`. Added full TDD test coverage that did not previously exist.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/BankAccountNames.cs` — public static class with 3 `const string` account-name constants
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobParameters.cs` — `internal sealed record` snapshot of one invocation's parameters
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs` — abstract `IRecurringJob` template: enabled-check → log → `GetParameters()` once → dispatch → log → catch/rethrow. Uses `ILoggerFactory.CreateLogger(GetType())` to preserve per-subclass log category
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs` — reduced to 31 lines (from 59): sealed, inherits base, `Metadata` + `GetParameters()` only
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateEurImportJob.cs` — same shape, 31 lines
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ShoptetPayImportJob.cs` — same shape, 31 lines
- `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs` — 8 tests covering the template (disabled early-return, request construction, single GetParameters() call, CancellationToken propagation, error logging + rethrow, 3 null-guard checks)
- `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJobTests.cs` — metadata + parameter assertions including literal string guard
- `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/ComgateEurImportJobTests.cs` — same
- `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/ShoptetPayImportJobTests.cs` — same
- `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobDiscoveryTests.cs` — 3 tests: abstract base excluded from scan, all 3 concrete jobs included, `IsAbstract` defence check

## Tests

17 new tests total across 5 test files. All 60 Bank tests pass (including pre-existing handler tests). Full test suite: 4286 pass, 32 pre-existing Docker/Postgres integration failures unrelated to this change.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank"

grep -rn '"ComgateCZK"' backend/src/Anela.Heblo.Application/Features/Bank/
# → only BankAccountNames.cs:5
```

## Notes

- `GetParameters()` uses `internal abstract` (not `protected abstract` as spec originally said) because the return type `BankImportJobParameters` is `internal`. This is correct given the existing `InternalsVisibleTo("Anela.Heblo.Tests")` in the project. All concrete overrides use `internal override`.
- `dotnet format` found no changes needed after the refactor.
- The 32 pre-existing test failures are Docker/Postgres environment issues unrelated to this change.

## PR Summary

Extracted `BankImportJobBase` abstract template to eliminate ~180 lines of duplicated `ExecuteAsync` boilerplate across three bank import jobs, and replaced all hardcoded account-name string literals with constants on `BankAccountNames` so a typo becomes a compile error rather than a silent runtime failure. Also fixes an existing bug where `ComgateCzkImportJob` and `ComgateEurImportJob` dropped the `CancellationToken` on `IsJobEnabledAsync`; the base class now propagates it to both `IsJobEnabledAsync` and `IMediator.Send`.

### Changes
- `BankAccountNames.cs` — new: `public static class` with 3 wire-contract constants
- `BankImportJobParameters.cs` — new: `internal sealed record` snapshot per invocation
- `BankImportJobBase.cs` — new: abstract template owning the shared `ExecuteAsync` flow
- `ComgateCzkImportJob.cs`, `ComgateEurImportJob.cs`, `ShoptetPayImportJob.cs` — reduced from 59-60 lines to 31 lines each; now sealed subclasses overriding only `Metadata` and `GetParameters()`
- `BankImportJobBaseTests.cs`, `ComgateCzkImportJobTests.cs`, `ComgateEurImportJobTests.cs`, `ShoptetPayImportJobTests.cs`, `BankImportJobDiscoveryTests.cs` — new: 17 tests covering the template behavior, per-job parameters, and discovery filter

## Status
DONE