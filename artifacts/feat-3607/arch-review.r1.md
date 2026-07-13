# Architecture Review: Align GiftSettings Module Boundaries with Logistics

## Skip Design: true

This is a pure backend namespace/folder move — no new or changed UI components, screens, or visual behavior. Frontend (`frontend/src/api/hooks/useGiftSetting.ts`) is untouched, and the HTTP contract is byte-for-byte identical.

## Architectural Fit Assessment

This refactor closes a real layer-boundary inconsistency, and the spec's Option B decision is correct and well-supported. I independently verified the two load-bearing claims:

1. **The `GiftPackageManufacture` precedent is exact.** Its Application layer lives at `Features/Logistics/UseCases/GiftPackageManufacture/` with its own `GiftPackageManufactureModule.cs` (`AddGiftPackageManufactureModule()`), registered in `ApplicationModule.cs` immediately alongside `services.AddGiftSettingsModule();` (line 93 vs. line 111). Domain sits at `Domain/Features/Logistics/GiftPackageManufacture/`, persistence at `Persistence/Logistics/GiftPackageManufacture/`. This is a currently-compiling, currently-tested three-layer-aligned shape — exactly the target for GiftSettings.

2. **`ModuleBoundariesTests.cs` already models Logistics as the namespace trio** `Anela.Heblo.Domain.Features.Logistics` / `Anela.Heblo.Application.Features.Logistics` / `Anela.Heblo.Persistence.Logistics` in the `Catalog -> Logistics` (line 490-496) and `ExpeditionList -> Logistics` (line 556-562) boundary checks, and `ShoptetApiAdaptersLogisticsAllowlist` (line 299-300) already allowlists `Anela.Heblo.Domain.Features.Logistics.GiftSettings.IGiftSettingRepository`/`GiftSetting` as Logistics-owned. Moving the Application layer under the same prefix makes GiftSettings visible to these checks for the first time, with no allowlist changes anticipated (the only import is `ICurrentUserService`, already a sanctioned cross-cutting dependency per ADR-005, not owned by any of Manufacture/Catalog/Purchase).

No disagreement with the spec's decision. This review's job is to pin down the exact mechanics.

## Proposed Architecture

### Component Overview

```
Domain/Features/Logistics/GiftSettings/            [UNCHANGED]
  GiftSetting.cs, IGiftSettingRepository.cs

Persistence/Logistics/GiftSettings/                 [UNCHANGED]
  GiftSettingConfiguration.cs, GiftSettingRepository.cs

Application/Features/Logistics/UseCases/GiftSettings/   [NEW LOCATION]
  GiftSettingsModule.cs
  Dto/GiftSettingDto.cs
  UseCases/GetGiftSetting/{GetGiftSettingQuery,GetGiftSettingHandler}.cs
  UseCases/SetGiftSetting/{SetGiftSettingCommand,SetGiftSettingHandler,
                           SetGiftSettingResponse,SetGiftSettingValidator}.cs

API/Controllers/GiftSettingsController.cs           [using-only change]
  route api/gift-settings unchanged

Application/ApplicationModule.cs                    [using-only change]
  AddGiftSettingsModule() call site unchanged (line 111)

Test/Application/GiftSettings/                      [UNCHANGED location, using-only change]
  Get/SetGiftSettingHandlerTests.cs, SetGiftSettingValidatorTests.cs
```

This mirrors `GiftPackageManufacture`'s already-working shape 1:1 — same folder depth (`Logistics/UseCases/{Feature}/`), same per-feature module class, same flat test folder despite nested source (confirmed: `test/.../Application/GiftPackageManufacture/` is flat while its production code lives under `Logistics/UseCases/GiftPackageManufacture/`).

### Key Design Decisions

#### Decision 1: Confirm Option B over Option A
**Options considered:** A (promote GiftSettings to a fully standalone module across all 3 layers) vs. B (fold Application into Logistics, matching Domain/Persistence which already live there).
**Chosen approach:** B, as specified.
**Rationale:** Domain and Persistence already encode "GiftSettings belongs to Logistics" and are exercised as such by `ShoptetApiAdaptersLogisticsAllowlist`. Reversing that (Option A) would require touching Domain, Persistence, `ApplicationDbContext`, and a migration namespace change for zero functional gain, and would fight the architecture test suite's existing model rather than complete it. B is strictly lower blast radius and directly precedented.

