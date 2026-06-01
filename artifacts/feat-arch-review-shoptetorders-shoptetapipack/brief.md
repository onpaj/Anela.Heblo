## Module
ShoptetOrders

## Finding
`ShoptetApiPackingOrderClient` declares a dependency on `IEshopOrderClient` (an interface) but immediately narrows it to the concrete `ShoptetOrderClient` in the constructor body and throws if the cast fails:

```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs — lines 24–34
```

```csharp
public ShoptetApiPackingOrderClient(
    IEshopOrderClient orderClient,   // ← claims interface dependency
    ...)
{
    _orderClient = orderClient as ShoptetOrderClient
        ?? throw new InvalidOperationException(
            $"{nameof(IEshopOrderClient)} must be {nameof(ShoptetOrderClient)} ...");
```

The concrete cast is needed because `GetExpeditionOrderDetailAsync` (called at line 46) is defined on `ShoptetOrderClient` directly, not on `IEshopOrderClient`. The interface is therefore never substitutable — passing any other implementation causes an immediate runtime failure.

## Why it matters
This violates the Dependency Inversion Principle (depend on abstractions) and Liskov Substitution Principle (any implementation of the interface must be usable). In practice it makes `ShoptetApiPackingOrderClient` untestable with a mock without also providing a `ShoptetOrderClient` concrete instance, which requires a live HTTP client. The `GetPackingOrderHandlerTests` works around this by mocking `IPackingOrderClient` one level up, but the adapter itself has zero test coverage because of this cast.

## Suggested fix
The smallest fix is to make the dependency honest: inject `ShoptetOrderClient` directly instead of the interface.

```csharp
public ShoptetApiPackingOrderClient(
    ShoptetOrderClient orderClient,   // honest concrete dependency
    ICatalogRepository catalog,
    ...)
{
    _orderClient = orderClient;
```

Alternatively, extract a narrow interface (e.g. `IExpeditionOrderDetailClient`) that exposes only `GetExpeditionOrderDetailAsync` and `GetOrderStatusIdAsync`, have `ShoptetOrderClient` implement it, and inject that. This restores substitutability without changing callers.

---
_Filed by daily arch-review routine on 2026-05-23._