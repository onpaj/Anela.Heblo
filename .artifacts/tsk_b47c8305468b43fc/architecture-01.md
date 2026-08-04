# Architecture Assessment: Consolidate eshop remark append logic

## Verdict on plan-01.md / design-01.md

Both documents are technically sound for the two call-sites they name, and I verified
every file path, line number, and quoted snippet against the current tree — all match
exactly (`IEshopOrderClient.cs`, `ShoptetOrderClient.cs`, `BlockOrderProcessingHandler.cs`
lines 55–59, `CompleteDeliveredOrdersJob.cs` lines 138–142). **However, the scope is
incomplete.** There is a third, previously unnoticed instance of the exact same
read-modify-write pattern. Implementation must not proceed against plan-01/design-01
as written — it needs the scope amendment below first.

## Finding: a third duplicate site was missed

`ShoptetApiExpeditionListSource.FlagIncompleteAddressAsync`
(`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:232-238`)
runs the identical sequence:

```csharp
var note = $"Robot expedice: neúplná adresa – chybí: {string.Join(", ", missingFields)}.";
var current = await _client.GetEshopRemarkAsync(code, cancellationToken);
var updated = string.IsNullOrEmpty(current) ? note : $"{current}\n{note}";
await _client.UpdateEshopRemarkAsync(code, updated, cancellationToken);
```

This is the same three-step contract (get → null-guard → `\n`-join → update) the finding
describes, just with a different message string. It was missed because it's called
through `_client`, typed `IShoptetExpeditionOrderSource` — a second, parallel interface
(`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetExpeditionOrderSource.cs:12-13`)
that redeclares `GetEshopRemarkAsync`/`UpdateEshopRemarkAsync` verbatim, rather than
through `IEshopOrderClient`. A grep for `IEshopOrderClient.AppendEshopRemarkAsync`
call-sites alone would never surface it.

**Why the fix is trivial despite the extra interface:** `ShoptetApiExpeditionListSource`
already holds a *second* field, `_eshopOrderClient` (typed `IEshopOrderClient`), injected
alongside `_client` in its constructor
(`ShoptetApiExpeditionListSource.cs:20-21,29-40`) — it's already used elsewhere in the
same class for `UpdateStatusAsync` (line 223). DI registration
(`ShoptetApiAdapterServiceCollectionExtensions.cs:47-48`) resolves both
`IEshopOrderClient` and `IShoptetExpeditionOrderSource` from the same
`sp.GetRequiredService<ShoptetOrderClient>()` call, so at runtime `_client` and
`_eshopOrderClient` are the same object. **No change to `IShoptetExpeditionOrderSource`
is needed.** `FlagIncompleteAddressAsync` should simply call the already-injected
`_eshopOrderClient.AppendEshopRemarkAsync(code, note, cancellationToken)` instead of the
two `_client.*RemarkAsync` calls, inside the existing `try/catch` (log message and
`OperationCanceledException` exclusion unchanged, matching the pattern of the other two
sites).

I confirmed this doesn't break test setup:
`ShoptetApiExpeditionListSource_AddressValidationTests.BuildSource` constructs one real
`ShoptetOrderClient` against a mocked `HttpMessageHandler` and passes it as *both*
constructor arguments (`client, client` —
`ShoptetApiExpeditionListSource_AddressValidationTests.cs:123-126`), asserting on HTTP
traffic, not on which interface reference was used. Routing the call through
`_eshopOrderClient` instead of `_client` is invisible to that test.

## Scope amendment (supersedes plan-01/design-01 scope sections)

Add to "in scope":
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
  — replace lines 235–237 with a single
  `await _eshopOrderClient.AppendEshopRemarkAsync(code, note, cancellationToken);` call
  (FR-5, mirrors FR-3/FR-4).
- Existing tests to verify unmodified:
  `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_AddressValidationTests.cs`.

Everything else in plan-01.md and design-01.md stands as written:
- `IEshopOrderClient.AppendEshopRemarkAsync` — single new interface member, placed after
  `UpdateEshopRemarkAsync`.
- `ShoptetOrderClient.AppendEshopRemarkAsync` — the one implementation, composing its own
  public `GetEshopRemarkAsync`/`UpdateEshopRemarkAsync` (confirmed sole implementer of
  `IEshopOrderClient`).
- **Do not** add `AppendEshopRemarkAsync` to `IShoptetExpeditionOrderSource` — that
  interface is a separate, narrower contract for expedition-specific reads
  (`GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, `GetOrderByCodeAsync`,
  `SetAdditionalFieldAsync`) plus the same two remark methods duplicated onto it.
  Widening it would be scope creep — the class already has `IEshopOrderClient` in hand
  for exactly this purpose. (The duplicate interface declaration of
  `Get/UpdateEshopRemarkAsync` across two segregated interfaces is itself a pre-existing
  smell, but unwinding it is a separate concern from this finding — flag it, don't fix it
  here.)

## Interface/contract summary (final)

```csharp
// IEshopOrderClient.cs — Application layer, one new member
Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default);
```

Three call-sites collapse to one line each, all via `IEshopOrderClient`:
1. `BlockOrderProcessingHandler.Handle` (as designed).
2. `CompleteDeliveredOrdersJob.AppendCompletionNoteAsync` (as designed).
3. `ShoptetApiExpeditionListSource.FlagIncompleteAddressAsync` (new — via the
   already-present `_eshopOrderClient` field, no constructor change).

## Risks and mitigations

- **Risk:** stopping at two call-sites (as plan-01/design-01 currently scope it) leaves
  the exact duplication the finding warns about alive in a third place — the underlying
  architectural problem (separator/guard convention drifting) would only be
  two-thirds fixed. **Mitigation:** implement FR-5 above in the same change; it's a
  3-line diff with no new dependencies.
- **Risk:** confusing `_client` and `_eshopOrderClient` in
  `ShoptetApiExpeditionListSource` during the edit, given both are in scope in that
  method's enclosing class. **Mitigation:** only touch the two lines inside
  `FlagIncompleteAddressAsync`; do not touch `_client`'s other call-sites in the same
  file (`GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, etc.) or its
  interface declaration.
- **Risk (carried from design-01, still valid):** no optimistic concurrency on the
  Shoptet remark field — concurrent appends to the same order can race and lose an
  update. Pre-existing across all three sites, not a regression, not in scope to fix.

## Prerequisites before implementation

None beyond what plan-01.md already lists — this amendment only adds one more
mechanical call-site replacement using infrastructure (the `_eshopOrderClient` field)
that already exists in the target file.
