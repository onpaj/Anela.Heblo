# Specification: Align GiftSettings Module Boundaries with Logistics

## Summary
The `GiftSettings` feature currently has its domain (`Domain/Features/Logistics/GiftSettings/`) and persistence (`Persistence/Logistics/GiftSettings/`) code nested under the `Logistics` module, while its application layer (`Application/Features/GiftSettings/`) lives as an independent top-level module. This specification defines the refactor to relocate `Application/Features/GiftSettings/` into `Application/Features/Logistics/UseCases/GiftSettings/`, matching the pattern already established by the `GiftPackageManufacture` sub-feature, so all four layers agree that GiftSettings is owned by Logistics. No domain, persistence, database, or public HTTP contract changes are required.

## Background
This work item originates from a daily architecture-review finding (`artifacts/feat-3607/brief.md`, filed 2026-07-12) that identified a layer-boundary mismatch: `SetGiftSettingHandler` (in the standalone `Application/Features/GiftSettings/` folder) imports `Anela.Heblo.Domain.Features.Logistics.GiftSettings`, exposing a hidden coupling to Logistics that the Application-layer folder structure denies. The brief offered two remediation options:
- **Option A**: promote GiftSettings to a fully standalone module (move domain + persistence out of `Logistics/` too).
- **Option B**: merge `Application/Features/GiftSettings/` into `Application/Features/Logistics/`, matching the existing domain/persistence nesting.

**Decision: Option B.** Reasoning, based on inspecting this repository's actual conventions rather than the brief's summary alone:

1. **Direct structural precedent already exists and was chosen deliberately.** `GiftPackageManufacture` is the closest analog to `GiftSettings` — another Logistics sub-feature with its own settings/business logic. Its domain lives at `Domain/Features/Logistics/GiftPackageManufacture/`, its persistence at `Persistence/Logistics/GiftPackageManufacture/`, and — critically — its **application layer also lives nested**, at `Application/Features/Logistics/UseCases/GiftPackageManufacture/`, with its own `GiftPackageManufactureModule.cs` registered as a standalone call (`services.AddGiftPackageManufactureModule();`) in `ApplicationModule.cs`, sitting right next to `services.AddGiftSettingsModule();`. This is exactly the target shape Option B describes, and it is a live, working, tested pattern — not a hypothetical one.

2. **The architecture test suite already models "Logistics" as three co-located namespace prefixes.** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` repeatedly defines the Logistics module boundary as the trio `"Anela.Heblo.Domain.Features.Logistics"`, `"Anela.Heblo.Application.Features.Logistics"`, `"Anela.Heblo.Persistence.Logistics"` (see the `Catalog -> Logistics`, `ExpeditionList -> Logistics`, and `ShoptetApi Adapters -> Logistics` boundary tests). The same file's `ShoptetApiAdaptersLogisticsAllowlist` already allowlists `Anela.Heblo.Domain.Features.Logistics.GiftSettings.IGiftSettingRepository` and `...GiftSetting` as Logistics-owned types being referenced from outside. In other words, the enforced architecture tests already treat GiftSettings' domain types as part of "Logistics" — moving the application layer under `Application.Features.Logistics` closes the one layer these tests don't yet cover, rather than fighting the test suite's existing model.

3. **A competing, weaker precedent exists and was consciously ruled out.** `CarrierCooling` and `WeatherForecast` also have domain/persistence nested under `Logistics` (`Domain/Features/Logistics/CarrierCooling` etc.) while keeping a fully standalone Application module and dedicated controller — the same "split" GiftSettings has today. If this split were followed instead, it would argue for Option A. It is called out explicitly here as an **out-of-scope, pre-existing sibling inconsistency** (see Open Questions) rather than silently ignored, but it does not outweigh point 1: `GiftPackageManufacture` is the more specific, more recently established, and more completely-resolved precedent for "a Logistics sub-feature with its own settings and its own module," and matches the literal folder guidance in `docs/architecture/filesystem.md` ("For complex domains, use subfolders: `{Feature}/{Subdomain}/`" — documented for the Domain layer and mirrored in Application via `UseCases/{Subdomain}/`).

4. **Lower blast radius, matching the "surgical changes" principle.** GiftSettings has no dedicated frontend OpenAPI client usage — `frontend/src/api/hooks/useGiftSetting.ts` builds its own absolute URL (`${apiClient.baseUrl}/api/gift-settings`) via raw `fetch`, not generated client methods. Since the HTTP contract does not need to change under Option B, this refactor is a pure backend namespace/folder move: no frontend changes, no OpenAPI client regeneration, no route change, no migration changes (EF configuration only needs a namespace update, not a schema change).

Separately, `docs/architecture/filesystem.md` shows every other Logistics sub-feature with its own dedicated controller and route (`CarrierCoolingController` → `/api/carrier-cooling`, `StockTakingController`, `TransportBoxController` → `/api/transport-boxes`, `WeatherForecastController` → `/api/weather-forecast`) even though their domain is Logistics-owned. `GiftSettingsController` keeping its own controller and `/api/gift-settings` route (rather than merging into `LogisticsController`, which only `GiftPackageManufacture` does) is consistent with that broader precedent, so the API layer is **not** changed by this work.

## Functional Requirements

### FR-1: Relocate the GiftSettings Application layer under Logistics
Move all files under `backend/src/Anela.Heblo.Application/Features/GiftSettings/` into `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/`, preserving internal structure:
- `Dto/GiftSettingDto.cs`
- `UseCases/GetGiftSetting/GetGiftSettingHandler.cs`, `GetGiftSettingQuery.cs`
- `UseCases/SetGiftSetting/SetGiftSettingCommand.cs`, `SetGiftSettingHandler.cs`, `SetGiftSettingResponse.cs`, `SetGiftSettingValidator.cs`
- `GiftSettingsModule.cs`

All namespaces change from `Anela.Heblo.Application.Features.GiftSettings*` to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings*`, mirroring `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture*`.

