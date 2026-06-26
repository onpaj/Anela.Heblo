# Specification: Relocate `Cooling` enum to shared Domain namespace

## Summary
Move the `Cooling` enum from the Catalog module to the cross-module shared Domain namespace, so that Logistics, ShoptetOrders, Manufacture, Analytics, and the ShoptetApi/Flexi adapters no longer take a compile-time dependency on `Anela.Heblo.Domain.Features.Catalog` solely to reference a temperature-chain shipping classification. This is a pure refactor: the enum's name, members, integer values, and persisted string representation are unchanged.

## Background
The `Cooling` enum (values `None`, `L1`, `L2`) is a shared business concept used to classify how cool a shipment must be kept during expedition. It currently lives at `backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs` under namespace `Anela.Heblo.Domain.Features.Catalog`.

Per the module boundary rules in `docs/architecture/development_guidelines.md` (_"No direct access to another module's entities"_), this placement creates invisible coupling:

- **ShoptetOrders** uses `Cooling` on the `PackingOrder` contract and its `GetPackingOrder` response.
- **Logistics** uses `Cooling` on the `CarrierCoolingSetting` entity and the CarrierCooling use cases.
- **Manufacture** references `Cooling` in `Ingredient`/related domain types.
- **Analytics** references `Cooling` in `AnalyticsProduct`.
- **ShoptetApi adapter** uses `Cooling` in `ShoptetApiPackingOrderClient`, `ExpeditionProtocolData`, and the expedition list source.
- **Flexi adapter** uses `Cooling` in `FlexiProductAttributesQueryClient` and its `FlexiCoolingParser`.

Every one of these modules currently imports `Anela.Heblo.Domain.Features.Catalog` to access the enum, even though they have no other reason to depend on the Catalog domain. A rename or removal of `Cooling` in Catalog would simultaneously break all of them, and independent module deployment becomes impossible for the affected modules.

A shared Domain folder (`backend/src/Anela.Heblo.Domain/Shared/`) already exists and hosts similar cross-cutting types (`CurrencyCode`, `Result`, `Rag/DocumentType`). `Cooling` belongs there.

## Functional Requirements

### FR-1: Move `Cooling` enum file
Move the file `backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs` to `backend/src/Anela.Heblo.Domain/Shared/Cooling.cs`.

**Acceptance criteria:**
- The file no longer exists at `backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs`.
- A file with identical enum content (members `None = 0`, `L1 = 1`, `L2 = 2`) exists at `backend/src/Anela.Heblo.Domain/Shared/Cooling.cs`.
- The enum's name, member names, and integer values are unchanged.

### FR-2: Change namespace to `Anela.Heblo.Domain.Shared`
Update the namespace declaration of the moved file from `Anela.Heblo.Domain.Features.Catalog` to `Anela.Heblo.Domain.Shared` (matching the existing `CurrencyCode` placement).

**Acceptance criteria:**
- The moved `Cooling.cs` declares `namespace Anela.Heblo.Domain.Shared;`.
- No other type in the codebase still resides under namespace `Anela.Heblo.Domain.Features.Catalog` with name `Cooling`.

### FR-3: Update all consumers to import the shared namespace
Every file that references `Cooling` must use the new namespace. Where a file currently has `using Anela.Heblo.Domain.Features.Catalog;` purely for the `Cooling` enum, replace it with `using Anela.Heblo.Domain.Shared;`. Where a file already needs other `Catalog` types in addition to `Cooling`, add `using Anela.Heblo.Domain.Shared;` alongside the existing using (do not remove the Catalog using).

The known consumer set (verified via grep on `Cooling`):

**Domain layer**
- `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogProperties.cs` — Catalog's own type that also stores `Cooling`; add the new using (the file's own namespace remains `Anela.Heblo.Domain.Features.Catalog`).
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/CatalogAttributes.cs` — same treatment as above.
- `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/ICarrierCoolingRepository.cs`

**Application layer**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/PropertiesDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierCoolingRowDto.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierGroupDto.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (only if it directly references `Cooling`)

**API layer**
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs`

**Adapters**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs`

**Persistence**
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingSettingConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingRepository.cs`
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

