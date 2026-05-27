# Specification: Honest Dependency in ShoptetApiPackingOrderClient

## Summary
Remove the dishonest interface dependency in `ShoptetApiPackingOrderClient` that declares `IEshopOrderClient` but immediately downcasts to concrete `ShoptetOrderClient`. Replace it with a direct dependency on `ShoptetOrderClient` so the constructor signature matches actual runtime requirements, restores DIP/LSP compliance, and unblocks unit testing of the adapter.

## Background
`ShoptetApiPackingOrderClient` (adapter sitting between the application layer and Shoptet's REST API) declares an `IEshopOrderClient` parameter but in its constructor body performs `orderClient as ShoptetOrderClient ?? throw …`. This is required because the method it relies on — `GetExpeditionOrderDetailAsync` — is declared only on the concrete `ShoptetOrderClient`, not on `IEshopOrderClient`.

Consequences:
- **DIP violation:** The class doesn't actually depend on the abstraction; it depends on the concrete type and lies about it.
- **LSP violation:** No other implementation of `IEshopOrderClient` is substitutable — any non-`ShoptetOrderClient` instance fails immediately at construction.
- **Test coverage gap:** The adapter cannot be unit-tested with a simple mock because the cast forces tests to supply a real `ShoptetOrderClient`, which in turn needs a live `HttpClient`. As a result, `GetPackingOrderHandlerTests` mocks one layer up (`IPackingOrderClient`) and the adapter itself has zero direct coverage.

This finding was filed by the daily arch-review routine on 2026-05-23.

## Functional Requirements

### FR-1: Remove the runtime downcast in `ShoptetApiPackingOrderClient`
Change the constructor of `ShoptetApiPackingOrderClient` to depend on the concrete `ShoptetOrderClient` directly. Drop the `as`/throw cast pattern entirely. The private `_orderClient` field becomes typed as `ShoptetOrderClient`.

**Acceptance criteria:**
- The constructor signature accepts `ShoptetOrderClient` (not `IEshopOrderClient`).
- No `as` cast or `InvalidOperationException` for the order-client parameter remains in the class.
- The private field used to call `GetExpeditionOrderDetailAsync` is typed as `ShoptetOrderClient`.
- `dotnet build` succeeds for the entire backend solution.

### FR-2: Update DI registration so the resolved instance matches the new constructor
Wherever `ShoptetApiPackingOrderClient` is constructed (DI container module / ServiceCollection extension under `Anela.Heblo.Adapters.ShoptetApi`), ensure that `ShoptetOrderClient` is registered and resolvable as the concrete type so the new constructor parameter is satisfied.

**Acceptance criteria:**
- `ShoptetOrderClient` is registered in DI as itself (in addition to whatever existing `IEshopOrderClient` registration it has, which remains untouched).
- Application boot resolves `ShoptetApiPackingOrderClient` without runtime exceptions.
- No other consumer of `IEshopOrderClient` is broken by the registration change.

### FR-3: Preserve all existing behavior of `ShoptetApiPackingOrderClient`
The methods on `ShoptetApiPackingOrderClient` — including the `GetExpeditionOrderDetailAsync` call at line 46 and any catalog/repository interactions — must behave identically before and after the change. This is a pure structural refactor.

**Acceptance criteria:**
- No method signatures on `ShoptetApiPackingOrderClient` change.
- No call sites of `ShoptetApiPackingOrderClient` need to be modified (other than DI wiring covered in FR-2).
- Existing `GetPackingOrderHandlerTests` continues to pass without modification.

### FR-4: Add direct unit tests for `ShoptetApiPackingOrderClient`
With the downcast removed, mocking `ShoptetOrderClient` directly is still not trivial (it likely takes a non-virtual `HttpClient`-backed surface). Add at least one unit test exercising `ShoptetApiPackingOrderClient` with a test double of `ShoptetOrderClient` (using virtual methods, a hand-rolled subclass, or `HttpClient` with a mocked `HttpMessageHandler` — whichever is least invasive given the existing shape of `ShoptetOrderClient`).

**Acceptance criteria:**
- At least one new test in `Anela.Heblo.Adapters.ShoptetApi.Tests` (or equivalent) covers a happy-path call through `ShoptetApiPackingOrderClient`.
- The test exercises the path that previously could not be reached because of the downcast.
- The new test passes via `dotnet test`.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. Removing a downcast and `throw` only reduces a tiny one-time cost at constructor time. No request-path code changes.

### NFR-2: Security
No security surface area affected. No auth, secrets, or external API contracts change.

### NFR-3: Maintainability
The refactor must improve, not degrade, maintainability:
- The class must clearly declare its true dependency.
- No reflective or generic indirection introduced to "preserve" the old interface dependency.
- No new abstractions added speculatively (YAGNI). If a narrow interface is ever needed by a second consumer, that's a future change.

### NFR-4: Backward compatibility
Internal refactor only. No external API, DTO, or persisted-data shape changes. Other consumers of `IEshopOrderClient` (if any) keep working untouched.

## Data Model
No data model changes. No entities, DTOs, or database tables are added, modified, or removed.

## API / Interface Design

**Class touched:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — constructor signature changes from `IEshopOrderClient` to `ShoptetOrderClient`; field type changes accordingly; downcast removed.

**Constructor — before:**
```csharp
public ShoptetApiPackingOrderClient(
    IEshopOrderClient orderClient,
    ICatalogRepository catalog,
    ...)
{
    _orderClient = orderClient as ShoptetOrderClient
        ?? throw new InvalidOperationException(...);
    ...
}
```

**Constructor — after:**
```csharp
public ShoptetApiPackingOrderClient(
    ShoptetOrderClient orderClient,
    ICatalogRepository catalog,
    ...)
{
    _orderClient = orderClient;
    ...
}
```

**DI module touched:** the `Anela.Heblo.Adapters.ShoptetApi` service-collection registration (typically a `ServiceCollectionExtensions`-style file). Register `ShoptetOrderClient` as itself if it is not already.

**No HTTP endpoints, MediatR contracts, or front-end client surfaces are affected.**

## Dependencies
- `Anela.Heblo.Adapters.ShoptetApi` project — contains `ShoptetApiPackingOrderClient`, `ShoptetOrderClient`, `IEshopOrderClient`.
- `Anela.Heblo.Application` (or wherever `IPackingOrderClient` and the consumer handlers live) — must continue to resolve `IPackingOrderClient` (which is what handlers depend on) successfully.
- DI composition root (Program.cs / `AddShoptetApiAdapters` extension) — must register `ShoptetOrderClient`.

No external service, library, or package dependency is added or upgraded.

## Out of Scope
- **Extracting a narrow interface** (e.g. `IExpeditionOrderDetailClient`) — explicitly rejected for this work. The brief calls out option 1 (concrete injection) as the smallest fix; option 2 (new narrow interface) is not introduced now because there is no second consumer requiring substitutability. It can be revisited if/when a second implementation appears.
- **Refactoring `IEshopOrderClient`** to add `GetExpeditionOrderDetailAsync` to the interface — out of scope; would expand the interface to fit a single consumer and pollute other implementations.
- **Wider refactor of `ShoptetOrderClient`** (e.g. extracting `HttpClient` boundary to make it more mockable) — out of scope for this ticket; only do the minimum needed to allow at least one direct unit test under FR-4. If that minimum requires a non-trivial change to `ShoptetOrderClient`, raise it as a follow-up rather than expanding scope here.
- **Removing or modifying other consumers of `IEshopOrderClient`** — untouched.
- **Adding test coverage beyond the one happy-path test required in FR-4** — broader coverage of the adapter is desirable but not required by this ticket.

## Open Questions
None.

## Status: COMPLETE