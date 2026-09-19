# Specification: Split test-only lifecycle methods out of IEshopOrderClient

## Summary
`IEshopOrderClient` (Application layer, `Features/ShoptetOrders/`) currently exposes 11 methods, but 4 of them (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) are Shoptet test-data housekeeping operations called only from adapter integration tests, never from Application business logic. This violates the Interface Segregation Principle and forces every Application-layer mock/stub of `IEshopOrderClient` to implement methods it will never use. This change extracts those 4 methods into a new adapter-owned interface so the Application-layer contract reflects only what handlers actually need.

## Background
Issue #3259 previously removed genuinely dead methods (zero callers anywhere) from `IEshopOrderClient`. This issue is a follow-on, narrower finding from the same arch-review routine: the 4 methods in question are *not* dead — they have real callers — but every one of those callers is integration test setup/teardown code in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/`, not Application handler code. Because the interface lives in the Application layer, any handler-level unit test that mocks `IEshopOrderClient` (e.g. for `BlockOrderProcessingHandler`) is forced to provide stub implementations for these 4 test-infrastructure methods even though the handler under test never calls them. The suggested fix is to move these methods to a new interface owned by the adapter project, implemented by the same concrete class, so integration tests reference the adapter-level interface directly while the Application-layer interface shrinks to the 7 methods handlers actually use.

## Functional Requirements

### FR-1: Extract a new adapter-level test-support interface
Introduce a new interface (default name `IShoptetOrderTestClient`, adjustable during architecture review) in the adapter project (`Anela.Heblo.Adapters.ShoptetApi`, sibling to where `ShoptetOrderClient` is implemented) declaring exactly the 4 methods currently on `IEshopOrderClient` that have no Application-layer callers:
- `CreateOrderAsync`
- `DeleteOrderAsync`
- `GetRecentOrdersAsync`
- `ListByExternalCodePrefixAsync`

**Acceptance criteria:**
- New interface exists in the adapter project (not the Application project).
- Interface declares the same 4 method signatures (name, parameters, return type) as they currently exist on `IEshopOrderClient`, unchanged — this is a pure interface-segregation move, not a behavior change.

### FR-2: Shrink `IEshopOrderClient` to Application-used methods only
Remove the 4 methods listed in FR-1 from `IEshopOrderClient` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`), leaving the 7 methods that Application handlers actually call.

**Acceptance criteria:**
- `IEshopOrderClient` no longer declares `CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, or `ListByExternalCodePrefixAsync`.
- The remaining 7 methods on `IEshopOrderClient` are unchanged in signature and behavior.
- No Application-layer production code references the 4 removed methods via `IEshopOrderClient` (there should be none today per the brief, but this is verified as a regression check).

### FR-3: Concrete client implements both interfaces
`ShoptetOrderClient` (the concrete adapter implementation) implements both `IEshopOrderClient` and the new `IShoptetOrderTestClient`, so a single class continues to provide all 11 methods without duplicating implementation code.

**Acceptance criteria:**
- `ShoptetOrderClient` class declares both interfaces in its base list.
- All 11 original method implementations remain on `ShoptetOrderClient` with unchanged behavior.
- DI registration continues to resolve `IEshopOrderClient` to `ShoptetOrderClient` as before, and additionally registers `IShoptetOrderTestClient` (or the chosen name) resolving to the same or an equivalent `ShoptetOrderClient` instance/registration for consumers that need it.

### FR-4: Update integration test call sites to use the new interface
Update the integration test files that currently call the 4 methods via `IEshopOrderClient` (or via `ShoptetOrderClient` directly) to depend on the new adapter-level interface instead:
- `BlockOrderProcessingIntegrationTests.cs` (lines referencing `CreateOrderAsync`, `DeleteOrderAsync`)
- `ShoptetTestEnvironmentHydrationTests.cs` (lines referencing `CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`)
- `PickingListIntegrationTests.cs` (line referencing `GetRecentOrdersAsync`)

**Acceptance criteria:**
- None of the listed test files resolve/inject `IEshopOrderClient` solely to call the 4 relocated methods.
- Each listed test file compiles and passes using the new `IShoptetOrderTestClient` (or equivalent) for those calls.
- Tests that separately need `IEshopOrderClient` for its remaining 7 methods (if any) keep doing so unchanged.

### FR-5: No Application-layer mocking burden for the removed methods
Any existing Application-layer test that mocks `IEshopOrderClient` (e.g. unit tests around `BlockOrderProcessingHandler`) must no longer need to stub the 4 removed methods.

**Acceptance criteria:**
- Grep for mock/stub setups of `IEshopOrderClient` in Application-layer unit tests confirms no remaining setup code for the 4 removed methods after the change.
- All existing Application-layer unit tests that mock `IEshopOrderClient` continue to pass with the narrowed interface.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is a pure compile-time interface reorganization with no change to runtime call paths, HTTP behavior, or Shoptet API usage patterns.

### NFR-2: Security
No security impact — no new data exposure, no change to authentication/authorization, no change to what the Shoptet API is asked to do. Test-only lifecycle operations (create/delete test orders) remain scoped to test code exactly as before, just accessed through a differently-named interface.

### NFR-3: Backward compatibility / behavior preservation
This is a refactor with zero intended behavior change. Method signatures, implementations, and runtime semantics of all 11 methods must remain identical; only the interface(s) through which they are exposed changes.

## Data Model
No data model changes. No new entities, no schema changes, no persistence changes. This is purely a C# interface/type reorganization within the existing Shoptet adapter and Application layer.

## API / Interface Design
- **Removed from `IEshopOrderClient` (Application layer)**: `CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`.
- **New interface `IShoptetOrderTestClient` (adapter layer, exact name subject to architecture review)**: declares the same 4 methods, same signatures, moved verbatim.
- **`ShoptetOrderClient` (adapter layer, concrete class)**: implements both `IEshopOrderClient` and `IShoptetOrderTestClient`.
- **DI wiring**: `IEshopOrderClient` registration unchanged in effect; add a registration for `IShoptetOrderTestClient` pointing at the same concrete implementation (exact DI lifetime/registration approach to be confirmed during architecture review — likely same lifetime as the existing `IEshopOrderClient` registration).
- **Consumers**: Application handlers continue to depend on `IEshopOrderClient` only. Integration tests that need the 4 relocated methods depend on `IShoptetOrderTestClient` instead.

## Dependencies
- Depends on the existing `ShoptetOrderClient` implementation and its current DI registration in the adapter project — no external service or library dependency changes.
- Depends on the outcome of issue #3259 (already merged) which established the pattern of trimming `IEshopOrderClient` to only what Application code needs.

## Out of Scope
- No change to the Shoptet API client's HTTP behavior, retry logic, or error handling.
- No change to the 7 methods remaining on `IEshopOrderClient` — their signatures and behavior are untouched.
- No renaming or restructuring of the `ShoptetOrders` Application feature folder beyond removing the 4 method declarations from `IEshopOrderClient`.
- No consolidation or further ISP cleanup beyond the 4 methods explicitly identified in the brief.
- No changes to other Shoptet-related interfaces not named in the brief.

## Open Questions

None.

## Status: COMPLETE
