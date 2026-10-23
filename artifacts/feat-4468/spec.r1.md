# Specification: Config guard for MetaAds and GoogleAds invoice-import adapter DI registration

## Summary
`AddMetaAdsAdapter` and `AddGoogleAdsAdapter` register their `IMarketingTransactionSource` and `IRecurringJob` services unconditionally, while `appsettings.json` ships only placeholders. Gate registration on real configuration, reusing the existing `AdSettingsGuard`, so an unconfigured environment is inert and logs a clear startup message instead of producing Hangfire retry noise.

## Background
Issue #4468 (arch-review, MarketingInvoices). Both extensions live in `backend/src/Adapters/`. The comparable MarketingAds registration (`AddGoogleAdsMarketingAds`) already gates on `AdSettingsGuard.IsConfigured` and logs a partial-configuration warning. `AdSettingsGuard` (Application layer, `Features/MarketingAds/Contracts`) rejects blank, `--` prefixed, `XXX`-containing and `your-` prefixed values. Both adapter projects already reference the Application project.

Facts verified in code:
- Meta settings: `AccountId`, `AccessToken` (`AccessToken` is absent from appsettings.json; `AccountId` = `act_XXXXXXXXX`).
- Google invoice import uses `SdkAccountBudgetFetcher`, which needs `DeveloperToken`, `OAuth2ClientId`, `OAuth2ClientSecret`, `OAuth2RefreshToken`, `CustomerId`. appsettings has `CustomerId` = `XXX-XXX-XXXX` and `-- stored in secrets.json --` for the others.
- `IRecurringJob` instances are discovered via `GetServices<IRecurringJob>()` (Hangfire scheduler, seeder, admin list/trigger handlers). `ImportMarketingInvoicesHandler` takes `IEnumerable<IMarketingTransactionSource>`, so zero registrations is safe.
- `Program.cs` calls both extensions at lines 124-125; `AddGoogleAdsMarketingAds` is registered separately and must be unaffected.

## Functional Requirements

### FR-1: Meta Ads registration guard
`AddMetaAdsAdapter` registers nothing (returns `services` unchanged) unless `MetaAdsSettings.IsConfigured()` is true: `AccountId` and `AccessToken` both pass `AdSettingsGuard.IsConfigured`.
**Acceptance criteria:**
- With repository placeholders (`act_XXXXXXXXX`, no token) no `IMarketingTransactionSource`, `IRecurringJob`, `MetaAdsSettings` options or `MetaAdsTransactionSource` http client is registered.
- With real-looking `AccountId` and `AccessToken`, all four registrations from the current code are present.
- Missing `MetaAds` section does not throw.

### FR-2: Google Ads registration guard
`AddGoogleAdsAdapter` registers nothing unless `GoogleAdsSettings` has real `DeveloperToken`, `CustomerId`, `OAuth2ClientId`, `OAuth2ClientSecret`, `OAuth2RefreshToken` (all via `AdSettingsGuard`). Expose as an invoice-import-specific check (e.g. `IsInvoiceImportConfigured()` on the settings class, or an internal static method on the extension class) so it does not alter the semantics of the existing MarketingAds `IsConfigured` (which intentionally does not require `DeveloperToken`).
**Acceptance criteria:**
- Placeholders from appsettings.json: nothing registered.
- Each of the five values blank or placeholder individually: nothing registered.
- All five real: `IAccountBudgetFetcher`, `GoogleAdsTransactionSource`, `IMarketingTransactionSource`, `IRecurringJob` registered and `GoogleAdsSettings` options configured.
- The MarketingAds registration (`AddGoogleAdsMarketingAds`) behaviour is unchanged.

### FR-3: Startup signal
When an adapter is skipped, emit a single-line, secret-free message naming the adapter and the settings that are unusable (names only, never values). Follow the existing precedent of `Console.Error.WriteLine("[GoogleAds] ...")` (no logger exists at DI-registration time). Do not warn when nothing at all is configured? Decision: emit a message only for a partially configured set (some values real, others not), mirroring `DescribeMisconfiguration`; fully unconfigured stays silent to avoid noise in dev/CI.
**Acceptance criteria:**
- Partial configuration yields a message listing unusable setting names and no values.
- Fully placeholder configuration yields no message.

### FR-4: Documentation
Update `docs/processes/module-marketing-invoices.md`: replace the "no config gate" known quirk with the new behaviour (adapters/jobs are only registered when configured; consequently unconfigured jobs are absent from the Recurring Jobs admin page), bump `verified_at`, and run `python3 scripts/process-docs/check.py index`.

## Non-Functional Requirements

### NFR-1: Safety
App must still boot with any config combination; no exceptions from the guard. No secret values in output.

### NFR-2: Scope
Only the two adapter extension files, their settings classes (or a helper beside them), tests, and the process doc change. No handler, service, domain, or Program.cs changes.

## Data Model
None. No migrations.

## API / Interface Design
New public/internal helpers: `MetaAdsSettings.IsConfigured()`; Google equivalent for invoice import. Internal helper for unusable-setting names. Tests need `InternalsVisibleTo` for the MetaAds project (`Anela.Heblo.Tests` already present; a dedicated MetaAds test project does not exist).

## Dependencies
`AdSettingsGuard` in `Anela.Heblo.Application.Features.MarketingAds.Contracts` (a MarketingAds-module type used from a MarketingInvoices adapter; acceptable because the GoogleAds adapter already does so, noted in Open Questions).

## Out of Scope
Changing the jobs' skip-when-disabled behaviour; removing the MarketingInvoices feature; moving `AdSettingsGuard` to a shared location; health checks; runtime (post-startup) config reload.

## Open Questions
None.

## Status: COMPLETE
