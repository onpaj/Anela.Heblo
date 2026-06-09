# Architecture Review: Replace Misleading Interface Injection in ShoptetApiExpeditionListSource

## Skip Design: true

Pure backend constructor signature refactor inside one adapter assembly. No UI, no visual changes, no new components.

## Architectural Fit Assessment

This change is a direct, near-mechanical follow-up to the already-completed `ShoptetApiPackingOrderClient` refactor (see `docs/superpowers/plans/2026-05-24-honest-dependency-shoptetapi-packing-order-client.md`), which explicitly flagged the identical pattern in `ShoptetApiExpeditionListSource.cs:42` as "out of scope; file a follow-up." This *is* that follow-up.

It aligns with the project's existing Clean Architecture posture: `IEshopOrderClient` is the contract used by *application-layer* consumers (`BlockOrderProcessingHandler`, `ScanPackingOrderHandler`) that genuinely depend on its narrow surface. `ShoptetApiExpeditionListSource` lives in the **same adapter assembly** as `ShoptetOrderClient`, calls methods (`GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, `SetAdditionalFieldAsync`) that exist only on the concrete client, and is the *only* in-process consumer of those methods. Depending on the concrete type here is appropriate and matches the pattern just established for `ShoptetApiPackingOrderClient`.

**Integration points:**
1. `ShoptetApiExpeditionListSource` constructor signature.
2. DI registration in `ShoptetApiAdapterServiceCollectionExtensions.cs`.
3. Two test files that already construct the SUT directly.

## Proposed Architecture

### Component Overview

```
[Application Layer]
    IPickingListSource ──┐
                         │  resolved by DI
                         ▼
[Adapter: Anela.Heblo.Adapters.ShoptetApi]
    ShoptetApiExpeditionListSource
        ├── ShoptetOrderClient      ◄── concrete dependency (changed)
        ├── ICatalogRepository
        ├── ICarrierCoolingRepository
        ├── IGiftSettingRepository
        ├── TimeProvider
        └── ILogger<…>

[DI Composition Root]
    AddHttpClient<ShoptetOrderClient>(…)                       ◄── already concrete; unchanged
    AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())  ◄── already in place; unchanged
    AddTransient<IPickingListSource, ShoptetApiExpeditionListSource>()                  ◄── unchanged
```

### Key Design Decisions

#### Decision 1: Depend on concrete `ShoptetOrderClient`, not a narrowed interface

**Options considered:**
- (A) Inject `ShoptetOrderClient` directly. (Chosen.)
- (B) Widen `IEshopOrderClient` to include the three additional methods.
- (C) Introduce a new narrow interface (e.g., `IExpeditionOrderDetailClient`) that the adapter implements.

**Chosen approach:** Option A.

