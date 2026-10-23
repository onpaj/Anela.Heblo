# Architecture Review: Config guard for MarketingInvoices adapters

## Skip Design: true
Backend-only DI change, no UI.

## Architectural Fit Assessment
Fits the established pattern in this repo: `AddGoogleAdsMarketingAds` (ADR-008 style) gates registration on `AdSettingsGuard.IsConfigured` and writes a names-only warning to stderr. The change is local to two adapter composition extensions. `Program.cs` calls stay as is. Consumers (`IEnumerable<IMarketingTransactionSource>`, `GetServices<IRecurringJob>()`) tolerate zero registrations; verified in `ImportMarketingInvoicesHandler`, `HangfireRecurringJobScheduler`, `RecurringJobDiscoveryService`.

## Proposed Architecture

### Component Overview
```
Program.cs -> AddMetaAdsAdapter(config)
                 bind MetaAds section -> MetaAdsSettings.IsConfigured()?
                    no  -> (partial? stderr warning) return services
                    yes -> Configure options, HttpClient, source, job
           -> AddGoogleAdsAdapter(config)   (same shape, 5 required values)
           -> AddGoogleAdsMarketingAds(config)  (untouched)
```

### Key Design Decisions

#### Decision 1: Guard implementation
**Options considered:** (a) plain `IsNullOrWhiteSpace` as in the issue's sketch; (b) reuse `AdSettingsGuard`.
**Chosen approach:** (b). appsettings.json holds non-empty placeholders (`act_XXXXXXXXX`, `XXX-XXX-XXXX`, `-- stored in secrets.json --`), so a blank-only check would still register the jobs. The issue sketch is insufficient on its own.
**Rationale:** Identical semantics to the sibling registration; already tested.

#### Decision 2: Separate Google predicate for invoice import
**Chosen approach:** Add a new check on `GoogleAdsSettings` (`IsInvoiceImportConfigured()`) requiring `DeveloperToken` plus the four existing credentials; do not reuse or edit `GoogleAdsMarketingAdsServiceCollectionExtensions.IsConfigured` (it deliberately omits the developer token and adds numeric-id rules). Keep `CustomerId` numeric-format validation out; `SdkAccountBudgetFetcher` normalizes ids itself. (The issue's suggested pair `DeveloperToken`/`CustomerId` is too weak: the fetcher also needs the OAuth triple.)

#### Decision 3: Signalling
`Console.Error.WriteLine("[MetaAds] ...")` / `"[GoogleAds] ..."` for the partial-configuration case only, naming settings not values, matching the existing precedent. No `ILogger` available at registration time.

#### Decision 4: Admin visibility trade-off
Unconfigured jobs vanish from the Recurring Jobs admin page and trigger endpoint. Accepted: that is the intent (cannot enable something that cannot work); document it.

## Implementation Guidance

### Directory / Module Structure
- Edit `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs`, `MetaAdsAdapterServiceCollectionExtensions.cs`.
- Edit `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsSettings.cs`, `GoogleAdsAdapterServiceCollectionExtensions.cs`.
- Add tests: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MetaAdsAdapterRegistrationTests.cs` and `GoogleAdsAdapterRegistrationTests.cs` (the Tests project has InternalsVisibleTo from both adapters and already hosts MarketingInvoices tests), or add to `Anela.Heblo.Adapters.GoogleAds.Tests` for Google, following `RegistrationTests.cs`.
- Edit `docs/processes/module-marketing-invoices.md`.

### Interfaces and Contracts
- `public bool MetaAdsSettings.IsConfigured()` => `AdSettingsGuard.IsConfigured(AccountId, AccessToken)`.
- `public bool GoogleAdsSettings.IsInvoiceImportConfigured()` => `AdSettingsGuard.IsConfigured(CustomerId, DeveloperToken, OAuth2ClientId, OAuth2ClientSecret, OAuth2RefreshToken)`. Beware: these are instance methods on option classes bound via `Get<T>()`; fine.
- Bind with `section.Get<TSettings>() ?? new TSettings()`; on success call `services.Configure<T>(section)` as today.

### Data Flow
Startup: bind section, evaluate guard, register or skip. Runtime unchanged when configured.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Real secret containing "XXX" treated as unconfigured (known guard limitation) | Low | Documented in AdSettingsGuard; accept |
| Key Vault secrets load after the guard runs | Medium | Verify KV configuration is added to `builder.Configuration` before the adapter calls (the MarketingAds guard relies on the same ordering, so it holds); implementer confirms in Program.cs |
| Hangfire/recurring-job DB rows for now-absent jobs | Low | Check `RecurringJobSeeder`/discovery tolerate orphan configuration rows; no deletion needed |
| Tests that resolve `IRecurringJob` expecting these jobs | Low | grep tests for `MetaAdsInvoiceImportJob`/`GoogleAdsInvoiceImportJob`; adjust only if affected |
| Cross-module reference to MarketingAds `AdSettingsGuard` | Low | Already done by GoogleAds adapter; moving it is out of scope |

## Specification Amendments
Google guard must include the OAuth triple (not only DeveloperToken/CustomerId). Guard must use `AdSettingsGuard`, not blank checks. Message only for partial configuration.

## Prerequisites
None. No migrations or config changes.
