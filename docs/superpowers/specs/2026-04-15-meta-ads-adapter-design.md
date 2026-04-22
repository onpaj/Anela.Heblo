# Design Spec: Meta Ads Transaction Fetching Adapter

**Issue:** #607
**Date:** 2026-04-15
**Depends on:** #606 (shared core — `IMarketingTransactionSource`, `MarketingInvoiceImportService`, persistence)

---

## Summary

Create `Anela.Heblo.Adapters.MetaAds` — a .NET 8 class library that fetches billing transaction data from Meta (Facebook) Ads Graph API and persists them via the shared marketing invoice import core.

---

## Architecture

New project at `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/`, following the Comgate adapter pattern (Polly resilience) and ShoptetApi adapter pattern (project structure).

**Key design decision:** `MarketingInvoiceImportService` is not registered in DI (the shared core module only registers the repository). `MetaAdsInvoiceImportJob` instantiates `MarketingInvoiceImportService` directly, injecting the concrete `MetaAdsTransactionSource`. This avoids DI conflicts when the Google Ads adapter is later registered with its own `IMarketingTransactionSource` implementation.

---

## Project Structure

```
backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/
├── Anela.Heblo.Adapters.MetaAds.csproj
├── MetaAdsSettings.cs
├── MetaAdsTransactionSource.cs
├── MetaAdsInvoiceImportJob.cs
└── MetaAdsAdapterServiceCollectionExtensions.cs
```

---

## Components

### `Anela.Heblo.Adapters.MetaAds.csproj`

- Target: `net8.0`, nullable enabled, implicit usings
- Package references: `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Polly`, `Polly.Extensions`
- Project references: `Anela.Heblo.Domain`, `Anela.Heblo.Application`

### `MetaAdsSettings.cs`

```csharp
public class MetaAdsSettings
{
    public const string ConfigurationKey = "MetaAds";
    public string AdAccountId { get; set; } = string.Empty;   // e.g. "act_123456789"
    public string AccessToken { get; set; } = string.Empty;   // System User token — secrets.json / Key Vault
    public string ApiVersion { get; set; } = "v21.0";
}
```

Configuration in `appsettings.json` (non-secret values only):
```json
{
  "MetaAds": {
    "AdAccountId": "act_XXXXXXXXX",
    "ApiVersion": "v21.0"
  }
}
```
`AccessToken` goes in `secrets.json` / Azure Key Vault only.

### `MetaAdsTransactionSource.cs`

Implements `IMarketingTransactionSource`.

- `Platform` → `"MetaAds"`
- Endpoint: `GET https://graph.facebook.com/{ApiVersion}/{AdAccountId}/transactions`
- Query params: `fields=id,time,amount,currency,payment_type`, `access_token={token}`
- Pagination: follow `paging.next` cursor URL until absent
- Amount mapping: integer value from API (cents) → `decimal` by dividing by 100
- `TransactionDate`: parsed from Unix timestamp in `time` field (`DateTimeOffset.FromUnixTimeSeconds`)
- `Description`: populated from `payment_type` field (e.g., `"THRESHOLD"`)
- `RawData`: set to the raw JSON object string for the transaction (not persisted, diagnostic only)
- Client-side date filter: only return transactions where `TransactionDate >= from && TransactionDate <= to`

**Resilience:** Polly `ResiliencePipeline` with retry on HTTP 429 (`TooManyRequests`):
```
MaxRetryAttempts = 3, Delay = 2s, BackoffType = Exponential, UseJitter = true
```
No circuit breaker (Meta is not on a critical payment path).

**Error handling:** `HttpRequestException` for non-429, non-success responses propagates to the caller (the import service logs per-transaction errors).

### `MetaAdsInvoiceImportJob.cs`

Implements `IRecurringJob`.

**Metadata:**
```csharp
JobName        = "meta-ads-invoice-import"
DisplayName    = "Meta Ads Invoice Import"
Description    = "Fetches billing transactions from Meta Ads Graph API (7-day lookback)"
CronExpression = "0 6,18 * * *"   // 6 AM and 6 PM
TimeZoneId     = "Europe/Prague"  (default from RecurringJobMetadata)
DefaultIsEnabled = true
```

**Constructor injects:**
- `MetaAdsTransactionSource source` (concrete — not via interface, avoids DI conflict)
- `IImportedMarketingTransactionRepository repository`
- `ILogger<MarketingInvoiceImportService> logger`
- `IRecurringJobStatusChecker statusChecker`

**`ExecuteAsync` logic:**
1. Check `statusChecker.IsJobEnabledAsync` — return early if disabled
2. Compute window: `from = DateTime.UtcNow.AddDays(-7)`, `to = DateTime.UtcNow`
3. Instantiate `new MarketingInvoiceImportService(source, repository, logger)`
4. Call `service.ImportAsync(from, to, cancellationToken)`
5. Log result (Imported / Skipped / Failed counts)
6. Rethrow unexpected exceptions after logging

### `MetaAdsAdapterServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddMetaAdsAdapter(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<MetaAdsSettings>(configuration.GetSection(MetaAdsSettings.ConfigurationKey));
    services.AddHttpClient<MetaAdsTransactionSource>();
    services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();
    return services;
}
```

### `Program.cs` change

Add after `AddComgateAdapter`:
```csharp
builder.Services.AddMetaAdsAdapter(builder.Configuration);
```

### Solution file

Add `Anela.Heblo.Adapters.MetaAds.csproj` to `backend/Anela.Heblo.sln`.

### Test project

Add project reference to `Anela.Heblo.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.MetaAds\Anela.Heblo.Adapters.MetaAds.csproj" />
```

---

## API Response Shape

```json
{
  "data": [
    {
      "id": "1234567890",
      "time": 1744300800,
      "amount": 150000,
      "currency": "CZK",
      "payment_type": "THRESHOLD"
    }
  ],
  "paging": {
    "cursors": { "before": "...", "after": "..." },
    "next": "https://graph.facebook.com/..."
  }
}
```

The `next` field in `paging` is absent on the last page.

---

## Tests

Location: `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs`

| # | Test | Description |
|---|------|-------------|
| 1 | `GetTransactionsAsync_ValidResponse_ParsesFieldsCorrectly` | Single transaction in response → assert `TransactionId`, `Amount`, `TransactionDate`, `Currency`, `Description` mapped correctly |
| 2 | `GetTransactionsAsync_Amount_ConvertedFromCentsToDecimal` | `amount: 150000` → `1500.00m` |
| 3 | `GetTransactionsAsync_Pagination_AllPagesCollected` | First response has `paging.next`, second has none → transactions from both pages returned |
| 4 | `GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt` | First call returns HTTP 429, second returns 200 → result returned (not thrown) |

Tests use `MockHttpMessageHandler` (manual `DelegatingHandler` pattern, no extra package needed).

---

## Out of Scope

- Circuit breaker (Meta is not critical-path)
- `Retry-After` header parsing (exponential backoff is sufficient)
- Token expiry detection (monitor manually via Meta Business Manager alerts)
- FlexiBee sync (future work, tracked in epic)
- UI for viewing imported transactions (future work)
