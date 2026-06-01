# Relocate `Cooling` Enum to Shared Domain Namespace — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `Cooling` enum from `Anela.Heblo.Domain.Features.Catalog` to `Anela.Heblo.Domain.Shared` so that Logistics, ShoptetOrders, Packaging, CarrierCooling, the ShoptetApi adapter, and the Flexi adapter no longer take a compile-time dependency on the Catalog module just to reach a temperature-chain shipping classification.

**Architecture:** Pure namespace relocation. The enum's name, members (`None=0`, `L1=1`, `L2=2`), CLR identity at runtime, JSON serialization, and EF Core `HasConversion<string>()` persistence remain byte-identical. Every consumer file's only edit is to its `using` directives at the top — except the two Catalog-owned files (`CatalogProperties.cs`, `CatalogAttributes.cs`) where same-namespace lookup currently resolves `Cooling` and a brand-new `using` line must be added.

**Tech Stack:** C# / .NET 8, EF Core (string-conversion column), xUnit + FluentAssertions for tests.

---

## File Structure

### File moved (one delete, one create)

```
DELETE: backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs
CREATE: backend/src/Anela.Heblo.Domain/Shared/Cooling.cs
```

New file contents (identical body, new namespace):

```csharp
namespace Anela.Heblo.Domain.Shared;

public enum Cooling
{
    None = 0,
    L1 = 1,
    L2 = 2,
}
```

### Edit pattern per file class

When a consumer file is edited, apply the right pattern depending on its **current** state:

| Class | Current state | Edit |
|---|---|---|
| **A. Catalog-owned, no `using` today** | File is in `Anela.Heblo.Domain.Features.Catalog[.…]` namespace and has zero `using` directives. `Cooling` resolves via same-namespace lookup today. | **Insert** a new `using Anela.Heblo.Domain.Shared;` line above the `namespace` line. |
| **B. Sole-purpose Catalog using** | File has `using Anela.Heblo.Domain.Features.Catalog;` and uses **no other** Catalog-namespace type (only `Cooling`). | **Replace** that using with `using Anela.Heblo.Domain.Shared;`. |
| **C. Catalog using shared with other types** | File has `using Anela.Heblo.Domain.Features.Catalog;` AND references `ICatalogRepository`, `CatalogProperties`, `ProductType`, etc. | **Keep** the existing `Catalog` using, **add** `using Anela.Heblo.Domain.Shared;` alongside it. Preserve alphabetical order if the file's other usings are sorted. |
| **D. No type name, only property access** | File never names the `Cooling` type — only accesses properties of that type (e.g. `s.Cooling`, `Properties.Cooling`). | **Do nothing.** The compiler resolves the type via the property's declared type; no using is needed. |
| **E. EF migrations & snapshot** | `Cooling` appears only as a string column name (`b.Property<string>("Cooling")`). | **Do nothing.** Historical migrations are immutable and the snapshot won't change because the enum's CLR identity is irrelevant here. |

The exact category for each file is given in Tasks 3–6.

### Files explicitly NOT edited (verified by grep)

These were on the spec's optional list but, on inspection, do not reference the `Cooling` type:

- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — no `Cooling` references; do not touch.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs` — no `Cooling` references; do not touch.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — references `Cooling` only via property access (`attributes.Cooling`, `product.Properties.Cooling`); category D.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` — property access only; category D.
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — property access only; category D.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs` — property access only; category D.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingResponse.cs` — no `Cooling` type name; category D.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs` — no `Cooling` type name; category D.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixRequest.cs` — no `Cooling` references; category D.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixResponse.cs` — no `Cooling` references; category D.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierGroupDto.cs` — no `Cooling` references; category D.
- `backend/src/Anela.Heblo.Domain/Features/Logistics/ICarrierCoolingRepository.cs` — no `Cooling` references (only `CarrierCoolingSetting`); category D.
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` — no `Cooling` references; category D.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — property access only; category D. (Its existing `using Anela.Heblo.Domain.Features.Catalog;` stays — needed for `ICatalogRepository`.)
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingSettingConfiguration.cs` — `builder.Property(e => e.Cooling)` is property access; category D.
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingRepository.cs` — `setting.Cooling` is property access; category D.
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — `Cooling` appears only inside the string `// Carrier Cooling module` comment; category D.
- All files under `backend/src/Anela.Heblo.Persistence/Migrations/` — category E.

