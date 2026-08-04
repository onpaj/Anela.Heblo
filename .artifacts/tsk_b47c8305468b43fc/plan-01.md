# Plan: Consolidate eshop remark append logic into IEshopOrderClient

## Summary

`BlockOrderProcessingHandler` and `CompleteDeliveredOrdersJob` both implement the same
read-current-remark → guard-for-empty → append-with-`\n` → write-back sequence against
`IEshopOrderClient`. This plan adds a single `AppendEshopRemarkAsync` method to
`IEshopOrderClient`, implements it once in `ShoptetOrderClient`, and replaces both
call-sites, removing the duplicated logic.

## Context

Filed by the 2026-07-25 arch-review routine as a real-duplication finding: the
separator/null-guard convention (`\n` join, empty-string guard) currently lives in two
places and would silently diverge if changed in only one. The remark-append operation is
a coherent unit of behavior that belongs on the client interface that owns the write
(`UpdateEshopRemarkAsync`), not duplicated in each caller.

This is a pure refactor — no behavior change, no new business rules. Both call-sites
already wrap the sequence in `try/catch` with call-site-specific logging on failure; that
error handling and logging stays at the call sites, not inside the new client method.

## Functional requirements

**FR-1: Add `AppendEshopRemarkAsync` to `IEshopOrderClient`.**
- New method: `Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default)`.
- Acceptance: interface compiles; XML doc explains it performs a read-modify-write
  (get current remark, append with `\n` if non-empty, otherwise write `text` as-is,
  then call the update endpoint).

**FR-2: Implement it once in `ShoptetOrderClient`.**
- Body is exactly the 3-line pattern currently duplicated, using the class's existing
  `GetEshopRemarkAsync` / `UpdateEshopRemarkAsync` methods (or their internal
  equivalents if `ShoptetOrderClient` already avoids a self-call — verify during
  implementation which is idiomatic there).
- Acceptance: `dotnet build` succeeds; no change to the wire format of the PATCH request.

**FR-3: Replace the call-site in `BlockOrderProcessingHandler.Handle`.**
- Lines 55–59 (`GetEshopRemarkAsync` + ternary + `UpdateEshopRemarkAsync`) collapse to a
  single `await _eshopOrderClient.AppendEshopRemarkAsync(request.OrderCode, request.Note, cancellationToken);`.
- The surrounding `try { } catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(...) }`
  block is unchanged — only the 5 lines inside `try` shrink to 1.
- Acceptance: `BlockOrderProcessingHandlerTests` pass unmodified (behavior identical from
  the caller's perspective, since Moq mocks the new interface method automatically).

**FR-4: Replace the call-site in `CompleteDeliveredOrdersJob.AppendCompletionNoteAsync`.**
- Lines 138–142 collapse to a single
  `await _orderClient.AppendEshopRemarkAsync(orderCode, CompletionNote, cancellationToken);`.
- The wrapping `try/catch` and its logging in `AppendCompletionNoteAsync` stay as-is; the
  private method itself may now be trivial enough to consider inlining, but that's a
  judgment call for whoever implements this — not required by this finding.
- Acceptance: `CompleteDeliveredOrdersJobTests` pass unmodified.

## Non-functional requirements

- No change to Shoptet API request/response shape — this is an internal refactor only.
- No change to error-handling semantics: both call-sites must continue to catch and log
  failures the same way they do today (call-site-specific log messages must be
  preserved verbatim).
- Keep the change surgical: do not touch unrelated methods on `IEshopOrderClient` or
  `ShoptetOrderClient`, do not reformat surrounding code.

## Data model

No data model changes. No new entities; this only touches an interface method and its
implementation/call-sites.

## Interfaces

- `IEshopOrderClient.AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default)` — new.
- `IEshopOrderClient.GetEshopRemarkAsync` / `UpdateEshopRemarkAsync` — unchanged, remain
  on the interface (still used internally by the new method, and potentially still
  useful independently — confirm no other caller relies on the raw get/update pair
  before considering removal; out of scope to remove them here).

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`
- Existing tests that exercise these two call-sites (verify, don't rewrite unless a
  compile break forces it):
  - `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`
  - `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`

**Out of scope:**
- No changes to `GetEshopRemarkAsync` / `UpdateEshopRemarkAsync` signatures.
- No changes to any other `IEshopOrderClient` consumers (Packaging, ExpeditionList
  handlers, `ShoptetApiExpeditionListSource`) — they don't touch remark logic.
- No new integration tests against the live Shoptet API (per project rule, Shoptet API
  behavior is already documented in `docs/integrations/shoptet-api.md`; this refactor
  doesn't add or change any endpoint usage).
- Not addressing whether `AppendCompletionNoteAsync` should be inlined away — leave as a
  judgment call for the implementer.

## Rough plan

1. Add `AppendEshopRemarkAsync` to `IEshopOrderClient` with XML doc describing the
   read-modify-write contract (mirroring the existing doc style on
   `GetEshopRemarkAsync`/`UpdateEshopRemarkAsync`).
2. Implement it in `ShoptetOrderClient`, moving the 3-line pattern there verbatim.
3. Update `BlockOrderProcessingHandler.Handle` to call the new method; remove the
   now-redundant local `currentRemark`/`updatedRemark` variables.
4. Update `CompleteDeliveredOrdersJob.AppendCompletionNoteAsync` to call the new method;
   keep the method's own try/catch and logging.
5. Run `dotnet build` and `dotnet format` on the touched projects.
6. Run the two affected test files (and the full `Anela.Heblo.Tests` suite, since it's
   fast) to confirm no regression; Moq will auto-satisfy the new interface member on
   existing `Mock<IEshopOrderClient>` setups, so no test changes are expected.

## Open questions

- Should `ShoptetOrderClient.AppendEshopRemarkAsync` call its own
  `GetEshopRemarkAsync`/`UpdateEshopRemarkAsync` public methods, or should it share a
  private helper with them to avoid an extra virtual dispatch? Default: call the
  existing public methods directly (simplest, matches how the two duplicated call-sites
  already used the interface) — no measurable performance concern here (this is an
  infrequent, I/O-bound operation).
- Should `AppendCompletionNoteAsync` in `CompleteDeliveredOrdersJob` be inlined into
  `ExecuteAsync` now that its body is a single line inside a try/catch? Default: leave it
  as a named private method — it documents intent at the call site in `ExecuteAsync`
  (`await AppendCompletionNoteAsync(...)` reads better than the raw client call there),
  and renaming/removing it is not what the finding asked for.
