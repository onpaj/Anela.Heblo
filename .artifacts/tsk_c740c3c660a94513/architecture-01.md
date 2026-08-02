# Architecture assessment — Flexi: `ILotsClient` registered twice with conflicting lifetimes

## Verdict

**Approved as designed, no changes required.** The plan and design in `plan-01.md` / `design-01.md` are correct, minimal, and consistent with this codebase's DI invariants. I re-verified every load-bearing claim against current source rather than trusting the prior artifacts at face value; all of them hold.

## Re-verification against current code

- `FlexiAdapterServiceCollectionExtensions.cs:73` and `:86` do register the identical pair today — `Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient → FlexiLotsClient`, Singleton then Scoped. Confirmed by direct read of the current file (line numbers unchanged from the evidence).
- The "same short name, two distinct types" claim is real, not a false alarm: `FlexiLotsClientTests.cs:4` imports `Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate.ILotsClient` explicitly via `using`, and `FlexiLotsClient.cs` itself references that SDK type fully-qualified in its constructor — confirming the Domain `ILotsClient` (bound via the `using Anela.Heblo.Domain.Features.Catalog.Lots;` on line 23 of the extensions file) is what both line 73 and line 86 resolve to. There is no legitimate "two different services" interpretation here.
- `FlexiLotsClient` is a pure stateless wrapper: one constructor parameter (the SDK's `ILotsClient`), no mutable fields, no per-request state, all logic is a synchronous translation in `GetAsync`. This independently confirms the plan's captive-dependency analysis — there is no scoped state that Singleton would incorrectly pin.
- Sibling registrations in the same file corroborate Singleton as the established convention for this class of stateless Flexi read-wrapper: `ICatalogAttributesClient`, `ICatalogSalesClient`, `IConsumedMaterialsClient`, `IErpStockClient`, `IPurchaseHistoryClient`, `IManufactureHistoryClient`, `ISupplierRepository` are all `AddSingleton` (lines 61–70). The Scoped registrations clustered at lines 78–88 are for a structurally different group — manufacture-domain services and clients with per-request/document-scoped semantics (`IFlexiManufactureTemplateService`, `IFlexiIngredientRequirementAggregator`, `IManufactureClient`, etc.). `ILotsClient` at line 86 is misplaced among them, not a deliberate outlier.

## Alignment with documented invariants

`docs/architecture/development_guidelines.md` § Dependency Injection Patterns (lines 116–148) states the DI-binding rule this defect violates and cites the exact precedent this task's evidence also references: the `IDqtRunRepository` duplicate-registration incident, now guarded by `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`. That existing test is the template to follow, and it validates the design's proposed approach almost line for line:

- It builds a bare `ServiceCollection`, calls the module's registration extension with an in-memory `IConfiguration`, and asserts directly against `services` (the `IServiceCollection`) via LINQ — it does **not** call `BuildServiceProvider()`.
- The design's proposed `FlexiAdapterLotsClientRegistrationTests` should follow the same shape: register, then assert on `ServiceDescriptor` properties (`ServiceType`, `Lifetime`, `ImplementationType`) without building/resolving. This avoids needing to satisfy `AddFlexiBee`'s full dependency graph (HTTP clients, credentials) — confirmed safe by `FlexiIntegrationTestFixture.cs:37`, which already calls `AddFlexiAdapter(Configuration)` with only user-secrets/env configuration and no live FlexiBee credentials required at *registration* time (resolution/HTTP calls are what need real credentials, not `AddFlexiAdapter` itself).

This makes the design's test plan not just plausible but a direct, low-risk application of an existing, proven pattern in this repo — no new test infrastructure or fixture is needed.

## Points to hold the implementer to

1. **Delete line 86 only, keep line 73 fully qualified as-is.** The design's instinct to leave the fully-qualified `Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient` on line 73 untouched (rather than simplifying it to the short form as a drive-by cleanup) is correct per this repo's "surgical changes" rule — do not let the implementer touch it beyond removing the duplicate.
2. **New test must not call `BuildServiceProvider()` / `GetRequiredService()`.** If the implementer reaches for `FlexiIntegrationTestFixture` instead of a bare `ServiceCollection`, that would turn a unit-level DI guard into something resembling an integration test and risks failing in CI without FlexiBee credentials. Keep it inspecting descriptors only, matching `PersistenceModuleTests`.
3. **Test location**: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Lots/` is correct — sibling to `FlexiLotsClientTests.cs`, consistent with the per-module test-folder convention already used in this test project.
4. No consumer changes are needed or should be made — `CatalogDataRefreshService` and `FlexiLotLoader` resolve `ILotsClient` through standard DI and are unaffected by which of the two (now one) descriptors serves them, since `FlexiLotsClient` has no scope-dependent behavior.

## Risks

None identified beyond what the plan already flags. This is a same-file, two-line diff (one deletion + one new test file) with no public contract, schema, or cross-module surface. The only failure mode worth a build/test pass check is a stale reference to the deleted Scoped descriptor anywhere else in the codebase — already checked and confirmed absent.

## Prerequisites before implementation

None. The investigation in `plan-01.md` (SDK lifetime probe) and this re-verification together fully de-risk the change; implementation can proceed directly per the design.
