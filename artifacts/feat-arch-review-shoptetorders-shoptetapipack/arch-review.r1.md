Now I have a full picture. Let me write the architecture review.

# Architecture Review: Honest Dependency in ShoptetApiPackingOrderClient

## Skip Design: true

## Architectural Fit Assessment
This is a structural refactor inside the `Anela.Heblo.Adapters.ShoptetApi` adapter assembly. The change aligns with the codebase's existing Clean Architecture boundary: the application layer (`IPackingOrderClient`, `IEshopOrderClient`) owns abstractions, and the adapter project provides concrete Shoptet implementations. The current `IEshopOrderClient` cast in `ShoptetApiPackingOrderClient` (line 31) violates DIP/LSP and the project convention that constructor signatures should reflect actual dependencies.

**Two integration points must be handled together:**
1. The constructor of `ShoptetApiPackingOrderClient` — minor, mechanical.
2. The typed-HttpClient registration in `ShoptetApiAdapterServiceCollectionExtensions.cs` — non-trivial. The current `services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>(...)` registers **only `IEshopOrderClient`**; `ShoptetOrderClient` is **not separately resolvable** as a concrete type. Simply adding a second `AddTransient<ShoptetOrderClient>()` would resolve it without an HttpClient, defeating the typed-client design. The fix must route both registrations to the same typed-client.

**Important spec correction:** The spec/brief claim that `ShoptetApiPackingOrderClient` has "zero direct test coverage because of the cast" is factually wrong. `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs` already contains 10 unit tests that pass a real `ShoptetOrderClient` (constructed with `FakeDelegatingHandler`) into the SUT. The current cast (`orderClient as ShoptetOrderClient`) never fails in these tests because the cast succeeds. The real value of this refactor is **architectural honesty**, not unlocking impossible tests.

**Adjacent finding (out of scope, document only):** `ShoptetApiExpeditionListSource` (line 35) has the identical pattern — `IEshopOrderClient` parameter immediately downcast to `ShoptetOrderClient`. The arch-review routine should file this as a separate ticket; do not bundle here.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ Application layer                                                    │
│   IPackingOrderClient  ◄────────┐                                    │
│   IEshopOrderClient    ◄──────┐ │                                    │
└───────────────────────────────┼─┼────────────────────────────────────┘
                                │ │
┌───────────────────────────────┼─┼────────────────────────────────────┐
│ Adapters.ShoptetApi           │ │                                    │
│                               │ │                                    │
│   ShoptetOrderClient ─────────┘ │  (concrete typed HttpClient)       │
│        ▲                        │                                    │
│        │ injected as concrete   │                                    │
│        │                        │                                    │
│   ShoptetApiPackingOrderClient ─┘  (was IEshopOrderClient + cast)    │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

After the change:
- `ShoptetOrderClient` is registered as the **single source-of-truth typed client** and resolves both as itself and as `IEshopOrderClient`.
- `ShoptetApiPackingOrderClient` declares its real dependency (`ShoptetOrderClient`).
- All other `IEshopOrderClient` consumers (`BlockOrderProcessingHandler`, `ScanPackingOrderHandler`, etc.) are untouched.

### Key Design Decisions

#### Decision 1: DI registration shape for the typed client
**Options considered:**
1. Two separate `AddHttpClient` calls (`<IEshopOrderClient, ShoptetOrderClient>` + `<ShoptetOrderClient>`) — would create **two distinct typed clients** with two distinct HttpClient configurations; not the same underlying client; subtly wrong and duplicative.
2. Keep `AddHttpClient<IEshopOrderClient, ShoptetOrderClient>` and add `services.AddTransient<ShoptetOrderClient>(sp => sp.GetRequiredService<IEshopOrderClient>() as ShoptetOrderClient ?? throw …)` — re-introduces a cast in the composition root; no better than the original.
3. **Chosen — invert the typed-client registration:** Register `AddHttpClient<ShoptetOrderClient>(configure)` directly on the concrete class, then forward the interface via `services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())`. One typed client, no casts, both abstractions resolvable.

**Rationale:** Option 3 is the idiomatic `AddHttpClient` pattern when one concrete class implements multiple service shapes. It keeps a single source of HttpClient configuration (BaseAddress + token header) and naturally aligns with the goal of "stop lying about the dependency."

#### Decision 2: Where the new constructor parameter type comes from
**Chosen approach:** The constructor takes `ShoptetOrderClient` directly (concrete). No new abstraction (`IExpeditionOrderDetailClient`) is introduced — out of scope per spec, YAGNI per project rules.
**Rationale:** Both consumers of `GetExpeditionOrderDetailAsync` live inside the same adapter assembly (`ShoptetApiPackingOrderClient`, `ShoptetApiExpeditionListSource`). Adapter-internal coupling to a sibling concrete type is acceptable and explicit, mirroring the comment already present at `ShoptetApiExpeditionListSource.cs:18-20`.

