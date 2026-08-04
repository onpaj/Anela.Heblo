# Design: Consolidate eshop remark append logic into IEshopOrderClient

## Scope note

This is a backend-only, behavior-preserving refactor with no user-facing surface — no
UI, no new HTTP endpoint, no request/response contract visible outside the process. The
UX/UI section is omitted per the design template.

## Component design

### `IEshopOrderClient` (interface, Application layer)

Add one member, placed directly after `UpdateEshopRemarkAsync` since it composes the two
adjacent members:

```csharp
/// <summary>
/// Read-modify-write helper: appends <paramref name="text"/> to the order's current
/// eshop remark, separated by a newline. If the order has no remark yet, the remark
/// becomes <paramref name="text"/> verbatim (no leading separator).
/// Equivalent to:
///   var current = await GetEshopRemarkAsync(orderCode, ct);
///   var updated = string.IsNullOrEmpty(current) ? text : $"{current}\n{text}";
///   await UpdateEshopRemarkAsync(orderCode, updated, ct);
/// </summary>
Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default);
```

`GetEshopRemarkAsync` and `UpdateEshopRemarkAsync` remain on the interface unchanged —
both are still called directly elsewhere is not the case today (grep confirms only the
two duplicated call-sites use them), but removing them is out of scope for this finding
and not required to eliminate the duplication.

### `ShoptetOrderClient` (adapter, sole implementation)

`ShoptetOrderClient` is the only class implementing `IEshopOrderClient` in the codebase
(verified: no fakes/stubs implement the interface directly — unit tests use
`Mock<IEshopOrderClient>`, which auto-satisfies new interface members via Moq's dynamic
proxy, so no test double needs updating).

Add the method as a thin composition of the two existing public methods on the same
class, placed immediately after `UpdateEshopRemarkAsync` (line 178):

```csharp
public async Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default)
{
    var currentRemark = await GetEshopRemarkAsync(orderCode, ct);
    var updatedRemark = string.IsNullOrEmpty(currentRemark)
        ? text
        : $"{currentRemark}\n{text}";
    await UpdateEshopRemarkAsync(orderCode, updatedRemark, ct);
}
```

This calls the class's own public methods (not a shared private helper) — same call
shape the two duplicated call-sites already used against the interface, no measurable
overhead since this is an infrequent, I/O-bound sequence (two HTTP round-trips either
way). No change to wire format: still `GET /api/orders/{code}?include=notes` followed by
`PATCH /api/orders/{code}/notes`.

### `BlockOrderProcessingHandler.Handle`

Second `try` block (lines 53–64) collapses from 5 lines to 1 inside the unchanged
`try/catch (Exception ex) when (ex is not OperationCanceledException)`:

```csharp
try
{
    await _eshopOrderClient.AppendEshopRemarkAsync(request.OrderCode, request.Note, cancellationToken);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogWarning(ex, "Order {OrderCode} was blocked but the note could not be appended", request.OrderCode);
}
```

Call-site-specific log message and status flow (block succeeds independent of remark
outcome) are unchanged.

### `CompleteDeliveredOrdersJob.AppendCompletionNoteAsync`

The private method's body collapses the same way; the method itself, its try/catch, and
its call-site-specific log message stay as a named private method (documents intent at
the `ExecuteAsync` call site — not inlined, per the plan's judgment call):

```csharp
private async Task AppendCompletionNoteAsync(string orderCode, CancellationToken cancellationToken)
{
    try
    {
        await _orderClient.AppendEshopRemarkAsync(orderCode, CompletionNote, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogWarning(ex,
            "CompleteDeliveredOrders: order {OrderCode} was completed but the note could not be appended.",
            orderCode);
    }
}
```

### Component interaction (unchanged call graph, narrower interface surface)

```
BlockOrderProcessingHandler.Handle          CompleteDeliveredOrdersJob.AppendCompletionNoteAsync
            │                                              │
            └───────────────┬──────────────────────────────┘
                             ▼
           IEshopOrderClient.AppendEshopRemarkAsync(orderCode, text, ct)
                             │
                             ▼
           ShoptetOrderClient.AppendEshopRemarkAsync   (new — owns the read-modify-write)
              ├─ calls its own GetEshopRemarkAsync   → GET  /api/orders/{code}?include=notes
              └─ calls its own UpdateEshopRemarkAsync → PATCH /api/orders/{code}/notes
```

Before this change, both call-sites each independently drove `GetEshopRemarkAsync` +
ternary + `UpdateEshopRemarkAsync`; after, they drive one interface call and the
merge/separator policy exists in exactly one place (`ShoptetOrderClient`).

## Data schemas

No schema changes. No new DTOs, no changes to the Shoptet PATCH/GET request or response
bodies (`UpdateEshopRemarkRequest`/`UpdateEshopRemarkData`, `CreateOrderResponse` →
`Notes.EshopRemark`). The new method is a behavioral composition over existing wire
contracts, not a new one.

## Non-functional notes carried into design

- Error handling stays at the call sites (unchanged log messages, unchanged
  `OperationCanceledException` exclusion) — `AppendEshopRemarkAsync` itself does not
  catch; a failure in either the GET or the PATCH propagates unchanged to the caller's
  existing `try/catch`, preserving today's failure semantics exactly.
- `AppendEshopRemarkAsync` is not thread-safe against concurrent remark writes for the
  same order (no optimistic concurrency token from Shoptet) — this matches current
  behavior exactly; not a regression, not addressed by this refactor.