#### Decision 2: Test folder stays flat (`Test/Application/GiftSettings/`), not nested
**Options considered:** Mirror the new source nesting (`Test/Application/Logistics/GiftSettings/`) vs. keep the existing flat `Test/Application/GiftSettings/` folder.
**Chosen approach:** Keep flat, per spec FR-6.
**Rationale:** Verified precedent — `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/` is flat by feature name today even though its production counterpart is nested three levels under `Logistics/UseCases/GiftPackageManufacture/`. This is the established test-layout convention in this repo; don't invent a new one for this move. Only the test files' `using` statements and any fully-qualified references change — the C# namespace `Anela.Heblo.Tests.Application.GiftSettings` (the test project's own namespace, independent of production namespaces) is unaffected.

#### Decision 3: `GiftSettingsModule.cs` keeps its extension method name and registration order
**Options considered:** Rename `AddGiftSettingsModule()` to something Logistics-prefixed, or relocate the call site next to other Logistics registrations.
**Chosen approach:** Keep the method name and call site exactly as-is (line 111 in `ApplicationModule.cs`); only the `using` import changes from `Anela.Heblo.Application.Features.GiftSettings` to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings`.
**Rationale:** `GiftPackageManufactureModule` isn't renamed to indicate Logistics ownership either (`AddGiftPackageManufactureModule()`, not `AddLogisticsGiftPackageManufactureModule()`) — the namespace conveys ownership, not the method name. Renaming would be gratuitous churn outside the spec's stated scope ("surgical changes" per CLAUDE.md), and reordering the call site is explicitly called out as optional/cosmetic in FR-3, not required.

## Implementation Guidance

### Directory / Module Structure

Move (git mv, preserving history) these 8 files, with matching subfolder structure, from
`backend/src/Anela.Heblo.Application/Features/GiftSettings/` to
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/`:

- `GiftSettingsModule.cs`
- `Dto/GiftSettingDto.cs`
- `UseCases/GetGiftSetting/GetGiftSettingQuery.cs`
- `UseCases/GetGiftSetting/GetGiftSettingHandler.cs`
- `UseCases/SetGiftSetting/SetGiftSettingCommand.cs`
- `UseCases/SetGiftSetting/SetGiftSettingHandler.cs`
- `UseCases/SetGiftSetting/SetGiftSettingResponse.cs`
- `UseCases/SetGiftSetting/SetGiftSettingValidator.cs`

After the move, delete the now-empty `Application/Features/GiftSettings/` directory tree.