**Tests**
- `backend/test/Anela.Heblo.Tests/Controllers/CarrierCoolingControllerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs`
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs`

The implementer must re-run a project-wide grep for the symbol `Cooling` after editing to catch any consumer added between this spec and execution.

**Acceptance criteria:**
- `grep -r "Anela\.Heblo\.Domain\.Features\.Catalog" backend/` returns no hit whose sole purpose is to reach the `Cooling` enum (every remaining hit must reference at least one other Catalog type).
- The solution builds (`dotnet build`) with no errors and no new warnings.
- Every test file referencing `Cooling` compiles against the new namespace.

### FR-4: Preserve persisted representation and migrations
The EF Core column for `Cooling` (in `CarrierCoolingSettingConfiguration`) is configured with `HasConversion<string>()` and `HasMaxLength(10)`. The stored values are the enum member names (`None`, `L1`, `L2`). Existing EF migrations under `backend/src/Anela.Heblo.Persistence/Migrations/` reference `Cooling` only as a property name in snapshot model strings — they do not import the enum's namespace.

**Acceptance criteria:**
- No EF migration files are edited.
- No new EF migration is generated as a result of this change.
- `ApplicationDbContextModelSnapshot.cs` does not require regeneration (verified by running `dotnet ef migrations has-pending-model-changes` or equivalent).
- The persisted column type, length, and string values for `Cooling` are unchanged.

### FR-5: Frontend / generated client unchanged
The TypeScript client is generated from the OpenAPI schema. The C# namespace of the enum does not affect the generated TypeScript output — only the enum's JSON/string surface does, and that is unchanged.

**Acceptance criteria:**
- `frontend/src/api/generated/api-client.ts` produces an identical diff (semantically) after regeneration.
- No manual frontend edits are required.

## Non-Functional Requirements

### NFR-1: Behavior preservation
This is a refactor with zero functional behavior change. The enum's runtime identity (`Cooling.None`, `Cooling.L1`, `Cooling.L2`), integer values, name-based persistence, JSON serialization, and OpenAPI surface must all be byte-equivalent before and after.

### NFR-2: Build and test gates
- `dotnet build` succeeds with no errors and no new warnings.
- `dotnet format` passes (no remaining style violations on edited files).
- All affected unit and integration tests pass: at minimum the test files enumerated in FR-3.
- E2E suite is not required to run as part of this refactor (E2E runs nightly, not in PR CI).

### NFR-3: Module boundary alignment
After this change, no module outside `Catalog` imports `Anela.Heblo.Domain.Features.Catalog` solely for `Cooling`. The Catalog module itself may keep its own `Catalog` namespace usings since `CatalogProperties` and `CatalogAttributes` legitimately consume `Cooling` from Shared.

### NFR-4: Atomicity
The change ships as a single commit (or single PR) — partial states where some references point to Catalog and others to Shared must not appear in main. Because every consumer file changes simultaneously, the refactor is naturally atomic per build.

## Data Model
No data model changes.

- `CarrierCoolingSetting` entity: `Cooling` property remains of type `Cooling` enum; column remains `varchar(10)` storing the enum member name.
- `CatalogProperties.Cooling`, `Ingredient.Cooling`, `AnalyticsProduct.Cooling`: type and default value unchanged.
- `PackingOrder.Cooling`: type unchanged.
- `ExpeditionProtocolData.Cooling`: type unchanged.

## API / Interface Design
No API contract changes.

- HTTP endpoints under `/api/carrier-cooling/*` continue to accept and return the same JSON shape for the `Cooling` field.
- The OpenAPI schema continues to expose `Cooling` as a string enum with values `None`, `L1`, `L2`.
- The generated C# client and generated TypeScript client need no manual edits.
- MediatR request/response signatures (`SetCarrierCoolingRequest`, `GetCarrierCoolingMatrixResponse`, etc.) keep identical property types — only the C# `using` directive at the top of the file changes.

## Dependencies
- No new NuGet packages.
- No new project references.
- The `Anela.Heblo.Domain` project already exists and already has the `Shared/` folder; no .csproj changes.
- The change is self-contained within `backend/`.

## Out of Scope
- **Renaming `Cooling`** or any of its members.
- **Changing enum values** (`None=0`, `L1=1`, `L2=2`).
- **Extracting other shared types** from Catalog (e.g., `Carriers`, `DeliveryHandling`) — those are separate findings if relevant; this spec covers `Cooling` only.
- **Touching the Catalog module's own `CatalogProperties.Cooling` or `CatalogAttributes` semantics** — only their `using` directives are adjusted so they continue to compile.
- **Editing EF migrations** — historical migrations are immutable.
- **Frontend code changes** — no consumer-visible change.
- **Adding architecture tests** (e.g., ArchUnit-style assertions that Catalog isn't depended on for `Cooling`). Worth considering, but separate.
- **Splitting `Domain/Shared`** into sub-namespaces (e.g., `Anela.Heblo.Domain.Shared.Logistics`). The existing flat `Anela.Heblo.Domain.Shared` namespace is used to match `CurrencyCode`.

## Open Questions
None.

## Status: COMPLETE