Despite this static analysis, **Task 8 still runs a project-wide grep sweep** to catch anything added between this plan and execution.

---

## Task 1: Move the enum file to `Shared`

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs`
- Create: `backend/src/Anela.Heblo.Domain/Shared/Cooling.cs`

- [ ] **Step 1: Delete the original file**

Run:
```bash
rm backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs
```

Expected: file is removed; no output.

- [ ] **Step 2: Create the new file under `Shared/`**

Create `backend/src/Anela.Heblo.Domain/Shared/Cooling.cs` with exactly:

```csharp
namespace Anela.Heblo.Domain.Shared;

public enum Cooling
{
    None = 0,
    L1 = 1,
    L2 = 2,
}
```

- [ ] **Step 3: Verify the move on disk**

Run:
```bash
ls backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs 2>&1 || echo "GONE: ok"
cat backend/src/Anela.Heblo.Domain/Shared/Cooling.cs
```

Expected: first command reports the file is gone; second command prints the new file with `namespace Anela.Heblo.Domain.Shared;` on line 1.

- [ ] **Step 4: Confirm build is broken now (sanity check)**

Run:
```bash
dotnet build backend/Anela.Heblo.sln 2>&1 | tail -40
```

Expected: build **fails** with many `CS0246: The type or namespace name 'Cooling' could not be found` errors. This is the desired intermediate state — Tasks 2–7 will resolve every error.

> Do **not** commit at this point. The next several tasks restore the build; commit only after Task 9 succeeds.

---

## Task 2: Patch Catalog-owned files (category A — silent failure zone)

These two files currently rely on **same-namespace lookup** to see `Cooling`. They have no `using` line to "replace." The implementer MUST add a new `using` line — otherwise the Catalog project itself fails to compile.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogProperties.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/CatalogAttributes.cs`

- [ ] **Step 1: Add the using directive to `CatalogProperties.cs`**

Before:
```csharp
namespace Anela.Heblo.Domain.Features.Catalog;

public record CatalogProperties
```

After:
```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Catalog;

public record CatalogProperties
```

Use Edit with `old_string` = `namespace Anela.Heblo.Domain.Features.Catalog;\n\npublic record CatalogProperties` and `new_string` = `using Anela.Heblo.Domain.Shared;\n\nnamespace Anela.Heblo.Domain.Features.Catalog;\n\npublic record CatalogProperties`.

- [ ] **Step 2: Add the using directive to `CatalogAttributes.cs`**

This file already has `using Anela.Heblo.Domain.Features.Catalog;` on line 1 (for `ProductType`). Insert `using Anela.Heblo.Domain.Shared;` as the second line.

Before:
```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Catalog.Attributes;
```

After:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Catalog.Attributes;
```

- [ ] **Step 3: Spot-build the Domain project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -30
```

Expected: Domain project builds **clean** (0 errors). If any `CS0246: 'Cooling' could not be found` remains, locate it via `grep -n "\bCooling\b" backend/src/Anela.Heblo.Domain/Features/Catalog/` and repeat.

---

## Task 3: Patch Logistics domain file

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs`

- [ ] **Step 1: Swap the Catalog using for Shared (category B)**

`CarrierCoolingSetting.cs` currently has `using Anela.Heblo.Domain.Features.Catalog;` on line 1 purely for `Cooling`. `Carriers` and `DeliveryHandling` live in the same `Anela.Heblo.Domain.Features.Logistics` namespace as the file and need no using.

Edit line 1:

```csharp
// before
using Anela.Heblo.Domain.Features.Catalog;
// after
using Anela.Heblo.Domain.Shared;
```

- [ ] **Step 2: Spot-build the Domain project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -20
```

Expected: build clean (0 errors, 0 new warnings).

---

## Task 4: Patch Application-layer consumers

These files explicitly name the `Cooling` type and need either a using-replace (category B) or a using-add (category C).

**Files (all under `backend/src/Anela.Heblo.Application/`):**

