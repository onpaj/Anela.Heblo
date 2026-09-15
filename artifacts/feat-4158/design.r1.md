# Design: Bank staleness warning logged twice per scheduled job run

## Component Design

### `ImportBankStatementHandler` (modified)
- **File:** `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- **Responsibility (unchanged):** Execute a bank-statement import for a given `ImportBankStatementRequest(AccountName, DateFrom, DateTo)` — fetch statements from the bank client, dedupe against existing `TransferId`s, persist import results, update `BankImportState` success/failure, return `BankStatementImportResultDto`.
- **Responsibility removed:** No longer evaluates whether `BankImportState.LastValidImportDate` is stale relative to `BankImportWatermarkOptions.StaleWarningDays`, and no longer logs a staleness warning. This decision is now made exclusively upstream, by `BankImportJobBase`, for scheduled runs; it is not replicated for the manual-trigger path (see `arch-review.r1.md`, Decision 2 — accepted trade-off, no replacement component).
- **Constructor contract change:** Drops `IOptions<BankImportWatermarkOptions> watermarkOptions` parameter and the `_watermarkOptions` field. All other constructor parameters (`IBankClientFactory`, `IBankStatementImportService`, `IBankStatementImportRepository`, `IOptions<BankAccountSettings>`, `IBankImportStateRepository`, `IMapper`, `ILogger<ImportBankStatementHandler>`) are unchanged.
- **State usage (unchanged):** Still loads `state` via `_stateRepository.GetByAccountAsync(...)` and still calls `state.RecordSuccess(...)` / `state.RecordFailure(...)` / `_stateRepository.UpsertAsync(state, ...)` — only the staleness *read-and-log* branch on `state.LastValidImportDate` is removed, not `state` itself.

### `BankImportJobBase` (unchanged)
- **File:** `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`
- Remains the sole owner of staleness-warning logging (`ResolveDateFromAsync`, the `else if (span > _options.StaleWarningDays)` branch) for all scheduled job runs. No code changes; this component's existing behavior is exactly what should remain after the fix.

### `BankStatementsController` (unchanged)
- **File:** `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- `ImportStatements` (`POST /api/bank-statements/import`) continues to call `_mediator.Send(new ImportBankStatementRequest(...))` unchanged. After this fix, a stale watermark on this manual-trigger path produces no warning log (previously produced exactly one, from the handler) — an accepted, documented trade-off, not a new component responsibility.

## Data Schemas

No schema changes. No new or modified:
- Database tables/columns (`BankImportState`, `BankStatementImport` unchanged).
- Request/response DTOs (`ImportBankStatementRequest`, `BankStatementImportResultDto`, `BankImportRequestDto` unchanged).
- Configuration schema (`BankImportWatermarkOptions` — `StaleWarningDays`, `MaxBackfillDays` — unchanged; still bound in `BankModule.AddBankModule` and still consumed by `BankImportJobBase`; simply no longer injected into `ImportBankStatementHandler`).
- Log event shape: one log statement (`Bank import watermark is {DaysBehind} days stale for account {AccountName}. Last valid date: {LastValidDate}`) is deleted outright; the surviving job-side log statement (`{JobName} watermark is {Span} days behind; importing range {DateFrom:yyyy-MM-dd}..{DateTo:yyyy-MM-dd}.`) is unchanged in format.
