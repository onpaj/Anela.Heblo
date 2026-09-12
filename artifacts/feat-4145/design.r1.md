# Design: GoogleAdsTransactionSource constructor visibility fix

## Component Design

### `GoogleAdsTransactionSource` (backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs)
- **Responsibility:** Unchanged — implements `IMarketingTransactionSource` for the Google Ads
  platform by fetching account budget rows via `IAccountBudgetFetcher` and mapping them to
  `MarketingTransaction` records.
- **Contract change:** Constructor accessibility widens from `internal` to `public`:
  ```csharp
  public GoogleAdsTransactionSource(
      IAccountBudgetFetcher fetcher,
      ILogger<GoogleAdsTransactionSource> logger)
  ```
  Parameter list, order, types, and body are unchanged. `Platform`, `PlatformName`, and
  `GetTransactionsAsync` are unchanged.

### `GoogleAdsAdapterServiceCollectionExtensions` (backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs)
- **Responsibility:** Unchanged — registers the Google Ads adapter's DI dependencies for
  `AddGoogleAdsAdapter(IServiceCollection, IConfiguration)`.
- **Contract change:** The `GoogleAdsTransactionSource` registration changes from a manual
  factory lambda to the standard container-resolved overload, mirroring
  `MetaAdsAdapterServiceCollectionExtensions.AddMetaAdsAdapter`:

  Before:
  ```csharp
  services.AddScoped<GoogleAdsTransactionSource>(sp =>
      new GoogleAdsTransactionSource(
          sp.GetRequiredService<IAccountBudgetFetcher>(),
          sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
  ```

  After:
  ```csharp
  services.AddScoped<GoogleAdsTransactionSource>();
  ```

  The following line, which already matches the MetaAds pattern, is unchanged:
  ```csharp
  services.AddScoped<IMarketingTransactionSource>(sp =>
      sp.GetRequiredService<GoogleAdsTransactionSource>());
  ```

No other component in the `Anela.Heblo.Adapters.GoogleAds` project changes. No component outside
this project (callers of `IMarketingTransactionSource`, `GoogleAdsInvoiceImportJob`,
`IAccountBudgetFetcher`/`SdkAccountBudgetFetcher`) is affected — the change is fully internal to
these two files and is invisible to every consumer of the `IMarketingTransactionSource` interface.

## Data Schemas
No data schema changes. `MarketingTransaction` (DTO shape), `RawAccountBudget`, and
`GoogleAdsSettings` are all unaffected by this change — no field, type, or serialization format
is touched.
