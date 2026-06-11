# Specification: Replace Misleading Interface Injection in ShoptetApiExpeditionListSource

## Summary
Refactor `ShoptetApiExpeditionListSource` to accept the concrete `ShoptetOrderClient` instead of the `IEshopOrderClient` interface that is immediately downcast in the constructor. The interface injection is misleading because the methods actually invoked are not declared on the interface, so any non-`ShoptetOrderClient` implementation would throw `InvalidCastException` at first use.

## Background
The `ShoptetApiExpeditionListSource` class lives in the Shoptet adapter (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`). Its constructor declares a dependency on the abstraction `IEshopOrderClient` but then performs a hard cast to the concrete `ShoptetOrderClient`:

```csharp
_client = (ShoptetOrderClient)client;
```

The three operations this class calls — `GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, and `SetAdditionalFieldAsync` — are defined only on `ShoptetOrderClient`, not on `IEshopOrderClient` (defined in `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`).

The interface abstraction here provides no decoupling benefit: it merely hides a hard dependency behind a misleading constructor signature. Because the class and its required dependency both live in the same adapter assembly, depending on the concrete type is appropriate and clearer per Clean Architecture's "depend on abstractions only when needed for decoupling" principle.

This was flagged by the daily architecture review routine on 2026-06-06.

## Functional Requirements

### FR-1: Replace Interface Parameter with Concrete Type
The `ShoptetApiExpeditionListSource` constructor must accept `ShoptetOrderClient` directly instead of `IEshopOrderClient`.

**Acceptance criteria:**
- Constructor parameter type changed from `IEshopOrderClient` to `ShoptetOrderClient`.
- Backing field type (`_client`) changed from `IEshopOrderClient` to `ShoptetOrderClient`.
- The downcast `(ShoptetOrderClient)client` is removed.
- No other behavioral changes to the class.

### FR-2: Update Dependency Injection Registration
Wherever the DI container registers or resolves `ShoptetApiExpeditionListSource`, ensure `ShoptetOrderClient` is resolvable as a concrete type (it is already registered in the adapter, but verify the existing registration still satisfies the new constructor signature).

**Acceptance criteria:**
- Application starts without DI resolution errors.
- `ShoptetOrderClient` is registered in the DI container such that `ShoptetApiExpeditionListSource` can be resolved.
- No new registration is added unless required by the change.

### FR-3: Update Unit Tests
Any unit or integration tests that construct `ShoptetApiExpeditionListSource` must be updated to pass a `ShoptetOrderClient` (or a test double thereof) rather than an `IEshopOrderClient`.

**Acceptance criteria:**
- All existing tests touching `ShoptetApiExpeditionListSource` compile.
- All existing tests for this class pass after the change.
- No test setup workarounds remain that exist solely because of the previous misleading interface signature.

### FR-4: Preserve Public Behavior
The refactor must be behavior-preserving — no change to how the class fetches orders, retrieves expedition order details, or sets additional fields.

**Acceptance criteria:**
- All public methods on `ShoptetApiExpeditionListSource` produce identical outputs for the same inputs before and after the change.
- No method signatures on `ShoptetApiExpeditionListSource` change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The change is purely a type signature refactor; no runtime behavior changes.

### NFR-2: Security
No security impact. No authentication, authorization, secrets handling, or data exposure changes.

### NFR-3: Maintainability
The change improves maintainability by:
- Making the actual dependency explicit at the constructor signature.
- Eliminating the silent `InvalidCastException` risk.
- Removing the false impression that the class is decoupled from `ShoptetOrderClient`.

### NFR-4: Build & Validation
- `dotnet build` must succeed.
- `dotnet format` must produce no diffs.
- All unit tests touching this class must pass.

## Data Model
No data model changes. This is a constructor signature refactor only.

## API / Interface Design
No public API changes. The only signature change is to the internal constructor of `ShoptetApiExpeditionListSource`:

**Before:**
```csharp
public ShoptetApiExpeditionListSource(
    IEshopOrderClient client,
    TimeProvider timeProvider,
    ICatalogRepository catalog,
    ...)
```

**After:**
```csharp
public ShoptetApiExpeditionListSource(
    ShoptetOrderClient client,
    TimeProvider timeProvider,
    ICatalogRepository catalog,
    ...)
```

The `IEshopOrderClient` interface itself is **not** modified — other consumers of that interface remain untouched.

## Dependencies
- `ShoptetOrderClient` — must remain registered in the adapter's DI configuration.
- `IEshopOrderClient` interface — unchanged; other consumers unaffected.
- No external services, libraries, or features impacted.

## Out of Scope
- **Expanding `IEshopOrderClient`** to include `GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, or `SetAdditionalFieldAsync`. The brief recommends concrete injection as the appropriate solution; broadening the interface is a separate design decision and not part of this fix.
- **Refactoring other consumers** of `IEshopOrderClient` or `ShoptetOrderClient`.
- **Renaming** the class, methods, or constructor parameters beyond the type change.
- **Changes to expedition list business logic** or to any of the three Shoptet API methods invoked.
- **Tests for code paths not already covered** — coverage expansion is not part of this task.

## Open Questions
None.

## Status: COMPLETE