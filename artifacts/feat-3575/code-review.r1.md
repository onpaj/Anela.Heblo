## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:71` — the 6-line explanatory comment on `RevertTrackedChangesAsync` is unusually long for this codebase's comment density; consider trimming to the single load-bearing point (that this resets `EntityState` only, without rolling back in-memory CLR property values) if a future pass touches this file. Not required now — the content is accurate and the nuance is genuinely non-obvious.

## Overall Notes
Reviewed the full unified diff (`/tmp/feat-3575-review.diff`, merge-base with `main` at `7e19113c`) against `spec.r2.md` and `arch-review.r1.md`, and traced the control flow in the real files:

- `InvoiceImportService.cs`: `GetOrCreateAsync`'s signature change to `(IssuedInvoice invoice, bool isNew)` has exactly one call site (confirmed via `grep -rn "GetOrCreateAsync" backend/src` during task 1's review — only `ExecuteImportInvoice`). `invoice`/`isNew` are correctly hoisted above the `try` so both are visible in the `catch`. The revert guard `if (!isNew && invoice != null)` correctly excludes the new-invoice path (out of scope per spec) and the case where `GetByIdAsync` itself throws before any entity is returned (`invoice` stays `null`, matching the existing `ImportInvoicesAsync_WithPartialFailure_TracksFailedInvoices` test's scenario). The inner `try`/`catch` around `_issuedInvoiceClient.SaveAsync` (the `SyncFailed`/`SyncSucceeded` + immediate `UpdateAsync`/`SaveChangesAsync` path) is untouched, as required — that path's persisted status write is intentional and out of scope.
- A scenario worth noting (not a bug): if `UpdateAsync`/`SaveChangesAsync` at lines 118-119 itself throws for an existing invoice, the outer catch now reverts the tracked entity including any `SyncSucceeded`/`SyncFailed` mutation applied moments earlier in the inner try. This is correct and consistent with the fix's intent — nothing for this invoice was actually persisted, so nothing should be flushed by a later invoice's `SaveChangesAsync` either.
- `IIssuedInvoiceRepository.cs` / `IssuedInvoiceRepository.cs`: `RevertTrackedChangesAsync` is additive (no existing member signatures changed), correctly scoped to `IIssuedInvoiceRepository` rather than the generic `IRepository<TEntity, TKey>` base (per the arch review's placement guidance), and its `Context.Entry(entity).State = EntityState.Unchanged` implementation is a safe, synchronous, no-DB-round-trip operation — if the entity somehow weren't already tracked, this call would just begin tracking it as `Unchanged`, which is a harmless no-op for this use.
- `InvoiceImportRealChangeTrackerTests.cs`: genuinely exercises the outer-catch path via a throwing `IIssuedInvoiceImportTransformation` mock (not `client.SaveAsync`, which would hit the out-of-scope inner catch) — confirmed by the `_mockClient.Verify(..., Times.Never)` assertion. This was independently verified during task 2's review to fail against the pre-fix code and pass against the fix.
- No dead code, no duplicated logic, no obvious inefficiency introduced. No documentation needs identified — this is an internal bug fix with no public API, CLI, or config surface change.

Existing test suite (`InvoiceImportServiceTests` × 21 + new test × 1 + `InvoiceImportIntegrationTests`, 28 total) passes; `dotnet build` and `dotnet format --verify-no-changes` are clean (verified directly in this worktree during the developer/reviewer loop for both tasks).