#### Decision 3: Test additions
**Chosen approach:** No new tests are required *to satisfy FR-4 as written*. The existing `ShoptetApiPackingOrderClientTests` already cover the happy path via the `FakeDelegatingHandler` pattern. After the refactor, those tests must still compile and pass without changes (the parameter type change is source-compatible because the test already passes a `ShoptetOrderClient`).
**Rationale:** Adding a redundant test to "satisfy" an FR built on a faulty premise violates YAGNI and the project's "surgical changes" rule. The spec must be amended (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure
No new files. Touch only:
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`

### Interfaces and Contracts

**`ShoptetApiPackingOrderClient` constructor — final shape:**

```csharp
public ShoptetApiPackingOrderClient(
    ShoptetOrderClient orderClient,
    ICatalogRepository catalog,
    ICarrierCoolingRepository carrierCooling,
    ILogger<ShoptetApiPackingOrderClient> logger,
    IOptions<ShoptetApiSettings> settings)
{
    _orderClient = orderClient;
    _catalog = catalog;
    _carrierCooling = carrierCooling;
    _logger = logger;
    _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
}
```

Field type: `private readonly ShoptetOrderClient _orderClient;` (already correct on line 18; no change there).

**DI registration — final shape (in `ShoptetApiAdapterServiceCollectionExtensions.AddShoptetApiAdapter`):**

Replace:
```csharp
services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>((sp, client) => { ... });
```
with:
```csharp
services.AddHttpClient<ShoptetOrderClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
});
services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
```

No other consumer code changes. `IEshopOrderClient` continues to resolve and yields the same `ShoptetOrderClient` instances (transient, with HttpClient pooled by the HttpClientFactory).

### Data Flow
Unchanged. Runtime sequence for a packing-order lookup:
1. MediatR handler depends on `IPackingOrderClient` (resolved to `ShoptetApiPackingOrderClient`).
2. `ShoptetApiPackingOrderClient` calls `_orderClient.GetExpeditionOrderDetailAsync(code)` and `_orderClient.GetOrderStatusIdAsync(code)` — same method calls as today.
3. `ShoptetOrderClient` uses its injected `HttpClient` (configured by HttpClientFactory) to hit `/api/orders/{code}?include=stockLocation,notes` and `/api/orders/{code}`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Naive DI fix (registering `ShoptetOrderClient` separately without `AddHttpClient`) would resolve it without an HttpClient → NullReferenceException at first call | HIGH | Use the Decision 1 pattern: `AddHttpClient<ShoptetOrderClient>` + transient forwarder for `IEshopOrderClient`. Verify at boot by resolving both types. |
| Existing tests break because they passed `ShoptetOrderClient` into a parameter typed `IEshopOrderClient` and now the parameter is concrete | LOW | The change is source-compatible — tests already pass a `ShoptetOrderClient`. Just run `dotnet test` after the change. |
| Other `IEshopOrderClient` consumers see a behavior change (e.g. different HttpClient instance reuse semantics) | LOW | `AddHttpClient<TConcrete>` + transient forwarder uses the same HttpClientFactory pipeline as the old `AddHttpClient<TInterface, TImpl>`; pooling/lifetime behavior is equivalent. |
| Adjacent identical pattern in `ShoptetApiExpeditionListSource` (line 35) remains in place and re-creates the smell | MEDIUM | Out of scope for this ticket. File a follow-up bug. Add a TODO-style comment? No — project rule says don't improve adjacent code. |
| Spec FR-4 asks for a "new test" that duplicates existing coverage | MEDIUM | Amend the spec (below) to drop FR-4 or reframe it as "verify existing tests still pass." |

## Specification Amendments

1. **FR-4 must be reframed.** The premise ("the adapter has zero direct coverage because of the downcast") is factually wrong — see `ShoptetApiPackingOrderClientTests.cs`. Replace FR-4 with:

   > **FR-4 (amended): Verify existing tests survive the refactor.** The existing 10 tests in `Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs` must compile and pass after the change to the constructor parameter type. No new tests are required. If the developer notices a coverage gap not caused by the cast, file a separate ticket.

2. **Background section needs a correction.** Remove the bullet "Test coverage gap: …As a result, `GetPackingOrderHandlerTests` mocks one layer up (`IPackingOrderClient`) and the adapter itself has zero direct coverage." Replace with: "Test coverage of the adapter exists (`ShoptetApiPackingOrderClientTests`) using a `FakeDelegatingHandler`-backed `ShoptetOrderClient`. The cast pattern does not actively block testing today, but it remains a DIP/LSP violation that misleads future readers."

3. **FR-2 must specify the DI pattern explicitly.** As written ("ensure `ShoptetOrderClient` is registered and resolvable as the concrete type so the new constructor parameter is satisfied") leaves room for the wrong fix. Add: "Use `services.AddHttpClient<ShoptetOrderClient>(...)` for the typed-HttpClient registration, and forward `IEshopOrderClient` via `services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())`. Do not add a separate `AddHttpClient<IEshopOrderClient, ShoptetOrderClient>` registration — that produces two typed clients with two HttpClient configurations."

4. **Add explicit out-of-scope note for `ShoptetApiExpeditionListSource`.** Already partially implied by "Removing or modifying other consumers of `IEshopOrderClient` — untouched", but the identical downcast in `ShoptetApiExpeditionListSource.cs:35` deserves explicit mention so the implementer doesn't get tempted to fix it. Add to Out of Scope: "`ShoptetApiExpeditionListSource` has the same downcast pattern. Out of scope here; file a follow-up ticket."

## Prerequisites
None. No migrations, no config changes, no infrastructure work. The refactor is self-contained inside the `Anela.Heblo.Adapters.ShoptetApi` assembly and DI composition root. `dotnet build` + `dotnet test` after the change is the only validation gate.