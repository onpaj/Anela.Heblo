# Design: Config guard for MarketingInvoices adapters

## Component Design
**MetaAdsSettings**: add `public bool IsConfigured() => AdSettingsGuard.IsConfigured(AccountId, AccessToken);` and `internal IReadOnlyList<string> UnusableSettings()` returning names of the two properties failing the guard.

**GoogleAdsSettings**: add `public bool IsInvoiceImportConfigured()` over `CustomerId, DeveloperToken, OAuth2ClientId, OAuth2ClientSecret, OAuth2RefreshToken`, plus the same style of names-only helper.

**AddMetaAdsAdapter** (shape; Google identical with its own names/services):
```csharp
var section = configuration.GetSection(MetaAdsSettings.ConfigurationKey);
var settings = section.Get<MetaAdsSettings>() ?? new MetaAdsSettings();
if (!settings.IsConfigured())
{
    var unusable = settings.UnusableSettings();
    // partial = at least one real value but not all
    if (unusable.Count > 0 && unusable.Count < TotalRequired)
        Console.Error.WriteLine($"[MetaAds] Meta Ads invoice import is partly configured and stays unregistered. Unusable settings: {string.Join(", ", unusable)}.");
    return services;
}
services.Configure<MetaAdsSettings>(section);
... existing four registrations unchanged ...
```
Registrations after the guard stay byte-for-byte as today. Google keeps `IAccountBudgetFetcher`, `GoogleAdsTransactionSource`, `IMarketingTransactionSource`, `IRecurringJob`.

**Tests** (xUnit + FluentAssertions, building `ServiceCollection` from in-memory `ConfigurationBuilder`, as in `GoogleAds.Tests/RegistrationTests.cs`):
- placeholders -> `services` empty (Count == 0);
- each required value blank/placeholder -> empty (Theory);
- all real -> contains descriptors for `IMarketingTransactionSource` and `IRecurringJob`;
- missing section -> no throw, empty;
- partial config message contains setting names and not the secret value (capture `Console.Error` with `Console.SetError`, restore in finally; mark test class non-parallel if shared).

## Data Schemas
None. Configuration keys unchanged: `MetaAds:AccountId`, `MetaAds:AccessToken`; `GoogleAds:CustomerId`, `DeveloperToken`, `OAuth2ClientId`, `OAuth2ClientSecret`, `OAuth2RefreshToken`.
