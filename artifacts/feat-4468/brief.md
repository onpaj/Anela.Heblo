## Module
MarketingInvoices

## Finding
`AddMetaAdsAdapter` and `AddGoogleAdsAdapter` register their `IMarketingTransactionSource` and `IRecurringJob` services unconditionally — no check is performed whether the required settings are actually configured before registering:

- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs:10–20`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs:10–22`

`appsettings.json` contains only placeholder values for `MetaAds:*` and `GoogleAds:*`. If either job is enabled (e.g. in staging, or accidentally in production after a config reset), it fires, calls the external API with empty/placeholder credentials, receives an HTTP error, and Hangfire retries it repeatedly with no upfront indication that the configuration is missing.

This is documented as a known quirk in `docs/processes/module-marketing-invoices.md` but is not tracked as a fixable issue. The comparable `AdsDatabase` adapter (ADR-008) avoids the same class of problem by gating registration on `AdSettingsGuard.IsConfigured` — this module has no equivalent guard.

## Why it matters
The jobs are disabled in production today, but the module is designed to be re-enabled (it is listed in the Recurring Jobs admin page). An operator enabling a job in an environment without secrets gets a sequence of silent failures and Hangfire retries rather than a clear "adapter not configured" signal at startup. The problem is invisible until the first job run, and the retry noise pollutes the job history.

## Suggested fix
Add an `IsConfigured()` helper to each adapter's settings class (checking that mandatory fields — `AccountId`/`AccessToken` for Meta, `DeveloperToken`/`CustomerId` for Google — are non-empty), then gate registration:

```csharp
public static IServiceCollection AddMetaAdsAdapter(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var settings = configuration.GetSection(MetaAdsSettings.ConfigurationKey).Get<MetaAdsSettings>();
    if (settings is null || !settings.IsConfigured())
        return services; // log a warning if needed

    services.Configure<MetaAdsSettings>(configuration.GetSection(MetaAdsSettings.ConfigurationKey));
    services.AddHttpClient<MetaAdsTransactionSource>();
    services.AddScoped<IMarketingTransactionSource>(sp => sp.GetRequiredService<MetaAdsTransactionSource>());
    services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();
    return services;
}
```

Same pattern for Google Ads. The change is self-contained to the two adapter extensions — no handler, service or domain code changes needed.

---
_Filed by daily arch-review routine on 2026-10-10._