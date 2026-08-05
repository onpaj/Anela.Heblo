# Plan: Fold Logistics→Purchase boundary check into the shared `Rules()` theory

## Summary

`ModuleBoundariesTests.cs` enforces ~35 module-pair boundaries via one data-driven
`[Theory]` (`Consumer_types_should_not_reference_provider_owned_namespaces`, backed
by `Rules()`), except for the Logistics→Purchase pair, which is hand-rolled as its
own `[Fact]` (`Logistics_types_should_not_reference_Purchase_owned_namespaces`,
lines 745–813) with a duplicated private `IsLogisticsForbidden` helper
(lines 759–774). This task removes the bespoke Fact and its duplicate helper,
replacing them with one more `ModuleBoundaryRule` row so all ~36 pairs share the
same enforcement code path.

## Context

The theory's shared algorithm (`EnumerateReferencedTypes`, `IsForbidden`, the
declaring-type fallback for compiler-generated nested types) is a living,
occasionally-strengthened piece of test infrastructure. Because the
Logistics→Purchase check reimplements that algorithm under a different name, any
future fix or enhancement to the shared theory silently does not apply to this one
pair — the exact kind of drift this test suite exists to prevent. This is a pure
test-infrastructure refactor with no production code involved; verified via direct
read of `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
(current line numbers: Fact at 745–813, `IsForbidden` at 988–1003, `Rules()` closes
at 697, theory at 699–743).

## Functional requirements

**FR-1 — Remove the bespoke Fact and its duplicated helper.**
Delete `Logistics_types_should_not_reference_Purchase_owned_namespaces`
(lines 745–813) in full, including its local `forbiddenPrefixes`, empty
`logisticsAllowlist`, and the nested `IsLogisticsForbidden` function
(lines 759–774).
- Acceptance: `grep -n "IsLogisticsForbidden\|Logistics_types_should_not_reference_Purchase"`
  over the file returns nothing.

**FR-2 — Add the equivalent rule row to `Rules()`.**
Insert one `ModuleBoundaryRule` entry into the `Rules()` `TheoryData` (alongside
the other Logistics/Purchase-adjacent rows, e.g. near "Logistics -> Manufacture"
or "Purchase -> Catalog" for readability):
```csharp
new ModuleBoundaryRule(
    Name: "Logistics -> Purchase",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Logistics",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Purchase",
        "Anela.Heblo.Application.Features.Purchase",
        "Anela.Heblo.Persistence.Purchase",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```
Use an inline empty `HashSet<string>(StringComparer.Ordinal)` for the allowlist
(matching the style already used for other zero-violation pairs like
"PackingMaterials -> Invoices" and "ExpeditionListArchive -> ExpeditionList"),
rather than introducing a new named `LogisticsPurchaseAllowlist` field — there is
nothing to annotate.
- Acceptance: the new row is present in `Rules()`; `Consumer_types_should_not_reference_provider_owned_namespaces`
  now covers 1 more case than before the change.

**FR-3 — Preserve enforcement strength.**
The replacement must forbid exactly the same three namespace prefixes
(`Anela.Heblo.Domain.Features.Purchase`, `Anela.Heblo.Application.Features.Purchase`,
`Anela.Heblo.Persistence.Purchase`) against the same inspected prefix
(`Anela.Heblo.Application.Features.Logistics`) with an empty allowlist (the
original Fact had no allowlist entries, i.e. zero known/accepted violations
today).
- Acceptance: `dotnet test --filter FullyQualifiedName~ModuleBoundariesTests` passes
  with no new failures — i.e. no real Logistics→Purchase violation exists today
  that the old Fact was silently missing and the new theory row would newly catch.

## Non-functional requirements

- No behavior change in what is enforced — this is a deduplication, not a policy
  change. If running the shared theory against the new row surfaces a real
  violation that the duplicated (and possibly subtly different) old code missed,
  stop and report it rather than silently adding an allowlist entry — that would
  be a scope-creep architecture fix, not this cleanup.
- No production code changes; `backend/test/` only.

## Data model

N/A — no domain/data entities involved. The only "model" is the existing
`ModuleBoundaryRule` record (`Name`, `InspectedNamespacePrefix`,
`ForbiddenNamespacePrefixes`, `Allowlist`, `InspectedAssembly`), already defined
at lines 15–20 and reused as-is.

## Interfaces

N/A — internal test file only, no API/UI/event surface.

## Dependencies and scope

**Depends on:** nothing external; self-contained within
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.

**In scope:**
- Delete the Fact + `IsLogisticsForbidden`.
- Add the one `Rules()` row.

**Out of scope:**
- Any other boundary pair or allowlist in the file.
- Strengthening `EnumerateReferencedTypes` or `IsForbidden` themselves.
- Fixing any newly-surfaced real violation beyond adding it to the allowlist with
  a justification comment if (and only if) the shared theory catches something
  the old Fact missed (see NFR above) — if that happens, flag it for a separate
  task rather than silently expanding scope here.

## Rough plan

1. Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.
2. Add the new `ModuleBoundaryRule` row (FR-2) to the `Rules()` `TheoryData`.
3. Delete the `Logistics_types_should_not_reference_Purchase_owned_namespaces`
   Fact and its `IsLogisticsForbidden` local function (FR-1).
4. Run `dotnet build` then
   `dotnet test --filter FullyQualifiedName~ModuleBoundariesTests` to confirm the
   theory now runs one more case and everything passes.
5. Run `dotnet format` to match repo formatting conventions.
6. Sanity-check line-count reduction is roughly what's expected (~65 fewer lines)
   and that no other test in the file references `IsLogisticsForbidden` or the
   deleted Fact by name (e.g. in comments or `[MemberData]` wiring elsewhere).

## Open questions

- **Placement of the new row within `Rules()`:** the file has no strict ordering
  convention (rows appear roughly grouped by consumer module but not
  alphabetically or by discovery date). Default: insert it near the other
  Logistics-consumer rows ("Logistics -> Manufacture" at line ~403, "Logistics ->
  Catalog" at line ~436) for local readability. Not load-bearing — any position
  in the `TheoryData` is functionally identical.
- **Naming the allowlist field:** default is to inline an empty
  `HashSet<string>(StringComparer.Ordinal)` rather than declare a new named
  private field, consistent with several existing zero-violation rows. If a
  reviewer prefers every rule to have a named allowlist field for consistency,
  that's a one-line change to swap in `LogisticsPurchaseAllowlist`.
