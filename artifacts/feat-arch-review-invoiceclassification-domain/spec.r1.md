# Specification: InvoiceClassification Domain DTO Separation

## Summary
The `InvoiceClassification` module currently exposes Domain-layer types named with the `Dto` suffix (`AccountingTemplateDto`, `ReceivedInvoiceDto`, `ReceivedInvoiceItemDto`) directly through Application-layer response objects. This couples the public API contract to the FlexiBee external service data shape and violates the project's Clean Architecture / Vertical Slice guidelines. This work introduces dedicated Application contracts, isolates the external-shaped types behind the Adapters boundary, and adds explicit mapping in the handlers.

## Background
Two response classes consumed by the frontend (and the auto-generated TypeScript OpenAPI client) currently return Domain-named types whose shape is driven by what the FlexiBee SDK returns:

- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesResponse.cs` exposes `List<AccountingTemplateDto>` defined in `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/AccountingTemplateDto.cs`.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsResponse.cs` exposes `ReceivedInvoiceDto?` defined in `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceDto.cs` (including a nested `ReceivedInvoiceItemDto`).

Both types are materialized by `FlexiInvoiceClassificationsClient` and `FlexiReceivedInvoicesClient` in the Adapters layer. The project guidelines (`docs/architecture/development_guidelines.md`, `docs/architecture/filesystem.md`) require:

- API DTOs live in `Application/Features/<Module>/Contracts/`, never in `Domain/`.
- The `Domain` layer contains entities and value objects, not transfer objects.
- The API contract must be insulated from external-service schema changes.

An `InvoiceClassificationMappingProfile.cs` AutoMapper profile already exists in the Application layer and is the established mapping seam for this module.

The change is a structural refactor with no behavioral change for end users. The generated OpenAPI client will continue to surface the same fields, only the C# and TypeScript type names will change (`AccountingTemplateDto` → `AccountingTemplateDto` in `Contracts/`, etc. — see FR-3 for naming policy).

## Functional Requirements

### FR-1: Introduce Application Contracts for invoice classification responses
Create dedicated DTO classes in `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/` representing the API surface for these two use cases. These classes own the public field names, types, and nullability of the API contract.

**Acceptance criteria:**
- A class `AccountingTemplateDto` exists under `Application/Features/InvoiceClassification/Contracts/`.
- A class `ReceivedInvoiceDto` exists under `Application/Features/InvoiceClassification/Contracts/`, with its nested `ReceivedInvoiceItemDto` either in the same file or a sibling file in the same folder.
- All three are plain C# classes (not `record`), to keep the OpenAPI generator stable (per `CLAUDE.md`: "DTOs are classes, never C# records").
- Each property carries the same JSON name and nullability presently observed in the generated OpenAPI schema for the affected endpoints.
- Files follow the same XML-doc / property-comment style used by sibling DTOs in the module.

### FR-2: Reshape Domain types or relocate them to Adapters
The current `Domain/Features/InvoiceClassification/AccountingTemplateDto.cs`, `ReceivedInvoiceDto.cs`, and `ReceivedInvoiceItemDto.cs` must no longer reside in `Domain/` with a `*Dto` suffix.

**Acceptance criteria:**
- Either:
  - **(Option A — preferred when used by Domain logic)** Rename the types to domain value objects without the `Dto` suffix (e.g. `AccountingTemplate`, `ReceivedInvoice`, `ReceivedInvoiceItem`), keep them in `Domain/Features/InvoiceClassification/`, and ensure they expose only Domain-meaningful fields. Or
  - **(Option B — preferred when only used as a transport from Flexi)** Move them to `backend/src/Anela.Heblo.Adapters.Flexi/.../InvoiceClassification/Contracts/` (or the existing adapter contracts location) and rename to make the source explicit (e.g. `FlexiAccountingTemplate`, `FlexiReceivedInvoice`).
- The chosen option is applied consistently across all three types.
- The repository / client interfaces in `Domain/Features/InvoiceClassification/` (e.g. `IInvoiceClassificationClient`, `IReceivedInvoicesClient`) return the chosen type and namespace.
- No type with the `Dto` suffix remains in the `Anela.Heblo.Domain` project under `InvoiceClassification`.

### FR-3: Map external/domain types to Application contracts in handlers
Each Application handler that previously passed the Domain type through must convert it to the new Application contract before returning.

**Acceptance criteria:**
- `GetAccountingTemplatesHandler` produces `GetAccountingTemplatesResponse` whose `Templates` collection is of the new `Application/.../Contracts/AccountingTemplateDto` type.
- `GetInvoiceDetailsHandler` produces `GetInvoiceDetailsResponse` whose `Invoice` is of the new `Application/.../Contracts/ReceivedInvoiceDto?` type.
- Mapping is performed by AutoMapper using profiles in `InvoiceClassificationMappingProfile.cs` (extended as needed), not by hand-written field-by-field assignment inline in the handler.
- AutoMapper profile registers maps from the renamed/relocated source type to the new Application contract type for: `AccountingTemplate(Dto)` → contract, `ReceivedInvoice(Dto)` → contract, `ReceivedInvoiceItem(Dto)` → contract.
- AutoMapper configuration validation (`configuration.AssertConfigurationIsValid()`) passes; covered by an existing or new unit test.

### FR-4: Preserve public API contract (no breaking changes for the frontend)
The JSON shape returned by both endpoints must remain byte-compatible with the current contract so that the auto-generated TypeScript client and any existing frontend code continue to function without manual edits beyond regeneration.

