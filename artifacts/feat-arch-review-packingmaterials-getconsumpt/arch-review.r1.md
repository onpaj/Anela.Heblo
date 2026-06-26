I have enough context. The proposal aligns with existing patterns in this codebase. Now writing the architecture review.

# Architecture Review: Extract duplicated `GetConsumptionTypeText` helper in PackingMaterials module

## Skip Design: true

## Architectural Fit Assessment

This is a pure backend refactor confined to the `PackingMaterials` application layer slice. The proposal fits cleanly into existing conventions:

- **Vertical-slice ownership**: The helper stays inside `Application/Features/PackingMaterials/`, respecting module isolation (`docs/architecture/development_guidelines.md` — "DTOs are never shared or global"; the same applies to formatting helpers).
- **Precedent**: Per-feature static helpers already exist in this codebase — `Features/Smartsupp/SmartsuppNameHelper.cs`, `Features/Photobank/Validators/PhotobankValidationHelpers.cs`, `Features/Manufacture/ErrorFilters/Filters/ManufactureErrorParsingHelpers.cs`. A `PackingMaterialsTextHelper.cs` follows the established naming and placement convention.
- **No cross-cutting impact**: `ConsumptionTypeText` is a presentation-layer string formatter, not domain logic. It belongs in `Application`, not `Domain` — consistent with the existing distribution where the `ConsumptionType` enum sits in `Domain/Features/PackingMaterials/Enums/` but its UI label is materialized at the application boundary into `PackingMaterialDto`.
- **Internal visibility**: The helper is consumed only by handlers within the same assembly. `internal` is correct and matches the spec.

The one minor deviation from filesystem.md is that `Contracts/` is documented as containing "Shared DTOs across use cases." The proposed file is a helper, not a DTO. See **Decision 1** below.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
└── Features/PackingMaterials/Enums/
    └── ConsumptionType.cs                  (unchanged — source enum)

Anela.Heblo.Application
└── Features/PackingMaterials/
    ├── Contracts/
    │   ├── PackingMaterialDto.cs           (unchanged — ConsumptionTypeText property)
    │   └── PackingMaterialsTextHelper.cs   (NEW — internal static helper)
    └── UseCases/
        ├── CreatePackingMaterial/CreatePackingMaterialHandler.cs           (call site rewired)
        ├── UpdatePackingMaterial/UpdatePackingMaterialHandler.cs           (call site rewired)
        ├── UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs  (call site rewired)
        ├── GetPackingMaterialsList/GetPackingMaterialsListHandler.cs       (call site rewired)
        └── GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs         (call site rewired)
```

Each affected handler already imports `Anela.Heblo.Application.Features.PackingMaterials.Contracts` (for `PackingMaterialDto`) and `Anela.Heblo.Domain.Features.PackingMaterials.Enums` (for `ConsumptionType`), so no new `using` directives are required.

### Key Design Decisions

#### Decision 1: Place the helper in `Contracts/` rather than the module root or a new `Helpers/` folder
**Options considered:**
1. `Contracts/PackingMaterialsTextHelper.cs` — co-located with `PackingMaterialDto` whose `ConsumptionTypeText` property it populates.
2. `PackingMaterials/PackingMaterialsTextHelper.cs` — at the module root, alongside `PackingMaterialsModule.cs`. Matches `Features/Smartsupp/SmartsuppNameHelper.cs`.
3. New `Helpers/` subfolder under `PackingMaterials/`.

**Chosen approach:** Option 1 (`Contracts/`), as specified in the spec and brief.

**Rationale:** The helper exists exclusively to produce one field on `PackingMaterialDto`. Co-locating it with the DTO it serves makes the binding obvious and discoverable when the enum is extended. Option 2 is also defensible (and matches `SmartsuppNameHelper`), but the spec's choice is acceptable given the tight semantic coupling to the DTO. Option 3 introduces a new folder for a single file — premature structure.

The minor naming friction (a non-DTO file under `Contracts/`) is acceptable for this single, surgical case. If a second helper appears later, promote both to `PackingMaterials/` root or a `Helpers/` folder at that point.

#### Decision 2: Keep visibility `internal`, helper class `static`, method `public`
**Options considered:**
- `internal static class` with `public static` method (spec).
- `public static class` (would expose the helper outside the assembly).

**Chosen approach:** `internal static class PackingMaterialsTextHelper` with `public static string ConsumptionTypeText(ConsumptionType type)`.

**Rationale:** No consumer outside `Anela.Heblo.Application` needs this formatter; the API project receives `ConsumptionTypeText` as a pre-formatted string on the DTO. `internal` enforces the module boundary documented in `development_guidelines.md`. The method must be `public` within the class so internal callers in other namespaces of the same assembly can reach it.

#### Decision 3: Preserve the `_ => type.ToString()` fallback unchanged
**Options considered:**
- Keep the silent fallback (spec).
- Throw `ArgumentOutOfRangeException` on unknown values to surface drift loudly.

**Chosen approach:** Preserve the fallback. Explicitly out of scope per spec.

**Rationale:** The whole point of consolidation is that a missing enum case now needs to be added in exactly one place, so the fallback's drift-hiding behavior is largely neutralized. Changing the fallback semantics in this PR would mix a behavior change with a mechanical refactor and risk altering API output for any data with unexpected enum values (e.g., legacy rows). Track as follow-up if desired.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file:

```
backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
```

Modify exactly five existing files (delete the private method, rewire the single call site each):

- `UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs` (lines 36, 50–56)
- `UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` (lines 37, 50–56)
- `UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` (lines 50, 63–69)
- `UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` (lines 39, 53–59)
- `UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` (lines 36, 63–69)

No other files are touched. No `using` additions are required — the namespace `Anela.Heblo.Application.Features.PackingMaterials.Contracts` is already imported by every affected handler.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

internal static class PackingMaterialsTextHelper
{
    public static string ConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder   => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay     => "za den",
        _ => type.ToString()
    };
}
```

