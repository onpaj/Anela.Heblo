# Architecture Review: Relocate `Cooling` enum to shared Domain namespace

## Skip Design: true

(Backend-only refactor with zero UI/UX or contract-shape changes. The OpenAPI schema and TypeScript client surface are unchanged.)

## Architectural Fit Assessment

The proposal aligns cleanly with existing patterns:

- `backend/src/Anela.Heblo.Domain/Shared/` already exists and hosts cross-cutting domain types (`CurrencyCode.cs`, `Result.cs`, `Rag/DocumentType.cs`). `CurrencyCode` uses the flat `Anela.Heblo.Domain.Shared` namespace — the same target the spec picks for `Cooling`.
- The codebase explicitly forbids "Direct access to another module's entities" (`docs/architecture/development_guidelines.md` §Forbidden Practices). Today, ShoptetOrders, Logistics, Analytics, Manufacture, ShoptetApi adapter, and Flexi adapter all pull `Anela.Heblo.Domain.Features.Catalog` solely to reach `Cooling` — a textbook violation.
- A reflection-based module-boundary test (`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`) already enforces this rule for several module pairs but does **not** currently cover any consumer→Catalog edge. After the refactor, the door is open to add such a rule without an allowlist explosion.

Main integration points are import-only: every consumer's `using` directives plus the EF Core column configuration (`HasConversion<string>()` on a `varchar(10)`), whose stored values are namespace-independent.

## Proposed Architecture

### Component Overview

```
Before
──────
                ┌────────────────────────────────────┐
                │ Domain.Features.Catalog            │
                │   ├── Cooling  (enum)              │◄────────────┐
                │   ├── CatalogProperties            │             │
                │   └── Attributes/CatalogAttributes │             │
                └────────────────────────────────────┘             │
                          ▲                                        │
       ┌──────────────────┼──────────────────┬──────────────┐      │
       │                  │                  │              │      │
  Logistics         ShoptetOrders        Manufacture     Analytics │
  Application       Application/Domain   Domain          Domain    │
  ShoptetApi adapter   ─────┘            ─────┘          ─────┘    │
  Flexi adapter   ─────────┘                                       │
                  (all six modules import Catalog only for Cooling)┘

After
─────
                ┌──────────────────────────────────┐
                │ Domain.Shared                    │
                │   ├── CurrencyCode               │
                │   ├── Result                     │
                │   ├── Cooling   (enum) ◄─────────┼──────── all consumers
                │   └── Rag/DocumentType           │         (incl. Catalog)
                └──────────────────────────────────┘

                ┌──────────────────────────────────┐
                │ Domain.Features.Catalog          │
                │   ├── CatalogProperties          │── uses Shared.Cooling
                │   └── Attributes/CatalogAttrs    │── uses Shared.Cooling
                └──────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Target namespace
**Options considered:**
- A) `Anela.Heblo.Domain.Shared` (flat — same as `CurrencyCode`, `Result`).
- B) `Anela.Heblo.Domain.Shared.Logistics` (sub-namespace by domain concept).
- C) A new shared project (`Anela.Heblo.Domain.Shared.dll`).

**Chosen approach:** A — `Anela.Heblo.Domain.Shared`.

**Rationale:** `CurrencyCode` already lives flat in this namespace and is used cross-module identically. Introducing `Shared.Logistics` for a 3-member enum creates a precedent without a sibling, and `Rag/` is the lone sub-namespace today (justified because it groups multiple RAG types). One enum doesn't earn its own bucket. A new project is out of proportion for a one-type move. The spec correctly excludes B and C from scope.

#### Decision 2: Catalog's own consumers of `Cooling`
**Options considered:**
- A) Leave `CatalogProperties` and `CatalogAttributes` in `Domain.Features.Catalog`; add `using Anela.Heblo.Domain.Shared;` so they continue to see `Cooling`.
- B) Move `CatalogProperties.Cooling` to a different mechanism entirely.

**Chosen approach:** A.

**Rationale:** `CatalogProperties.Cooling` represents the product-side metadata; that's a legitimate Catalog-owned concept that happens to reference a shared enum. The relationship inverts cleanly: Catalog depends on Shared (allowed), no module depends on Catalog (now enforceable). Crucially, `CatalogProperties.cs` has **no `using` directives today** — the same-namespace lookup currently resolves `Cooling`. After the move, the implementer MUST add `using Anela.Heblo.Domain.Shared;` or that file fails to compile. The spec lists this correctly; flag it as the single highest-risk edit because the "just change usings" mental model can skip files that don't have any.

#### Decision 3: Atomicity strategy
**Options considered:**
- A) Single PR moves the file and updates every consumer simultaneously.
- B) Temporary `[Obsolete]`-style type forwarder in `Domain.Features.Catalog.Cooling` aliasing the new location.

**Chosen approach:** A (single PR), aligns with spec NFR-4.

**Rationale:** C# doesn't support cross-namespace type forwarders inside the same assembly without using `[TypeForwardedTo]` (assembly-level only) or a wrapper struct. A wrapper introduces a distinct type and breaks JSON/EF identity. Single-PR atomicity is the only safe path. Build won't succeed until every consumer is updated, which the spec acknowledges.

## Implementation Guidance

### Directory / Module Structure

```
DELETE:  backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs
CREATE:  backend/src/Anela.Heblo.Domain/Shared/Cooling.cs
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

No new folders. No `.csproj` edits. No new tests (per spec scope).

### Interfaces and Contracts

No interface, DTO, MediatR contract, controller signature, or EF entity type changes. The only edits to non-`Cooling.cs` files are `using` directives at the top of each consumer.

