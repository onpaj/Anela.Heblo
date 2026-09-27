## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51` — This one-line fix (`HasSeededFieldsChanged(existing, config)` → `HasSeededFieldsChanged(existingConfig, config)`) is a genuine, correct fix for a pre-existing bug on `origin/main` (confirmed identical to main pre-fix; `existing` is the full `List<RecurringJobConfiguration>`, which wouldn't even compile against the `HasSeededFieldsChanged(RecurringJobConfiguration, RecurringJobConfiguration)` signature — this was blocking the whole solution from building). It is unrelated to issue #4330's `ExpeditionListArchive` scope (NFR-2 scope discipline). It was necessary to unblock compilation and is isolated in its own commit (`352f78546`) with a clear message. No action needed; noting only because a stricter scope-discipline reading would prefer this shipped as a separate PR rather than riding along on this branch.

## Notes

Reviewed the full feature diff (`ecb13edf4` vs. merge-base with `main`) against `spec.r1.md`'s FR-1/FR-2/FR-3/NFR-1/NFR-2.

**FR-1 (eliminate the registration-order dependency):** `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` no longer registers `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` at all — it only configures `ExpeditionListArchiveOptions`. `ReprintExpeditionListHandler` now receives `[FromKeyedServices("cups")] IPrintQueueSink? cupsSink = null` (trailing, defaulted, per C#'s optional-parameter-must-be-last rule) plus a required `IPrintQueueSink fallbackSink`, with the constructor body doing `_cupsSink = cupsSink ?? fallbackSink` — identical selection semantics to the deleted factory's `GetKeyedService("cups") ?? GetRequiredService<IPrintQueueSink>()`. MediatR's normal assembly scan is now the sole registrant, so registration order in `ApplicationModule.cs` is provably irrelevant to this handler. Confirmed via `grep` that no other file constructs `ReprintExpeditionListHandler` positionally except the one test file, which was updated to match.

**FR-2 (regression test):** `ReprintExpeditionListHandlerRegistrationTests` builds a real `ServiceCollection` via `AddMediatR(...)` + `AddExpeditionListArchiveModule(...)` (deliberately not caring which is called first) and asserts exactly one `IRequestHandler<...>` registration, plus keyed-vs-fallback sink selection in both the keyed-present and keyed-absent cases. This is a solid, order-independent proxy for the invariant FR-1 requires, and mirrors the existing `CombinedPrintQueueSinkRegistrationTests` pattern in the same test folder.

**FR-3 (no-keyed-sink fallback preserved):** `NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink` exercises this directly, and the existing `ReprintExpeditionListHandlerTests` unit tests were updated only for the new constructor parameter order/count — their assertions and mock wiring are otherwise untouched, preserving intent.

**NFR-1 (no behavior change):** The selection expression is unchanged; only where it executes moved from an opaque factory delegate to the constructor. Traced all consumers of `IPrintQueueSink` in `backend/src` (`ExpeditionListService`, the three adapter implementations, `ServiceCollectionExtensions`, `ExpeditionListArchiveModule`) — `ExpeditionListService` is the only other consumer of the ambient (non-keyed) sink and is untouched by this diff, confirming the architect's finding that this change cannot collide with the main expedition-list print flow.

**NFR-2 (scope discipline):** Only `ReprintExpeditionListHandler.cs`, `ExpeditionListArchiveModule.cs`, and the two test files change in the feature's own scope, plus the one unrelated pre-existing-bug line noted above as advisory.

The prior task-level code reviews (`review/add-registration-regression-test.r1.md`, `review/remove-manual-handler-factory-and-inject-keyed-sink.r1.md`) already document that the developer actually ran the full test suite (39/39 relevant tests passing, including the previously-red registration-count assertion) and a full solution build/format check with clean results — I did not re-run the full solution build in this pass (a single-core `dotnet build` of this solution runs past the available time budget here) but traced every changed call site and constructor signature by hand and found them internally consistent and consistent with the passing test evidence already on record.

No correctness bugs found. Result: CLEAN.
