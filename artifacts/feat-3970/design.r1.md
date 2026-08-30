# Design: Remove Hangfire `[AutomaticRetry]` Leak from ProductPairingDqtJob

## Component Design

Single component touched, no new components introduced.

**`ProductPairingDqtJob`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`)
- Responsibility (unchanged): implement `IRecurringJob` for the `daily-product-pairing-dqt` recurring job — check whether the job is enabled, create and persist a `DqtRun` for today's date window, and delegate the actual Shoptet/ABRA Flexi product-pairing comparison to `IDriftDqtJobRunner`.
- Change: remove the `using Hangfire;` import and the `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` attribute currently decorating `ExecuteAsync`. Constructor, field list, `Metadata`, and `ExecuteAsync` body remain identical.
- Interface surface (unchanged): still implements `IRecurringJob` (`Anela.Heblo.Domain.Features.BackgroundJobs`); still constructed with `IDqtRunRepository`, `IDriftDqtJobRunner`, `IRecurringJobStatusChecker`, `TimeProvider`, `ILogger<ProductPairingDqtJob>`.

**Hangfire registration** (`HangfireJobRegistrationHelper`, `RecurringJobDiscoveryService`, `HangfireRecurringJobScheduler` — all `Anela.Heblo.API/Infrastructure/Hangfire/`): no design change. These already discover and register any `IRecurringJob` reflectively and are attribute-agnostic — they require no edits for this change to take effect. After the edit, Hangfire simply falls back to its built-in default retry filter (10 attempts) for `daily-product-pairing-dqt`, exactly as it already does for `InvoiceDqtJob`, `StockWriteBackDqtJob`, and `LotStockReconciliationDqtJob`.

## Data Schemas
None. No database schema, API request/response shape, or event payload is added, removed, or changed by this work. `DqtRun`, `DqtTestType`, `DqtTriggerType`, `DqtRunStatus`, and the `daily-product-pairing-dqt` Hangfire recurring-job record (name, cron expression, enabled flag) are all unaffected.