**Acceptance criteria:**
- No file remains under `Anela.Heblo.Application/Features/GiftSettings/`.
- All moved types compile under the `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings` namespace tree.
- Class/interface/member names, public shapes, and behavior are unchanged (rename is namespace/folder-only, not a rewrite).

### FR-2: Update all call sites referencing the old namespace
Update `using` directives and fully-qualified references in:
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (the `using Anela.Heblo.Application.Features.GiftSettings;` import; the `services.AddGiftSettingsModule();` call itself is unchanged)
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs`

Any other reference to `Anela.Heblo.Application.Features.GiftSettings` found via repo-wide search must also be updated.

**Acceptance criteria:**
- `dotnet build` succeeds with zero errors and zero new warnings.
- A repo-wide search for `Anela.Heblo.Application.Features.GiftSettings` (old namespace) returns no matches outside of historical/migration files that don't reference C# namespaces.

### FR-3: Preserve DI registration semantics
`GiftSettingsModule.AddGiftSettingsModule()` keeps its current signature and registrations (`IGiftSettingRepository` → `GiftSettingRepository`, the `SetGiftSettingCommand` validator, and the `ValidationBehavior` pipeline binding). Only its namespace changes. `ApplicationModule.AddApplicationServices()` continues to call `services.AddGiftSettingsModule();` at the same point in the registration sequence (no reordering required, though placing it adjacent to `services.AddGiftPackageManufactureModule();` is acceptable for readability since both are Logistics sub-feature modules).

**Acceptance criteria:**
- The DI container resolves `IGiftSettingRepository`, the `SetGiftSettingCommand` validator, and the validation pipeline behavior exactly as before the move.
- Existing module-wiring/integration tests that exercise GiftSettings DI resolution pass unchanged in behavior.

### FR-4: Leave Domain, Persistence, and API layers unchanged
No changes to:
- `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/GiftSetting.cs`, `IGiftSettingRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingConfiguration.cs`, `GiftSettingRepository.cs` (these already reference `Anela.Heblo.Domain.Features.Logistics.GiftSettings`, which is unaffected by this move)
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (`DbSet<GiftSetting> GiftSettings`)
- EF Core migrations (no schema change; table `public.GiftSettings` is untouched)
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` route (`api/gift-settings`), HTTP verbs, and request/response wire shapes
- `frontend/src/api/hooks/useGiftSetting.ts` and any other frontend code (the controller route and DTO shape are unchanged, so the frontend requires no changes)

**Acceptance criteria:**
- `git diff` shows no changes to Domain, Persistence, Migrations, `GiftSettingsController.cs` (beyond the `using` statement in FR-2), or any `frontend/` file.
- The OpenAPI spec generated from the API project is byte-for-byte identical for the GiftSettings endpoints (same route, same request/response schema names and shapes).

### FR-5: Keep architecture boundary tests green and aligned with the new structure
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already treats `Anela.Heblo.Application.Features.Logistics` as one of the three Logistics namespace prefixes in several boundary checks (`Catalog -> Logistics`, `ExpeditionList -> Logistics`, `ShoptetApi Adapters -> Logistics`, and `Logistics -> Manufacture` / `Logistics -> Catalog` / `Logistics_types_should_not_reference_Purchase_owned_namespaces`). After the move, `GiftSettings` Application types fall under this prefix and are subject to these checks for the first time.

