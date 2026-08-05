# Design: Fold Logistics→Purchase boundary check into the shared `Rules()` theory

## UX/UI

N/A — this is a test-infrastructure-only change inside
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. There is no
user-facing surface, API, or UI involved.

## Component design

There is one component in scope: `ModuleBoundariesTests` (single test class, no
collaborators outside itself). No new component, type, or abstraction is introduced —
the design is a **subtraction**: remove a duplicate code path and let the pair be
absorbed by the code path that already exists and already runs ~35 other pairs.

### Current shape (as verified in the file)

| Element | Lines | Role |
|---|---|---|
| `Rules()` `TheoryData<ModuleBoundaryRule>` | 368–697 | Declares one row per enforced module-pair boundary. |
| `Consumer_types_should_not_reference_provider_owned_namespaces` | 699–743 | `[Theory]`, `[MemberData(nameof(Rules))]` — the single shared algorithm: enumerate consumer types → enumerate referenced types → check `IsForbidden` → allowlist lookup (direct + declaring-type fallback) → assert no violations. Runs once per `Rules()` row. |
| `Logistics_types_should_not_reference_Purchase_owned_namespaces` | 745–813 | `[Fact]` — hand-rolled duplicate of the theory body, scoped to one pair (Logistics consumer, Purchase provider), with its own local `forbiddenPrefixes`, empty `logisticsAllowlist`, and nested `IsLogisticsForbidden`. |
| `IsForbidden(Type, IReadOnlyList<string>)` | 988–1003 | Shared helper used by the theory (and by `Application_types_should_not_catch_SDK_exception_types_directly`, line 972). |
| `IsLogisticsForbidden(Type)` (local function) | 759–774 | Line-for-line duplicate of `IsForbidden`, scoped inside the Fact, only difference is capturing `forbiddenPrefixes` by closure instead of taking it as a parameter. |

### Target shape

- **Delete** lines 745–813 in full (the Fact method body, including its local
  `forbiddenPrefixes` array, `logisticsAllowlist`, the nested `IsLogisticsForbidden`
  function, and the `violations`/`Should().BeEmpty(...)` block).
- **Add one row** to the `Rules()` initializer (368–697). Placement: immediately after
  the "Logistics -> Catalog" row (ends at line 445, before "ExpeditionListArchive ->
  ExpeditionList" at 448), keeping the two Logistics-as-consumer rows adjacent for
  readability — this is not load-bearing, `TheoryData` order has no semantic effect.
- **No change** to `IsForbidden`, `EnumerateReferencedTypes`, or the theory method
  itself — the whole point is that the new row is executed by the *existing,
  unmodified* shared algorithm.
- **Allowlist**: inline `new HashSet<string>(StringComparer.Ordinal)` at the call
  site, matching the style already used for other zero-violation rows ("PackingMaterials
  -> Invoices" line 423, "ExpeditionListArchive -> ExpeditionList" line 456,
  "Analytics (Application) -> Catalog" line 467). No new named private field — every
  named allowlist field in the file (`LeafletAllowlist`, `LogisticsAllowlist`,
  `CatalogPurchaseAllowlist`, etc.) exists because it holds one or more justified
  entries; an empty set doesn't warrant a name.

### Interface / call contract

No public interface changes. `ModuleBoundaryRule` (record at lines 15–20) is reused
verbatim — this task adds a value, not a shape.

```
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

`InspectedAssembly` is omitted, defaulting to `"Anela.Heblo.Application"` — identical
to what the deleted Fact hard-coded via `Assembly.Load("Anela.Heblo.Application")`
(line 776), so assembly scope is unchanged.

### Behavioral equivalence check

The deleted Fact and the new row must produce the same pass/fail outcome today:

| Aspect | Old Fact | New row | Same? |
|---|---|---|---|
| Inspected assembly | `"Anela.Heblo.Application"` (hard-coded) | default `InspectedAssembly` = `"Anela.Heblo.Application"` | ✅ |
| Inspected namespace prefix | `"Anela.Heblo.Application.Features.Logistics"` | same | ✅ |
| Forbidden prefixes | 3 `Purchase` prefixes (Domain/Application/Persistence) | same 3, same order (order is irrelevant to `IsForbidden`) | ✅ |
| Allowlist | empty `HashSet<string>(StringComparer.Ordinal)` | empty `HashSet<string>(StringComparer.Ordinal)` | ✅ |
| Forbidden-check logic | local `IsLogisticsForbidden`, textually identical to `IsForbidden` | shared `IsForbidden` | ✅ (verified line-for-line against 988–1003) |
| Declaring-type fallback | present (797–803), identical logic to theory's (726–732) | present (theory's own) | ✅ |
| Type enumeration | `EnumerateReferencedTypes` (shared, same call) | same | ✅ |
| Assertion message | pair-specific wording | generic wording driven by `rule.Name` ("Logistics -> Purchase: consumer types must not reference...") | Message text differs but assertion condition (`violations.Should().BeEmpty()`) is identical — acceptable, cosmetic only. |

Since every input and the algorithm itself are identical, the new row cannot pass or
fail differently than the old Fact did against the current codebase state. No
allowlist entries need to be pre-populated.

## Data schemas

No database, API, or event schema is touched. The only structural element is the
existing `ModuleBoundaryRule` record (unchanged):

```csharp
public sealed record ModuleBoundaryRule(
    string Name,
    string InspectedNamespacePrefix,
    IReadOnlyList<string> ForbiddenNamespacePrefixes,
    IReadOnlySet<string> Allowlist,
    string InspectedAssembly = "Anela.Heblo.Application");
```

This task adds one more value of this existing type to the `TheoryData<ModuleBoundaryRule>`
collection returned by `Rules()`; the type itself is not modified.

## Verification approach

- `dotnet build` — must succeed with no new warnings.
- `dotnet test --filter FullyQualifiedName~ModuleBoundariesTests` — theory case count
  increases by one (new "Logistics -> Purchase" case), old Fact name disappears from
  test output, all cases pass.
- `grep -n "IsLogisticsForbidden\|Logistics_types_should_not_reference_Purchase"` over
  the file returns nothing post-change.
- `dotnet format` — apply repo formatting conventions to the touched region.
