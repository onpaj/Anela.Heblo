# Design: Deduplicate Shoptet order-status ID constants (ExpeditionList / Logistics.Picking)

## Component Design

### `ExpeditionPickingRequest` (`Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`)
- **Role:** Consumer-owned contract for the expedition picking flow; becomes the **sole declaration site** for the three Shoptet order-status ID constants.
- **Responsibility change:** None functionally — it already declares `DefaultSourceStateId = -2`, `DefaultDesiredStateId = 26`, `DefaultNoteStateId = 35`, plus `DefaultCarriers` (unaffected). This design only adds two inline comments identifying the Shoptet status names, so all three constants are self-documenting:
  - `DefaultSourceStateId` → comment: Shoptet status "Vyřizuje se" (processing).
  - `DefaultDesiredStateId` → comment: Shoptet status "Bali se" (packing).
  - `DefaultNoteStateId` → existing comment retained as-is ("Poznámka — orders with incomplete address").
- **Contract surface:** Unchanged. Public property list, types, and constant values are identical before/after.

### `PrintPickingListRequest` (`Application/Features/Logistics/Picking/PrintPickingListRequest.cs`)
- **Role:** Logistics-owned DTO consumed by `IPickingListSource`; loses local ownership of the three constants and instead sources its property defaults from `ExpeditionPickingRequest`.
- **Interface/contract:**
  - Removes its own `DefaultSourceStateId`, `DefaultDesiredStateId`, `DefaultNoteStateId` `public const int` declarations, and the adjacent stray commented-out dead-code line (`//private const string DesiredStateId = "26"; // Bali se`).
  - Adds `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;`, annotated with a breadcrumb comment explaining why a Logistics DTO imports an ExpeditionList contracts namespace (following this codebase's existing convention for intentional cross-module edges, as in `LogisticsModule.cs` and `LogisticsExpeditionPickingAdapter.cs`).
  - Its three auto-properties keep their existing names/types and now default as:
    - `SourceStateId` → `ExpeditionPickingRequest.DefaultSourceStateId`
    - `DesiredStateId` → `ExpeditionPickingRequest.DefaultDesiredStateId`
    - `NoteStateId` → `ExpeditionPickingRequest.DefaultNoteStateId`
  - Observable defaults from `new PrintPickingListRequest()` are unchanged: `-2`, `26`, `35`.

### `LogisticsExpeditionPickingAdapter` (`Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`)
- **Role:** Unchanged. Continues to implement `ExpeditionList.Contracts.IExpeditionPickingSource` and build a `PrintPickingListRequest` by copying an `ExpeditionPickingRequest` instance's *runtime property values* — not the class-level default constants. No code in this file is touched by this design.

### Module boundary
- The existing `Logistics → ExpeditionList.Contracts` dependency direction (already load-bearing via the adapter) is reused, not newly introduced. `ModuleBoundariesTests.cs` guards only the reverse (`ExpeditionList → Logistics`) direction, so no allowlist change is required. This is a component-ownership decision, not new architecture: `ExpeditionPickingRequest` is confirmed as the canonical owner; `PrintPickingListRequest` is a pure reference consumer for its own defaults.

### Test component: `PickingListIntegrationTests` (`Anela.Heblo.Adapters.Shoptet.Tests`)
- **Change:** Two constant references (`PrintPickingListRequest.DefaultSourceStateId`, `PrintPickingListRequest.DefaultDesiredStateId`) are repointed to `ExpeditionPickingRequest.DefaultSourceStateId` / `.DefaultDesiredStateId`. The adjacent explanatory comment is updated to describe a single declaration rather than a cross-class value match. No new `using` needed — the file already imports `ExpeditionList.Contracts`.
- `LogisticsExpeditionPickingAdapterTests.cs` requires no change (never references the removed constants by name).

## Data Schemas

No schema changes. This refactor touches only compile-time `const int` declarations and their reference sites — no database schema, no API request/response shape, no MediatR contract, no event payload, and no JSON serialization shape is altered. `ExpeditionPickingRequest` and `PrintPickingListRequest` keep identical public property lists, types, and effective default values (`-2`, `26`, `35`) before and after.