**Edit pattern by file class:**

| File class | Edit |
|---|---|
| File has `using Anela.Heblo.Domain.Features.Catalog;` only for `Cooling` | Replace with `using Anela.Heblo.Domain.Shared;` |
| File has `using Anela.Heblo.Domain.Features.Catalog;` for `Cooling` + other Catalog types | Keep existing using, add `using Anela.Heblo.Domain.Shared;` alongside |
| File has no using and is itself in `Anela.Heblo.Domain.Features.Catalog` (e.g. `CatalogProperties.cs`, `CatalogAttributes.cs`) | **Add** `using Anela.Heblo.Domain.Shared;` — easy to miss, no existing using to mutate |
| File uses `Cooling` only as a string in metadata (EF migrations, `ApplicationDbContextModelSnapshot.cs`) | Do not touch — `Cooling` is a column name there, not a type reference |

The third row is the silent-failure zone. Spot-check it explicitly.

### Data Flow

Data flow is unchanged. The EF Core configuration in `CarrierCoolingSettingConfiguration` (`HasConversion<string>().HasMaxLength(10)`) writes and reads enum member names; the CLR namespace is irrelevant to persistence. The OpenAPI generator emits a string-valued enum schema based on member names, unaffected.

The migration snapshot stores type names as **strings** (`"Anela.Heblo.Domain.Features.Logistics.CarrierCoolingSetting"`) for entities, not for primitive/enum property types. Cooling appears in the snapshot as `b.Property<string>("Cooling")` — the property name only — confirming no snapshot regeneration is needed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `CatalogProperties.cs` and `CatalogAttributes.cs` have **no `using` lines today**; implementer pattern-matches "replace using" and skips them, breaking Catalog build. | HIGH | Spec FR-3 lists them. Implementer must verify by attempting `dotnet build` after the move — Catalog will fail loudly if missed. Add an explicit Read of both files before editing. |
| Hidden consumer added between spec time and execution (a new feature touching `Cooling`). | MEDIUM | Spec mandates a post-edit `grep -rn "Cooling" backend/` sweep. Run it; any remaining `using Anela.Heblo.Domain.Features.Catalog` that resolves only `Cooling` is a bug. |
| EF Core re-evaluates the model and triggers a "pending model changes" warning if a different enum type identity is detected (e.g. accidental duplicate `Cooling` left in Catalog). | MEDIUM | After the move, run `dotnet ef migrations has-pending-model-changes` (spec FR-4). It must report no pending changes. If it does, a duplicate type remains somewhere. |
| Module boundary test (`ModuleBoundariesTests.cs`) does **not** currently cover Catalog as a forbidden provider, so the refactor's effect isn't pinned by CI. | LOW | Out of scope per spec, but worth a follow-up issue: add a rule for `Logistics`/`ShoptetOrders`/`Analytics`/`Manufacture` → `Anela.Heblo.Domain.Features.Catalog`. Without this guard, the violation can return. |
| `using Anela.Heblo.Domain.Features.Catalog;` is left intact in a consumer that no longer needs *any* Catalog type after the edit, producing a dead using (CS8019). | LOW | `dotnet format` removes unused usings on edited files. NFR-2 requires `dotnet format` to pass. |
| Compiler-generated allowlist drift: when the Catalog-boundary test is eventually added, async state machines or display classes for consumer code may need allowlist entries (as seen with the Logistics→Manufacture entries). | LOW | Not part of this PR. When the test is added, follow the established allowlist-with-justification pattern from `ModuleBoundariesTests.cs:29-64`. |

## Specification Amendments

The spec is complete. Two clarifications worth folding into FR-3 to harden execution:

1. **Explicitly flag the no-using-today files.** `CatalogProperties.cs` and `CatalogAttributes.cs` currently rely on **same-namespace lookup** to see `Cooling`. After the move they must gain a brand-new `using Anela.Heblo.Domain.Shared;` rather than have an existing using rewritten. Verified by reading `CatalogProperties.cs` — it has zero `using` directives at the top. Call this out as a sub-bullet in FR-3 so the implementer does not pattern-match "replace existing using" and miss them.

2. **`ApplicationModule.cs` and `CarrierCoolingModule.cs` likely need no change.** Grep shows neither file references the `Cooling` *type* — only names in DI strings (`AddCarrierCoolingModule`, `IPipelineBehavior<SetCarrierCoolingRequest, …>`). The spec's "only if it directly references `Cooling`" qualifier is correct; recommend strengthening to "verify with `grep -n '\bCooling\b'` per file and edit only if a type reference exists." This avoids speculative edits.

No other functional or structural amendments are needed.

## Prerequisites

None. The refactor is self-contained:

- `Anela.Heblo.Domain/Shared/` folder already exists.
- No new NuGet packages, project references, migrations, configuration, or infrastructure changes.
- No coordination with frontend (generated TypeScript client is regenerated at build; semantic diff is empty).
- No database changes — column type and stored string values are byte-identical.

Implementer can start immediately. Suggested execution order to minimize broken-build time: (1) move the file and update its namespace, (2) edit all C# consumers in a single batch using the table in *Edit pattern by file class*, (3) `dotnet build`, (4) `dotnet format`, (5) `dotnet test` on the touched test projects, (6) `dotnet ef migrations has-pending-model-changes`, (7) final `grep -rn "Anela\.Heblo\.Domain\.Features\.Catalog" backend/` to confirm every remaining hit legitimately uses a non-`Cooling` Catalog type.