# Design — Flexi: `ILotsClient` registered twice with conflicting lifetimes

No UI is involved; this section is omitted per instructions.

## Component design

### 1. `FlexiAdapterServiceCollectionExtensions.AddFlexiAdapter` (changed)

File: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

Responsibility: composition root for the Flexi adapter module — one `IServiceCollection` extension method that registers every Flexi-backed implementation of a Domain port exactly once, with the lifetime appropriate to that implementation.

Current state (defect): registers the Domain port `Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient → FlexiLotsClient` twice:

```csharp
73:  services.AddSingleton<Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient, FlexiLotsClient>();
...
86:  services.AddScoped<ILotsClient, FlexiLotsClient>();
```

Target state: a single registration, Singleton, at (approximately) line 73:

```csharp
services.AddSingleton<ILotsClient, FlexiLotsClient>();
```

Change boundary — exactly two edits, nothing else in the file moves:

1. Delete line 86 (`services.AddScoped<ILotsClient, FlexiLotsClient>();`) from the manufacture-adjacent Scoped registration block. The surrounding lines 82–88 (`IFlexiLotLoader`, `IProductWeightClient`, `IDepartmentClient`, …) are untouched — they are unrelated services that happen to sit next to the deleted line.
2. Line 73 stays as the sole registration. Per the plan's open question, default to leaving it fully qualified (`Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient`) to keep the diff to a one-line deletion and avoid touching a line that isn't itself defective. (Simplifying to the short form is acceptable but not required — implementer's call, not a design requirement.)

No signature, namespace, or `using` changes. No consumer (`CatalogDataRefreshService`, `FlexiLotLoader`) changes — both already resolve `ILotsClient` through DI and are agnostic to which of the two (now one) descriptors served them.

Rationale for Singleton over Scoped (carried from the plan, restated for the record): every sibling Flexi read-client wrapper in this file that follows the same shape (stateless wrapper around a FlexiBee SDK client) is Singleton (`ICatalogAttributesClient`, `ICatalogSalesClient`, `IConsumedMaterialsClient`, `IErpStockClient`, `IPurchaseHistoryClient`, `IManufactureHistoryClient`, `ISupplierRepository`). `FlexiLotsClient` fits that shape exactly — a single constructor dependency on the SDK's `ILotsClient`, no per-request state — and the SDK's own `AddFlexiBee` registers that wrapped client as Singleton, so there is no captive-dependency mismatch.

### 2. Regression test (new): DI single-registration guard for `ILotsClient`

Purpose: make the exact defect class in this task (two descriptors for one service, silently tolerated by the container) fail a test instead of requiring a manual arch review to catch it again — this mirrors the guideline's stated concern about this defect class recurring (`IDqtRunRepository` precedent).

Placement: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Lots/` (sibling to the existing `FlexiLotsClientTests.cs` in that folder — confirmed to exist), e.g. `FlexiAdapterLotsClientRegistrationTests.cs`.

Scope and mechanics:
- Build a bare `ServiceCollection`, call `services.AddFlexiAdapter(configuration)` with an empty/minimal `IConfiguration` (no live FlexiBee credentials needed — this test only inspects the resulting `IServiceCollection`'s descriptors, it never calls `BuildServiceProvider()`/`GetRequiredService()`, so no HTTP client is constructed and no real Flexi endpoint is touched). This keeps it a fast unit test, not an integration test — do not reuse `FlexiIntegrationTestFixture` (that fixture builds and resolves the provider against live/user-secret configuration, which is unnecessary here and would misclassify this as an integration concern).
- Assert exactly one `ServiceDescriptor` in the collection has `ServiceType == typeof(Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient)`.
- Assert that descriptor's `Lifetime == ServiceLifetime.Singleton` and `ImplementationType == typeof(FlexiLotsClient)`.

Interface: this is a self-contained xUnit test class with no public surface beyond the test methods themselves; it has no interaction with other components besides calling the existing public `AddFlexiAdapter` extension method.

## Data schemas

Not applicable — no request/response DTOs, database schema, or event payloads are touched by this change. It is confined to the in-process DI composition root.
