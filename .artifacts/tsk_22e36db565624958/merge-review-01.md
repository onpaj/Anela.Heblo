# Merge review — PR #3800

**Branch:** `harness/tsk_8709c000320640bd` → `main`
**Closes:** #3799
**Title:** [arch-review] TestInfrastructure: ModuleBoundariesTests duplicates the whole boundary-check theory as a bespoke Logistics→Purchase Fact

## Scope of the change

A single test file: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
(+11 / −70). No production code, no config, no migrations. The rest of the diff is
`.artifacts/**` markdown from earlier pipeline steps.

The change:
1. Deletes the bespoke `[Fact] Logistics_types_should_not_reference_Purchase_owned_namespaces`
   and its local `IsLogisticsForbidden` helper (a verbatim copy of the shared `IsForbidden`).
2. Adds one `ModuleBoundaryRule` row ("Logistics -> Purchase") to `Rules()`, so the pair
   is enforced by the shared `[Theory] Consumer_types_should_not_reference_provider_owned_namespaces`.

This matches the issue/PR intent exactly: fold a duplicated theory-as-a-Fact into the
shared data-driven theory. No scope creep.

## Correctness — equivalence verified by reading

I read the deleted Fact and the shared theory in full and confirmed byte-for-byte
behavioral equivalence:

| Aspect | Deleted Fact | New row + shared theory |
|---|---|---|
| Inspected assembly | `Assembly.Load("Anela.Heblo.Application")` | `InspectedAssembly` omitted → defaults to `"Anela.Heblo.Application"` (record default, line 20) |
| Consumer filter | `StartsWith("Anela.Heblo.Application.Features.Logistics", Ordinal)` | same prefix, same `StartsWith(..., Ordinal)` (line 717) |
| Forbidden prefixes | Domain/Application/Persistence `.Purchase` (3) | identical 3-element array |
| Forbidden test | `Equals(prefix)` or `StartsWith(prefix+".")`, Ordinal | shared `IsForbidden` (lines 929–944) is identical |
| Allowlist | `new HashSet<string>(StringComparer.Ordinal)` (empty) | `new HashSet<string>(StringComparer.Ordinal)` (empty) inline |
| DeclaringType fallback | present | present in shared theory (lines 737–743) |
| Reference enumeration | `EnumerateReferencedTypes` | same shared helper |

The new row's inline empty-allowlist style matches 8 existing rows. Enforcement is
strictly ≥ before: the shared theory also carries the declaring-type fallback, which the
old Fact already had, so no coverage is lost. Since the old Fact passed with an empty
allowlist (no violations exist today), the new row must pass identically.

## Blast radius

None. Test-only, no auth/secrets/migrations/payments/public-API/CI-release surface. If
the refactor were somehow wrong, the failure mode is a red test in CI, not a bad merge to
production.

## Independent verification

- Read the full diff and the surrounding source (record definition, `Rules()`, shared
  theory, `IsForbidden`).
- Ran `dotnet test` filtered to `ModuleBoundariesTests` to completion — see result below.