| File | Category | Reason |
|---|---|---|
| `Features/ShoptetOrders/IPackingOrderClient.cs` | B | Only Catalog reference is `Cooling`. |
| `Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs` | B | Only Catalog reference is `Cooling`. |
| `Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs` | B | Only Catalog reference is `Cooling`. |
| `Features/Catalog/Contracts/PropertiesDto.cs` | B | File is in `Application.Features.Catalog.Contracts` namespace but the `using` for the `Domain` namespace is solely for `Cooling`. |
| `Features/CarrierCooling/Contracts/CarrierCoolingRowDto.cs` | B | Only Catalog reference is `Cooling`. |
| `Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs` | B | Only Catalog reference is `Cooling`. |
| `Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs` | B | Only Catalog reference is `Cooling` (named as `Cooling.None`). |

- [ ] **Step 1: Apply category B replacement to all seven files**

In each file, locate the line `using Anela.Heblo.Domain.Features.Catalog;` and **replace it with** `using Anela.Heblo.Domain.Shared;`. Preserve the surrounding using lines.

Concrete examples:

`IPackingOrderClient.cs` before:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
```
After:
```csharp
using Anela.Heblo.Domain.Shared;
```

`CarrierCoolingRowDto.cs` before:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
```
After:
```csharp
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
```
(reordered to keep `using` directives alphabetical, matching the codebase's existing style for adjacent imports).

`SetCarrierCoolingRequest.cs` before:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;
```
After:
```csharp
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using MediatR;
```

`GetCarrierCoolingMatrixHandler.cs` before:
```csharp
using Anela.Heblo.Application.Features.CarrierCooling.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;
```
After:
```csharp
using Anela.Heblo.Application.Features.CarrierCooling.Contracts;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using MediatR;
```

`GetPackingOrderResponse.cs`, `ScanPackingOrderResponse.cs`, `PropertiesDto.cs` follow the same pattern: replace the Catalog using with Shared, keep other usings in alphabetical position.

- [ ] **Step 2: Spot-build the Application project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj 2>&1 | tail -30
```

Expected: build clean. If any `CS0246` remains, grep for it: `grep -n "\bCooling\b" backend/src/Anela.Heblo.Application/`.

---

## Task 5: Patch adapter consumers

**Files:**

| File | Category | Reason |
|---|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` | C | Uses `ICatalogRepository` (from Catalog) **and** names `Cooling` as a type (e.g., `Dictionary<string, Cooling>`, `internal static Cooling ResolveCarrierCooling(...)`). Keep Catalog using, add Shared. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs` | B | Only Catalog reference is `Cooling` (used as `Cooling CarrierCooling`, `Cooling.None`). Replace. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs` | C | Uses `ProductType` (from Catalog) **and** names `Cooling` (`internal static Cooling ParseCooling(...)`, `Cooling.None`). Keep Catalog using, add Shared. |

`ShoptetApiPackingOrderClient.cs` is **not** edited — only property access; the existing Catalog using is needed for `ICatalogRepository`.

- [ ] **Step 1: Edit `ShoptetApiExpeditionListSource.cs` (category C — add)**

Find the existing block:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
```

Add `using Anela.Heblo.Domain.Shared;` so the block becomes:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
```

Keep all other usings (`GiftSettings`, `Picking`, etc.) untouched.

- [ ] **Step 2: Edit `ExpeditionProtocolData.cs` (category B — replace)**

Before:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
```
After:
```csharp
using Anela.Heblo.Domain.Shared;
```

- [ ] **Step 3: Edit `FlexiProductAttributesQueryClient.cs` (category C — add)**

Find:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
```

Add `using Anela.Heblo.Domain.Shared;` after the `.Attributes` line:
```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Shared;
```

- [ ] **Step 4: Spot-build the adapter projects**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj 2>&1 | tail -20
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj 2>&1 | tail -20
```

Expected: both build clean.

---

## Task 6: Patch test files

Every test file references `Cooling.None`, `Cooling.L1`, or `Cooling.L2` directly (so they all name the type).

**Files:**

| File | Category |
|---|---|
| `backend/test/Anela.Heblo.Tests/Controllers/CarrierCoolingControllerTests.cs` | C (other Catalog types may be in scope; keep Catalog using, add Shared) |
| `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` | B (Catalog using is only for `Cooling`) |
| `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingValidatorTests.cs` | B |
| `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs` | B |
| `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs` | B |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs` | C (uses `CatalogProperties`, so Catalog using stays; add Shared) |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` | C (likely uses `CatalogProperties`/other; verify, then add Shared if any other Catalog type is referenced — else convert to B) |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs` | B |
| `backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs` | C (uses `CatalogAttributes`, `CatalogProperties`; keep both Catalog usings, add Shared) |

The B-vs-C decision for each file is "does any identifier other than `Cooling` come from the `Catalog` namespace?" If yes → C. If `Cooling` is the only Catalog symbol used → B.

- [ ] **Step 1: Edit each test file per its category**

For category B files: replace `using Anela.Heblo.Domain.Features.Catalog;` with `using Anela.Heblo.Domain.Shared;`.

For category C files: insert `using Anela.Heblo.Domain.Shared;` after the existing `using Anela.Heblo.Domain.Features.Catalog;` (alphabetical position is after `.Catalog.Attributes;` if present).

Example, `GetPackingOrderHandlerTests.cs` (category B), before:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
```
After:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
```

Example, `FlexiCoolingParserTests.cs` (category C), before:
```csharp
using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using FluentAssertions;
```
After:
```csharp
using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
```

For the two files marked as "verify, then decide" (e.g., `ShoptetApiExpeditionListSourceTests.cs`), first inspect:
```bash
grep -nE "\b(CatalogProperties|CatalogAttributes|ProductType|CatalogStock|ICatalogRepository|CatalogAggregate)\b" backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs
```
If the grep returns one or more hits → category C (add Shared, keep Catalog). If empty → category B (replace).

- [ ] **Step 2: Build the test projects**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -20
dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj 2>&1 | tail -20
```

Expected: both build clean.

---

## Task 7: Full-solution build

- [ ] **Step 1: Build the entire solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln 2>&1 | tail -40
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

If there are warnings on edited files specifically (e.g., `CS8019: Unnecessary using directive`), proceed to the format step in Task 9 — `dotnet format` will remove unused usings on edited files.

If `CS0246: 'Cooling' could not be found` remains anywhere, run the grep sweep in Task 8 *now* to locate the file missed.

---

## Task 8: Project-wide sweep for stragglers and stale Catalog references

- [ ] **Step 1: Grep for any remaining `Cooling` reference under `Anela.Heblo.Domain.Features.Catalog`**

Run:
```bash
grep -rn "Anela\.Heblo\.Domain\.Features\.Catalog" backend/src backend/test \
  --include='*.cs' \
  | grep -v "Anela\.Heblo\.Domain\.Features\.Catalog\." \
  | grep -v "Anela.Heblo.Persistence/Migrations/"
```

This filters out hits that go into sub-namespaces (e.g., `Catalog.Attributes`, `Catalog.Stock`) and migration files. The remaining hits all import the base `Catalog` namespace.

Manually inspect each remaining hit: it must reference at least one Catalog-namespace type that is **not** `Cooling`. If a file lists `using Anela.Heblo.Domain.Features.Catalog;` and uses **only** `Cooling`, fix it now: replace the using with `using Anela.Heblo.Domain.Shared;`.

- [ ] **Step 2: Grep for `Cooling` references that the plan may have missed**

Run:
```bash
grep -rn "\bCooling\b" backend/src backend/test --include='*.cs' \
  | grep -v "backend/src/Anela.Heblo.Persistence/Migrations/" \
  | grep -v "backend/src/Anela.Heblo.Domain/Shared/Cooling.cs" \
  | awk -F: '{print $1}' | sort -u
```

Cross-check the file list against Tasks 2–6. If any file is unaccounted for and **names** the `Cooling` type (vs. mere property access), add an edit per the right category.

- [ ] **Step 3: Re-run the full-solution build to confirm no regressions from sweep edits**

Run:
```bash
dotnet build backend/Anela.Heblo.sln 2>&1 | tail -20
```

Expected: clean build.

---

## Task 9: EF migration safety + format + tests

- [ ] **Step 1: Verify EF Core sees no model changes**

Run:
```bash
cd backend/src/Anela.Heblo.API && dotnet ef migrations has-pending-model-changes 2>&1 | tail -10 ; cd -
```

Expected: `No changes have been made to the model since the last migration.` If pending changes are reported, a duplicate `Cooling` type still exists somewhere or an entity property type silently changed — investigate before continuing.

- [ ] **Step 2: Run `dotnet format`**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | tail -20 \
  || dotnet format backend/Anela.Heblo.sln 2>&1 | tail -20
```

The first invocation reports whether anything needs formatting (CS8019 unused usings, whitespace). If it exits non-zero, the second invocation applies the fixes. Re-run `--verify-no-changes` after the fix to confirm clean state.

- [ ] **Step 3: Run all affected unit & integration tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CarrierCooling|FullyQualifiedName~ShoptetApi|FullyQualifiedName~GetPackingOrder|FullyQualifiedName~Expedition|FullyQualifiedName~ScanPackingOrder" \
  --no-build 2>&1 | tail -30

dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-build 2>&1 | tail -20
```

Expected: all tests pass. If a test fails with a serialization or comparison error around `Cooling`, investigate — that would indicate accidental type identity change (e.g., duplicate enum left in the Catalog namespace).

- [ ] **Step 4: Run the full backend test suite as a safety net**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build 2>&1 | tail -30
```

Expected: full backend suite green. (E2E suite is not required for this refactor per spec NFR-2.)

---

## Task 10: Commit

- [ ] **Step 1: Stage the changes**

Run:
```bash
git add backend/src/Anela.Heblo.Domain/Shared/Cooling.cs \
        backend/src/Anela.Heblo.Domain/Features/Catalog \
        backend/src/Anela.Heblo.Domain/Features/Logistics \
        backend/src/Anela.Heblo.Application \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi \
        backend/test
```

(The Catalog `Cooling.cs` deletion is captured by staging the `Features/Catalog` directory.)

- [ ] **Step 2: Confirm the staged diff is what you expect**

Run:
```bash
git status
git diff --cached --stat
```

Expected: one file added (`Domain/Shared/Cooling.cs`), one deleted (`Domain/Features/Catalog/Cooling.cs`), and the using-directive edits across the consumer/test files. No EF migration changes. No model snapshot changes.

- [ ] **Step 3: Commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor(backend): relocate Cooling enum to Anela.Heblo.Domain.Shared

Moves the Cooling enum (None/L1/L2) from Anela.Heblo.Domain.Features.Catalog
to Anela.Heblo.Domain.Shared so that ShoptetOrders, Logistics, Packaging,
CarrierCooling, the ShoptetApi adapter, and the Flexi adapter no longer take
a compile-time dependency on the Catalog module solely to reach a
temperature-chain shipping classification. The enum's CLR identity, integer
values, EF Core string-conversion persistence, and OpenAPI surface are
unchanged.
EOF
)"
```

Expected: commit created. The repo is `clean` per `git status` afterward.

---

## Self-Review Checklist

**Spec coverage**
- FR-1 (file move) → Task 1.
- FR-2 (namespace change to `Anela.Heblo.Domain.Shared`) → Task 1.
- FR-3 (all consumers updated) → Tasks 2–6 plus Task 8 sweep.
- FR-3 (post-edit grep) → Task 8 step 1.
- FR-4 (no migration changes, no model snapshot regeneration) → Task 9 step 1.
- FR-5 (frontend/generated client unchanged) → implicit; not edited, build succeeds (Task 7), and the OpenAPI schema is unchanged because the enum's string serialization is unchanged.
- NFR-1 (behavior preservation) → Task 9 steps 3 & 4 (tests pass) + Task 9 step 1 (EF stable).
- NFR-2 (build/test gates) → Tasks 7, 9.
- NFR-3 (module boundary alignment) → Task 8 step 1's grep enforces no consumer imports `Catalog` solely for `Cooling`.
- NFR-4 (atomicity) → Task 10 ships one commit covering every edit.

**Arch-review amendments folded in**
- "No-using-today files" callout → Task 2 has its own dedicated task with explicit before/after snippets.
- "`ApplicationModule.cs` / `CarrierCoolingModule.cs` likely need no change" → captured in "Files explicitly NOT edited" list.
- Spot-build per project after each task (vs. full-solution rebuilds on every change) → Tasks 2, 3, 4, 5, 6 each end with a focused build.
- Migration verification before commit → Task 9 step 1.

**Placeholders / hand-waving**
None — every step has either exact commands, exact code, or both. Where the implementer must classify a file (category B vs. C in Task 6), the decision rule is stated as a single grep command.

**Type / namespace consistency**
- Target namespace `Anela.Heblo.Domain.Shared` is used consistently in every snippet.
- Enum body is shown verbatim once (Task 1 step 2) and matches the original member set.
- The deleted path (`backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs`) is the same string everywhere.

---

## Status: COMPLETE