**Acceptance criteria:**
- For each field currently present in the OpenAPI schema for `GET /api/invoice-classification/accounting-templates` and `GET /api/invoice-classification/invoices/{id}` (or equivalent route), the same JSON name, type, and nullability exists after the refactor.
- The regenerated `frontend/src/api/generated/` TypeScript client compiles without changes to any non-generated frontend file.
- A snapshot or contract test (added if not already present) asserts the OpenAPI schema for both endpoints matches a committed reference, OR a manual diff of the generated swagger JSON is reviewed and confirms no field rename/removal.

### FR-5: Update all references and tests
All call sites that referenced the old Domain `*Dto` types must be updated to the new type.

**Acceptance criteria:**
- `dotnet build` succeeds with zero warnings about obsolete or missing types.
- All unit, integration, and handler tests under `backend/test/` that reference these types compile and pass.
- No `using Anela.Heblo.Domain.Features.InvoiceClassification;` import remains where the only purpose was to access one of the relocated/renamed types.
- The frontend regenerated client builds (`cd frontend && npm run build` succeeds).

## Non-Functional Requirements

### NFR-1: Performance
No measurable change in endpoint latency. Mapping is in-memory object-to-object and runs once per response. No additional database calls or external API calls are introduced.

### NFR-2: Security
No change in authorization. Endpoints continue to require the same authenticated user role as today. No PII fields are added or removed.

### NFR-3: Maintainability / Architectural compliance
- Zero types with the `Dto` suffix remain under `Anela.Heblo.Domain` after this change (project-wide grep should confirm; if other modules already violate this, scope is limited to InvoiceClassification — note as Open Question).
- The InvoiceClassification module's layer boundaries match the rules in `docs/architecture/development_guidelines.md` and `docs/architecture/filesystem.md`.
- The Application contracts folder layout matches the convention already used by other modules (verify against at least one peer module).

### NFR-4: Testability
- AutoMapper profile configuration validation must be exercised by a unit test (extending the existing pattern if present).
- Each handler's happy-path test asserts the response uses the new contract types.

## Data Model

The logical data does not change. Field-by-field, the contracts should mirror today's exposed shape. The relocation/rename map is:

| Current (Domain) | New (Application contract) | New (Domain/Adapters representation) |
|---|---|---|
| `Anela.Heblo.Domain.Features.InvoiceClassification.AccountingTemplateDto` | `Anela.Heblo.Application.Features.InvoiceClassification.Contracts.AccountingTemplateDto` | `AccountingTemplate` (Domain) or `FlexiAccountingTemplate` (Adapters) per FR-2 |
| `Anela.Heblo.Domain.Features.InvoiceClassification.ReceivedInvoiceDto` | `Anela.Heblo.Application.Features.InvoiceClassification.Contracts.ReceivedInvoiceDto` | `ReceivedInvoice` (Domain) or `FlexiReceivedInvoice` (Adapters) |
| `Anela.Heblo.Domain.Features.InvoiceClassification.ReceivedInvoiceItemDto` | `Anela.Heblo.Application.Features.InvoiceClassification.Contracts.ReceivedInvoiceItemDto` | `ReceivedInvoiceItem` (Domain) or `FlexiReceivedInvoiceItem` (Adapters) |

Field-level contents are preserved verbatim from the current files; this spec does not introduce, rename, or remove any field.

## API / Interface Design

### Affected endpoints (no route, verb, or JSON shape change)
- `GetAccountingTemplates` → returns `GetAccountingTemplatesResponse { Templates: AccountingTemplateDto[] }` (contract type now from `Application/.../Contracts/`).
- `GetInvoiceDetails` → returns `GetInvoiceDetailsResponse { Invoice: ReceivedInvoiceDto? }` (contract type now from `Application/.../Contracts/`).

### Internal interfaces
- `IInvoiceClassificationClient` (or equivalent in Domain) returns the new Domain/Adapters type, never the Application contract type.
- `FlexiInvoiceClassificationsClient` and `FlexiReceivedInvoicesClient` continue to build the same SDK-shaped object; only the C# type name changes if Option B is chosen.

### AutoMapper profile additions
- `InvoiceClassificationMappingProfile` registers:
  - `CreateMap<AccountingTemplate /* or FlexiAccountingTemplate */, Contracts.AccountingTemplateDto>()`
  - `CreateMap<ReceivedInvoice /* or FlexiReceivedInvoice */, Contracts.ReceivedInvoiceDto>()`
  - `CreateMap<ReceivedInvoiceItem /* or FlexiReceivedInvoiceItem */, Contracts.ReceivedInvoiceItemDto>()`

## Dependencies

- **AutoMapper** — existing dependency, `InvoiceClassificationMappingProfile.cs` is the extension point.
- **FlexiBee SDK** — unchanged; only the C# type that wraps its output is repositioned/renamed.
- **OpenAPI client generation pipeline** — the auto-generated TypeScript client under `frontend/src/api/generated/` must be regenerated as part of the build (`docs/development/api-client-generation.md`). No manual edits expected; a regeneration step in the PR is required.
- **InvoiceClassification feature flag** (if any) — none assumed; check `docs/development/feature-flags.md`. If a flag exists, this refactor is behind the same flag scope (no new flag introduced).

## Out of Scope

- Renaming or restructuring DTOs in modules other than `InvoiceClassification`, even if they exhibit the same violation. A repository-wide cleanup is tracked separately.
- Changing the JSON field names, casing, or nullability exposed to the frontend.
- Refactoring the FlexiBee adapter implementation, error handling, or caching.
- Introducing a generic `IExternalDto` marker, base class, or convention test framework. (Only the surgical fix is in scope.)
- Adding new endpoints, new fields, or new business rules to `InvoiceClassification`.
- Changing the AutoMapper profile registration mechanism or DI wiring.

## Open Questions

None.

## Status: COMPLETE