**Rationale:**
- Both consumer and dependency live in the same adapter assembly, so a contract abstraction provides no decoupling benefit (Clean Architecture's "depend on abstractions only when needed for decoupling").
- Option B pollutes a contract used by other application-layer handlers with methods they don't need (ISP violation).
- Option C is YAGNI for a single in-assembly consumer. If a second consumer appears, extract then.
- This is the same decision the team made and validated for `ShoptetApiPackingOrderClient` in May.

#### Decision 2: No DI changes required

**Options considered:**
- (A) Leave DI alone — `ShoptetOrderClient` is already registered as a concrete type via `AddHttpClient<ShoptetOrderClient>(…)` (line 37 of `ShoptetApiAdapterServiceCollectionExtensions.cs`), and `IEshopOrderClient` is forwarded via a transient factory (line 43). (Chosen.)
- (B) Add a new registration.

**Chosen approach:** Option A.

**Rationale:** The DI shape was already inverted during the prior `ShoptetApiPackingOrderClient` refactor specifically to enable both concrete- and interface-based resolution. The new constructor signature for `ShoptetApiExpeditionListSource` is already satisfiable by the container as-is. Spec FR-2 should be treated as a verification step, not a change.

#### Decision 3: No test edits required

**Options considered:**
- (A) Leave tests unchanged. (Chosen.)
- (B) Edit test helpers.

**Chosen approach:** Option A.

**Rationale:** Both `ShoptetApiExpeditionListSourceTests` (`backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/`) and `ShoptetApiExpeditionListSource_CoolingMarkerTests` (`backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/`) already construct a real `ShoptetOrderClient` (wrapped around `FakeDelegatingHandler` / Moq `HttpMessageHandler`) and pass it to the SUT. Changing the constructor parameter from `IEshopOrderClient` to `ShoptetOrderClient` is **source-compatible** — the test code requires no edits. Spec FR-3 should similarly be treated as a "must compile and pass" verification, not an edit task.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify only:

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
  - Line 34: change constructor parameter type from `IEshopOrderClient` to `ShoptetOrderClient`.
  - Line 42: replace `_client = (ShoptetOrderClient)client;` with `_client = client;`.
  - Lines 20–21: remove the now-stale "ShoptetOrderClient is the only implementation… safe to cast" comment — it describes a workaround that no longer exists.
  - Remove the `using Anela.Heblo.Application.Features.ShoptetOrders;` import **only if** `dotnet format` removes it as unused. Do not hand-edit the using list (project rule: surgical changes).

Files validated (must continue to compile and pass; **do not edit**):

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — DI registration is already correct.
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` — already passes a real `ShoptetOrderClient`.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — same.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — interface is unchanged.

### Interfaces and Contracts

**No public contract changes.**

- `IEshopOrderClient` is untouched. Other consumers (`BlockOrderProcessingHandler`, `ScanPackingOrderHandler`) continue to depend on it.
- `IPickingListSource` (the application-layer contract `ShoptetApiExpeditionListSource` implements) is untouched.
- Only the internal constructor signature of `ShoptetApiExpeditionListSource` changes.

### Data Flow

Unchanged. The class continues to:

1. Resolve from DI with the same set of dependencies (only the parameter type differs).
2. Fetch order lists via `_client.GetOrdersByStatusAsync` (paginated).
3. Fetch each order detail via `_client.GetExpeditionOrderDetailAsync`.
4. Optionally PATCH cooling marker via `_client.SetAdditionalFieldAsync`.
5. Optionally update status via `_client.UpdateStatusAsync`.

The removed cast was a no-op at runtime (the DI factory always returns a `ShoptetOrderClient`), so production behavior is byte-identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A test or production consumer (not yet found) passes a non-`ShoptetOrderClient` implementation of `IEshopOrderClient` to this constructor | Low | The full-solution `dotnet build` after the change catches this at compile time. No grep hit beyond the two test files already verified. |
| DI resolution fails at boot because the concrete type isn't registered | Very low | `AddHttpClient<ShoptetOrderClient>(…)` already registers the concrete type (line 37 of the DI module). Confirm with `dotnet build` of the API project; a startup integration test, if present (`WebApplicationFactory<Program>`), is a stronger check. |
| Stale comment about the cast misleads future readers | Low | Remove the comment block on lines 20–21 of `ShoptetApiExpeditionListSource.cs` as part of the same commit. |
| Unused `using Anela.Heblo.Application.Features.ShoptetOrders;` lingers | Low | Let `dotnet format` decide. Don't hand-edit. |

## Specification Amendments

**FR-2 (DI registration) — reframe as verification only.** The spec wording ("ensure `ShoptetOrderClient` is resolvable… verify the existing registration still satisfies") is already correct in intent, but should be made explicit: **no edit to `ShoptetApiAdapterServiceCollectionExtensions.cs` is expected**. The registration was put in its current shape during the `ShoptetApiPackingOrderClient` refactor specifically to support exactly this scenario. The acceptance criterion "No new registration is added unless required" is already met by *no change*.

**FR-3 (Unit tests) — reframe as verification only.** Both relevant test files already construct the SUT with a concrete `ShoptetOrderClient`. The change is source-compatible. The acceptance criteria can be satisfied by **running** the existing tests; no test code edits should be needed. If an edit *is* required, that's a signal that the test was reaching for something the new signature accidentally prevents, and the developer should stop and report rather than work around it.

**Additional cleanup (not in spec).** The current class has a comment (lines 20–21) explaining the cast as "safe to cast within this adapter assembly." Once the cast is gone, the comment is dead and misleading. Remove it as part of the same commit — this is within the spirit of FR-4 (behavior-preserving) and is the minimal surgical follow-through.

## Prerequisites

None. No migrations, no config changes, no infrastructure work. The DI registration is already in the required shape (verified at `ShoptetApiAdapterServiceCollectionExtensions.cs:37-43`).

Validation gates before commit:
- `dotnet build backend/Anela.Heblo.sln` — green.
- `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ShoptetApiExpeditionListSource"` — green (covers both test files).
- `dotnet test backend/Anela.Heblo.sln` — green (full suite, to catch any unexpected consumer).
- `dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj` — no diffs after run.