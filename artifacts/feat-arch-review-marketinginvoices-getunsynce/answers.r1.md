### Question 1
Which option should be implemented — A (build the sync workflow) or B (remove the scaffolding)? The brief states "Option B is cheaper now; option A is the right call only when the sync workflow is actively being built." Is the accounting-system sync on the near-term roadmap, or should we remove the scaffolding now and re-add it when the sync work is actually scheduled?

**Answer:** Option B — remove the scaffolding now.

**Rationale:** The original shared-core design (`docs/superpowers/specs/2026-04-14-marketing-invoice-import-shared-core-design.md:50`) explicitly defers the sync to a "future FlexiBee phase," and epic #609 lists "FlexiBee received invoice creation from stored transactions" under **Future Work (not in scope)**. No open issue, PR, or plan in the repo schedules that work today. Per YAGNI and the established `IssuedInvoice` precedent (which uses a richer `SyncSucceeded` / `SyncFailed` API with sync-history rows, `LastSyncTime`, and `ErrorType` — not a single `bool IsSynced` + `text ErrorMessage` pair), any future sync implementation would almost certainly redesign these fields rather than reuse them as-is, so removing now is strictly cheaper than carrying speculative scaffolding.

### Question 2
(If Option A) Which accounting system is the sync target, and what is the transport (REST, SOAP, file drop, queue)?

**Answer:** Not applicable — Option B is chosen. For the record, if Option A were ever revived, the target would be **FlexiBee** (named explicitly in epic #609 and the shared-core spec) via the existing FlexiBee REST adapter that already backs `IssuedInvoice` sync — creating received-invoice documents from `ImportedMarketingTransaction` rows.

**Rationale:** Option B is the chosen direction (see Question 1). The target system is documented from prior artifacts only so a future implementer has a starting point.

### Question 3
(If Option A) What is the desired trigger model — scheduled background service, on-demand command, or both? And what schedule/cadence?

**Answer:** Not applicable — Option B is chosen. For the record, if Option A were ever revived, the project's established pattern is a Hangfire recurring job (mirroring `MetaAdsInvoiceImportJob` / `GoogleAdsInvoiceImportJob`, registered through the Recurring Jobs admin UI) running shortly after the import jobs (e.g., 06:30 and 18:30 daily), with an on-demand manual trigger exposed through the same admin UI.

**Rationale:** Option B is the chosen direction. The Hangfire-based, admin-toggleable pattern is the consistent project convention for periodic external-system writes.

### Question 4
(If Option A) What are the retry semantics for failed posts — bounded retries, indefinite retry until success, dead-letter after N attempts?

**Answer:** Not applicable — Option B is chosen. For the record, if Option A were ever revived, retries should follow the `IssuedInvoice` precedent: indefinite retry on the next scheduled run while `IsSynced = false`, with a structured `ErrorType` distinguishing transient (retry) from permanent (do not retry / surface for manual review) failures, and the error string persisted on the entity for diagnostics.

**Rationale:** Option B is the chosen direction. The `IssuedInvoice` model (`IsCriticalError` gated on `ErrorType != InvoicePaired`) already encodes the right precedent if revival ever happens.

### Question 5
(If Option B) Are there any pending PRs or in-flight work that already reference `GetUnsyncedAsync`, `IsSynced`, or `ErrorMessage` and would conflict with their removal? (Worth confirming before the migration is generated.)

**Answer:** Yes — two open PRs touch the same module and must be sequenced. PR **#1771** (`feat-arch-review-marketinginvoices-iimportedm`) edits `IImportedMarketingTransactionRepository.AddAsync` and the concrete repository — the same interface and class this spec modifies. PR **#1766** (`feat-arch-review-marketinginvoices-marketingt`) removes `MarketingTransaction.Platform` and touches `MarketingInvoiceImportService.cs` near the entity-construction block where `IsSynced = false` is set. No other open PR, issue, or branch in the repo reads or writes `GetUnsyncedAsync`, `IsSynced`, or `ErrorMessage`.

**Rationale:** `gh pr list --search "MarketingInvoice"` returns only #1771, #1766, and the long-running shared-core PR #616 (which originally introduced these fields and is irrelevant to current head). Sequence: merge whichever of #1771/#1766 lands first, then rebase this branch onto `main` before generating the EF migration so the migration is generated against the latest schema snapshot and no merge conflicts hit the interface or service files.
