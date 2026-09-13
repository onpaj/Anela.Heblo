### task: widen-google-ads-transaction-source-constructor
**Status:** PASS

## Review

**Spec compliance:**
- FR-1 (public constructor): met — `GoogleAdsTransactionSource`'s constructor is `internal` → `public`.
- FR-2 (simplified DI registration): met — `services.AddScoped<GoogleAdsTransactionSource>();` replaces the factory lambda; the `IMarketingTransactionSource` registration line is untouched, matching the MetaAds pattern.
- FR-3 (no regressions): met — `GoogleAdsTransactionSourceTests` (5/5) pass unmodified.
- NFR-1 (no behavioral change): met — `GetTransactionsAsync` and all mapping logic are byte-for-byte unchanged; only accessibility modifiers and the DI call shape changed.
- NFR-2 (build/format integrity): met — `dotnet build` on the full solution: 0 errors; `dotnet format --verify-no-changes`: no changes needed.

**Deviation from spec's Out-of-Scope list (widening `IAccountBudgetFetcher` and `RawAccountBudget` to `public`):**
Verified as necessary, not optional. C#'s accessibility-consistency rule requires a `public`
member's parameter and return types to be at least as accessible as the member itself
(`CS0051`/`CS0050`). Reproduced by reverting just these two files locally: build fails with
exactly those two errors. The spec's assumption that only two files needed changing was
incorrect — this is the correct, minimal fix, not scope creep. Both widened types had only their
access modifier changed; no members, signatures, or logic touched. `SdkAccountBudgetFetcher`
(the implementation) was correctly left untouched, matching the spec's remaining Out-of-Scope
items.

**Correctness:** No logic errors. The diff is a pure accessibility/DI-wiring change across four
files, all within the same project.

**Full test suite:** 110 failures, all `System.ArgumentException: Docker is either not
running...` from Testcontainers-based integration tests — a pre-existing sandbox limitation
(no Docker daemon available), unrelated to this change. None of the failing tests reference
GoogleAds, MetaAds, or MarketingInvoices. 7104 tests passed.

## Docs to Update
None. This is an internal accessibility/DI-wiring change with no public API, CLI, or
operational-behavior impact.

## Overall Notes
Clean, minimal, well-justified implementation. The developer's impl artifact documents the
required deviation clearly.