In every moved file, change the `namespace` declaration from `Anela.Heblo.Application.Features.GiftSettings[.Sub]` to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings[.Sub]`. Internal cross-references between these files (e.g. `GetGiftSettingHandler.cs` importing `Dto`, `SetGiftSettingHandler.cs` importing `SetGiftSettingCommand`'s namespace implicitly via same-folder) need their `using` statements updated to the new prefix — concretely:
- `GetGiftSettingHandler.cs`: `using Anela.Heblo.Application.Features.GiftSettings.Dto;` → `...Logistics.UseCases.GiftSettings.Dto;`
- `GetGiftSettingQuery.cs`: same Dto using change.
- `SetGiftSettingHandler.cs`: `using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;` → drop or update (it's in the same namespace after the move — verify no self-referential using is left dangling).
- `GiftSettingsModule.cs`: `using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;` → `...Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;`

Do **not** touch (already correctly reference Logistics-owned Domain/Persistence types, unaffected by this move):
- `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` (in `SetGiftSettingHandler.cs`, `GetGiftSettingHandler.cs`)
- `using Anela.Heblo.Persistence.Logistics.GiftSettings;` (in `GiftSettingsModule.cs`)

### Interfaces and Contracts

No public interface or contract changes. `IGiftSettingRepository`, `GiftSetting`, `GiftSettingDto`, `GetGiftSettingQuery`, `SetGiftSettingCommand`, `SetGiftSettingResponse` keep identical members and shapes — only their C# namespace changes for the four Application-layer types. The MediatR `IRequestHandler<TRequest, TResponse>` wiring is unaffected since both request and handler move together.

### Data Flow

Unchanged. `GET /api/gift-settings` → `GetGiftSettingQuery` (now in `Logistics.UseCases.GiftSettings`) → `GetGiftSettingHandler` → `IGiftSettingRepository.GetAsync()` (Domain/Persistence, untouched) → `GiftSettingDto`. `PUT /api/gift-settings` follows the same path through `SetGiftSettingCommand`/`SetGiftSettingHandler`/`ICurrentUserService`/`IGiftSettingRepository.SaveAsync()`. Only the fully-qualified type names in the middle of this chain change; wire shapes, route, and DI resolution graph do not.

### Files requiring `using`-only edits (no move)

- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` — two `using` lines (`GetGiftSetting`, `SetGiftSetting` sub-namespaces).
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — line 36 `using Anela.Heblo.Application.Features.GiftSettings;` → `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;`. Consider moving this using line to sit near line 31 (`...Logistics.UseCases.GiftPackageManufacture;`) for readability, though not required. Line 111 `services.AddGiftSettingsModule();` call itself is unchanged.
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs` — 2 using lines (`.Dto`, `.UseCases.GetGiftSetting`).
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs` — 1 using line (`.UseCases.SetGiftSetting`).
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs` — 1 using line (`.UseCases.SetGiftSetting`).

After edits, grep the whole repo for `Anela.Heblo.Application.Features.GiftSettings` (old namespace, not `Domain.Features.Logistics.GiftSettings` or `Persistence.Logistics.GiftSettings`, which are correct and must stay) to confirm zero remaining references.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Grep-and-replace across namespace catches false positives, e.g. accidentally touching `Domain.Features.Logistics.GiftSettings` or `Persistence.Logistics.GiftSettings` strings | Low | Match the exact prefix `Anela.Heblo.Application.Features.GiftSettings` (with trailing `.` or `;` or end-of-line), not the bare word `GiftSettings`; verify Domain/Persistence `using` lines are byte-identical before/after via `git diff` |
| `ModuleBoundariesTests.cs`'s `Logistics -> Manufacture`/`Logistics -> Catalog`/`Logistics_types_should_not_reference_Purchase_owned_namespaces` checks now scan GiftSettings types for the first time and could fail on an unexpected reference | Low | GiftSettings' only external dependency is `ICurrentUserService` (`Domain.Features.Users`), not Manufacture/Catalog/Purchase-owned; run the full `ModuleBoundariesTests` suite after the move as the primary verification step, not just a build |
| OpenAPI-generated TypeScript client silently changes operation IDs or schema names if C# namespace leaks into Swagger operation naming | Low | FR-4's acceptance criterion (byte-for-byte identical OpenAPI spec for GiftSettings endpoints) should be checked by diffing the generated spec before/after; ASP.NET Core route/controller/DTO class names (not namespaces) drive Swashbuckle operation IDs by default in this codebase, so risk is low but worth a concrete diff, not just an assumption |
| Leftover empty `Application/Features/GiftSettings/` directory or stray `.cs` file missed in the move | Low | `find backend/src/Anela.Heblo.Application/Features/GiftSettings -type f` should return nothing after the move; delete the directory explicitly |

## Specification Amendments

None required — the spec (FR-1 through FR-6) is implementation-ready as written and matches what I found in the actual files. One clarification worth calling out explicitly to the implementer (not a spec defect, just easy to miss): `SetGiftSettingHandler.cs` currently has a self-referential `using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;` at its top even though the file itself declares that same namespace — this is a redundant using in the current code (harmless, likely IDE-generated) that will need updating to the new namespace or can be dropped entirely as dead weight; either is acceptable since it doesn't change behavior.

## Prerequisites

None. No new infrastructure, config, feature flags, or migrations are needed — this is a self-contained rename executable in a single PR. Confirm before merging:
- `dotnet build` produces zero errors/new warnings (FR-2 acceptance criterion).
- `dotnet test --filter Anela.Heblo.Tests.Architecture.ModuleBoundariesTests` passes without allowlist changes (FR-5).
- `dotnet test --filter FullyQualifiedName~GiftSettings` passes with the same test count as before the move (FR-6).
- `git diff --stat` shows changes confined to the 8 moved files (as renames), `ApplicationModule.cs`, `GiftSettingsController.cs`, and the 3 test files — nothing under `Domain/`, `Persistence/`, migrations, or `frontend/` (FR-4).