**Acceptance criteria:**
- All existing tests in `ModuleBoundariesTests.cs` pass without modification to allowlists (GiftSettings has no known cross-module references beyond `ICurrentUserService`, which is an already-sanctioned dependency per ADR-005 and is not Logistics/Manufacture/Catalog/Purchase-owned).
- If a new, legitimate cross-module reference is surfaced by this move, it is either resolved via the existing contract-inversion pattern (see `development_guidelines.md`'s `ILeafletKnowledgeSource` example) or added to the relevant allowlist with a one-line justification comment, following the existing allowlist style in the file — never silently suppressed.

### FR-6: Preserve test coverage
The three existing test files (`GetGiftSettingHandlerTests.cs`, `SetGiftSettingHandlerTests.cs`, `SetGiftSettingValidatorTests.cs`) continue to exist under `backend/test/Anela.Heblo.Tests/Application/GiftSettings/` (test folder layout is flat by feature name in this project regardless of source nesting — see `Application/GiftPackageManufacture/` as precedent, which stays flat even though its source lives under `Logistics/UseCases/GiftPackageManufacture/`). Only their `using`/namespace references to production types change.

**Acceptance criteria:**
- All three test files pass with zero test logic changes (assertions, arrangements, and mocked dependencies identical).
- Test discovery and `dotnet test` count for the GiftSettings suite is unchanged before/after the move.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is a pure namespace/folder reorganization with no change to algorithms, queries, or I/O patterns. No new database round-trips, no new allocations of note.

### NFR-2: Security
No change. `GiftSettingsController` retains its existing `[FeatureAuthorize(Feature.Warehouse_Logistics)]` / `[FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]` attributes unchanged. `SetGiftSettingHandler` continues to resolve the current user via `ICurrentUserService` inside the handler per ADR-005 (already compliant; not part of this change).

### NFR-3: Backward compatibility
The public HTTP contract (`GET /api/gift-settings`, `PUT /api/gift-settings`, request/response JSON shapes) must remain byte-for-byte identical, since it is consumed by a hand-rolled frontend hook that is out of scope for this change and by the auto-generated OpenAPI client (regenerating the client must produce no diff for GiftSettings operations).

## Data Model
No data model changes. For reference, the existing (unchanged) model:

- **`GiftSetting`** (`Domain/Features/Logistics/GiftSettings/GiftSetting.cs`) — singleton-style aggregate (`Id` is always `1`), fields: `IsEnabled` (bool), `ThresholdCzk` (decimal), `Text` (string, max 50 chars), `ModifiedAt` (nullable `DateTimeOffset`), `ModifiedBy` (nullable string). Mapped to table `public.GiftSettings` via `GiftSettingConfiguration`.
- **`IGiftSettingRepository`** — `GetAsync()` (returns the single row or `GiftSetting.CreateDefault()` if none exists), `SaveAsync(GiftSetting)` (upsert against the single row).
- **`GiftSettingDto`** (moves from `Application.Features.GiftSettings.Dto` to `Application.Features.Logistics.UseCases.GiftSettings.Dto`) — flat projection of the entity for the `GET` response.

## API / Interface Design
No change to the external HTTP surface:
- `GET /api/gift-settings` → `GetGiftSettingQuery` → `GiftSettingDto`
- `PUT /api/gift-settings` (requires write access) → `SetGiftSettingCommand` (`IsEnabled`, `ThresholdCzk`, `Text`) → `SetGiftSettingResponse` (`BaseResponse` with `Success`/`ErrorCode`/`Params`)

Internal (backend-only) change:
- MediatR request/response/handler namespaces move from `Anela.Heblo.Application.Features.GiftSettings.*` to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.*`.
- `GiftSettingsModule` namespace moves from `Anela.Heblo.Application.Features.GiftSettings` to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings`; the `AddGiftSettingsModule()` extension method name and call site in `ApplicationModule.cs` are unchanged.

## Dependencies
- No new external libraries or services.
- Depends on existing MediatR, FluentValidation, and EF Core wiring already in place.
- Depends on `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`) for identity resolution in `SetGiftSettingHandler`, unchanged.

## Out of Scope
- Any change to `Domain/Features/Logistics/GiftSettings/` or `Persistence/Logistics/GiftSettings/` (these already match the target Logistics ownership and are untouched).
- Any change to `GiftSettingsController.cs` beyond its `using` directives (route, verbs, and authorization attributes stay as-is).
- Merging `GiftSettingsController` into `LogisticsController` (rejected — see Background; would diverge from the `CarrierCooling`/`StockTaking`/`TransportBox`/`WeatherForecast` precedent of dedicated per-feature controllers for Logistics sub-features).
- Any change to `frontend/src/api/hooks/useGiftSetting.ts` or other frontend code.
- Resolving the analogous domain/application "split" that also exists for `CarrierCooling` and `WeatherForecast` (flagged as a related, pre-existing inconsistency — see Open Questions).
- Any database schema or migration change.
- Phase 2 persistence work (per-module `DbContext`) referenced in `docs/architecture/development_guidelines.md` — unrelated to this fix.

## Open Questions
1. `CarrierCooling` and `WeatherForecast` have the identical domain/persistence-under-Logistics-but-standalone-application "split" that this spec fixes for `GiftSettings`. Should a follow-up arch-review item be filed to apply the same `GiftPackageManufacture`-style consolidation to those two features for full repo-wide consistency, or is the split acceptable for "simple" (1-3 use case) Logistics sub-features that keep their own controller? Assumption made for this spec: those two are explicitly out of scope here and should be tracked as a separate decision, since resolving them is not required to close this specific arch-review finding.

## Status: HAS_QUESTIONS
