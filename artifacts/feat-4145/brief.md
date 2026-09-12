## Module
MarketingInvoices (Adapters/GoogleAds)

## Finding
`backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` line 17 declares the constructor `internal`:

```csharp
internal GoogleAdsTransactionSource(
    IAccountBudgetFetcher fetcher,
    ILogger<GoogleAdsTransactionSource> logger)
```

Meanwhile the equivalent `MetaAdsTransactionSource` (`Adapters/MetaAds/MetaAdsTransactionSource.cs`) has a standard `public` constructor. The `internal` modifier has two visible consequences:

1. **Unit tests cannot directly instantiate `GoogleAdsTransactionSource`** from the test project (which is outside the adapter assembly), preventing isolation tests like those that exist for `MetaAdsTransactionSource`.
2. **DI registration uses a manual factory lambda** (`new GoogleAdsTransactionSource(...)`) in `GoogleAdsAdapterServiceCollectionExtensions.cs` lines 16–19, bypassing the DI container's constructor resolution — a workaround that exists solely because of the `internal` modifier.

The type itself is `public`, so `internal` on the constructor provides no meaningful encapsulation — any code in the same assembly can still reference the class.

## Why it matters
- Prevents writing parallel unit-test coverage for `GoogleAdsTransactionSource` without reflection or `InternalsVisibleTo`.
- Inconsistency between the two adapters increases cognitive overhead when navigating the codebase.

## Suggested fix
Change the constructor from `internal` to `public`:

```csharp
public GoogleAdsTransactionSource(
    IAccountBudgetFetcher fetcher,
    ILogger<GoogleAdsTransactionSource> logger)
```

Then simplify the DI registration to use the standard `AddScoped<>` overload (matching the MetaAds pattern):

```csharp
services.AddScoped<GoogleAdsTransactionSource>();
services.AddScoped<IMarketingTransactionSource>(sp =>
    sp.GetRequiredService<GoogleAdsTransactionSource>());
```

No logic change required.

---
_Filed by daily arch-review routine on 2026-09-11._