Call-site rewrite pattern (each of the five DTO constructions):

```csharp
// before
ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),

// after
ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
```

### Data Flow

Unchanged. For each handler:

1. Handler receives MediatR request.
2. Handler loads `PackingMaterial` (with `ConsumptionType` enum value) from repository.
3. Handler constructs `PackingMaterialDto`, calling `PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType)` (was: private `GetConsumptionTypeText`).
4. Handler returns response containing the DTO.
5. Controller serializes — the `ConsumptionTypeText` JSON field is byte-identical to the pre-refactor output for any current enum value.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed call site or stale duplicate left behind | Low | After edit, run a repo-wide grep for `GetConsumptionTypeText` — must return zero hits. Spec FR-2 already lists this as acceptance criterion. |
| Accidental behavior change (different label, lost diacritics) | Low | Copy the switch arms verbatim, including the Czech characters `á`, `ž`. Existing handler tests assert the strings indirectly via DTO snapshots/assertions; they must pass without modification. |
| Helper placement under `Contracts/` may confuse future readers expecting only DTOs | Low | Documented in Decision 1. The file name `…TextHelper.cs` makes its purpose unambiguous. Revisit if a second helper appears. |
| Encoding regression (file saved without UTF-8 BOM where the rest of the project uses BOM, or vice versa) | Low | Match the encoding of an existing sibling file in `Contracts/` (e.g., `PackingMaterialDto.cs`). Run `dotnet format` — spec NFR-4 requires a no-op diff. |
| Other duplicated label helpers elsewhere in the codebase remain untouched | Low (out of scope) | Spec explicitly excludes touching other duplicates. The brief was filed by an automated arch-review routine; if it surfaces more, they will arrive as separate tickets. |

## Specification Amendments

None substantive. Two clarifications worth recording:

1. **No new `using` statements are required.** Every target handler already imports both `Anela.Heblo.Application.Features.PackingMaterials.Contracts` and `Anela.Heblo.Domain.Features.PackingMaterials.Enums`. The spec doesn't say otherwise, but reviewers should not add a `using static` directive — call sites read `PackingMaterialsTextHelper.ConsumptionTypeText(...)` fully qualified by class name (matches `SmartsuppNameHelper` precedent).
2. **Verification step:** After the edit, in addition to FR-2's grep for `GetConsumptionTypeText`, run `dotnet build` against `backend/Anela.Heblo.sln` and execute `dotnet test backend/test/Anela.Heblo.Tests` filtered to PackingMaterials. NFR-4 already implies this; making it explicit avoids ambiguity.

## Prerequisites

None. No migrations, no config changes, no infrastructure changes, no DI registration changes (helper is a static class, not injected). The work can begin immediately on the current branch.