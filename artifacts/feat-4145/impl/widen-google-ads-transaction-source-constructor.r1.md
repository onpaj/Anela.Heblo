# Implementation: widen-google-ads-transaction-source-constructor

## What was implemented
Changed `GoogleAdsTransactionSource`'s constructor from `internal` to `public`, matching
`MetaAdsTransactionSource`'s pattern, and simplified the DI registration in
`GoogleAdsAdapterServiceCollectionExtensions` to the standard `AddScoped<GoogleAdsTransactionSource>()`
overload instead of a manual factory lambda.

## Deviation from the task plan (required to compile)
The task plan (and `spec.r1.md`'s Out of Scope section) assumed only
`GoogleAdsTransactionSource.cs` and `GoogleAdsAdapterServiceCollectionExtensions.cs` needed to
change. Making the constructor `public` alone does not compile: C#'s accessibility-consistency
rule (CS0051/CS0050) requires every type referenced in a `public` member's signature to be at
least as accessible as that member. The constructor takes `IAccountBudgetFetcher` (was
`internal interface`), and `IAccountBudgetFetcher.FetchAsync` returns
`Task<IReadOnlyList<RawAccountBudget>>` (`RawAccountBudget` was `internal sealed record`). Both
had to be widened to `public` as a direct, mechanical consequence of widening the constructor —
this is not a discretionary scope expansion, it is required for FR-1 to compile at all.

- `IAccountBudgetFetcher`: `internal interface` → `public interface`
- `RawAccountBudget`: `internal sealed record` → `public sealed record`

No members, signatures, or logic changed on either type — accessibility modifier only. This does
not touch `SdkAccountBudgetFetcher` (the implementation stays as declared) or any other class.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` — constructor `internal` → `public` (FR-1)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` — replaced factory-lambda registration with `services.AddScoped<GoogleAdsTransactionSource>();` (FR-2)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/IAccountBudgetFetcher.cs` — `internal` → `public` (required for FR-1 to compile; see Deviation above)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/RawAccountBudget.cs` — `internal` → `public` (required for FR-1 to compile; see Deviation above)

## Tests
No new tests added (none required by spec — no logic change). Existing
`backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs` (5 tests)
run unmodified and pass.

## How to verify
```bash
cd backend
dotnet build ../Anela.Heblo.sln            # 0 errors
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GoogleAdsTransactionSourceTests"
# Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```
Full solution `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes needed.
Full `dotnet test` run: 110 pre-existing failures, all `System.ArgumentException: Docker is
either not running...` from Testcontainers-based integration tests (Postgres container
unavailable in this sandbox) — unrelated to this change, none touch GoogleAds/MetaAds/
MarketingInvoices. 7104 tests passed.

## Notes
The `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` entry in
`Anela.Heblo.Adapters.GoogleAds.csproj` was left in place per spec's Out of Scope guidance (not
strictly required to remove, harmless to keep).

## PR Summary
Widened `GoogleAdsTransactionSource`'s constructor from `internal` to `public` to match
`MetaAdsTransactionSource`'s pattern, and simplified its DI registration in
`GoogleAdsAdapterServiceCollectionExtensions` to the standard `AddScoped<T>()` overload instead of
a manual factory lambda that existed only to work around the internal constructor.

Making the constructor public required also widening `IAccountBudgetFetcher` (interface) and
`RawAccountBudget` (record) from `internal` to `public` — a mechanical consequence of C#'s
accessibility-consistency rule, since the public constructor's parameter type and its method's
return type must be at least as accessible as the constructor itself. No logic changed on either
type. No behavioral change anywhere in this PR.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` — constructor made public
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` — simplified DI registration
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/IAccountBudgetFetcher.cs` — made public (required for the above to compile)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/RawAccountBudget.cs` — made public (required for the above to compile)

## Status
DONE_WITH_CONCERNS
