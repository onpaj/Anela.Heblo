I have enough grounding. Writing the architecture review now.

# Architecture Review: Strongly-Type `MarginLevel` in Analytics Module

## Skip Design: true

Backend-only refactor with no new or changed UI components, screens, or visual design. Existing frontend `MarginLevelType` union and dropdown already constrain user selection — only the generated client typing changes shape.

## Architectural Fit Assessment

The change aligns with existing conventions: every other discriminator in the Analytics module (`ProductGroupingMode`, `AnalyticsProductType`, `BankStatementDateType`, `ImportDateType`) is a domain-layer enum living in `Anela.Heblo.Domain.Features.Analytics`. `MarginLevel` is the lone holdout.

**Critical name collision (must be addressed, not assumed away):** A type named `MarginLevel` already exists at `backend/src/Anela.Heblo.Domain/Features/Catalog/MarginLevel.cs` — but it is a value object (`class` with `Percentage`, `Amount`, `CostTotal`, `CostLevel`), not an enum, and it belongs to the Catalog module. Under vertical-slice rules (each module owns its domain types; cross-module communication is via `Contracts/` only — see `docs/architecture/development_guidelines.md`), Analytics is entitled to its own `MarginLevel` enum in `Domain/Features/Analytics/`. Namespaces disambiguate them. We accept the lexical collision as the lesser evil compared to introducing a misleading alternative name (`MarginTier`, `MarginLevelCode`) that would diverge from the wire contract `M0|M1|M2` and the existing frontend `MarginLevelType` literal.

**Other integration points the spec under-specifies:**
- `IMarginCalculator.CalculateAsync` takes `string marginLevel = "M2"` (line 47, `MarginCalculator.cs`) — must change.
- `IMonthlyBreakdownGenerator.Generate` takes `string marginLevel = "M2"` (line 13, `MonthlyBreakdownGenerator.cs`) — must change. Its private helpers `GenerateMonthlySegments` and `ProcessGroupForMonth` also need the enum.
- `GetProductMarginSummaryResponse.MarginLevel` is a `string` (line 14, response DTO) and is echoed back to the caller — must change for symmetry, otherwise the asymmetry produces a request-enum/response-string contract that the OpenAPI generator will surface as two different TS types.
- Two existing tests (`Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin`, `Handle_UnknownMarginLevel_FallsBackToM2` in `GetProductMarginSummaryHandlerTests.cs`) lock in the **legacy silent-fallback and case-insensitivity** semantics that this change explicitly removes. The spec's acceptance criterion "All previously-passing tests still pass" is incorrect — these tests must be deleted/replaced.

## Proposed Architecture

### Component Overview

```
[Controller]
     │  GET /api/analytics/product-margin-summary?marginLevel=M1
     │  ASP.NET model binding (JsonStringEnumConverter, case-insensitive by default for enums)
     ▼
[GetProductMarginSummaryRequest]    ── MarginLevel: enum ──┐
     │                                                     │ invalid value (e.g. "M9")
     │ MediatR                                             │ → 400 BadRequest before handler
     ▼                                                     │
[GetProductMarginSummaryHandler]                           │
     │ passes MarginLevel enum to:                         │
     ├── IMarginCalculator.CalculateAsync(...)             │
     ├── IMarginCalculator.GetMarginAmountForLevel(...)    │
     └── IMonthlyBreakdownGenerator.Generate(...)          │
              └── GetMarginAmountForLevel (delegated)     ◄┘
                       │
                       ▼
              switch over MarginLevel
                M0 / M1 / M2 → AnalyticsProduct.{M0,M1,M2}Amount
                default       → ArgumentOutOfRangeException
     │
     ▼
[GetProductMarginSummaryResponse] ── MarginLevel: enum (round-trip echo)
     │ NSwag → TS client
     ▼
[Frontend useProductMarginSummary hook]
     │  literal "M0" | "M1" | "M2" union (existing UI dropdown)
     ▼ existing select control unchanged
```

### Key Design Decisions

#### Decision 1: Where the enum lives
**Options considered:**
- (A) Put `MarginLevel` enum in `Domain/Features/Analytics/` (new file).
- (B) Put it in `Domain/Shared/` so Catalog and Analytics could share it.
- (C) Reuse the existing `Catalog.MarginLevel` type.

**Chosen approach:** (A) — Analytics-owned enum at `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginLevel.cs`.

**Rationale:** Vertical-slice rule: domain types are module-private; cross-module sharing goes through `Contracts/`. (C) is impossible — `Catalog.MarginLevel` is a value object, not an enum, with different semantics (margin computation result vs. discriminator). (B) is the wrong precedent: `AnalyticsProductType` exists *precisely because* Analytics owns its categorical types even when Catalog has a similar one (per the doc-comment on `AnalyticsProductType.cs`). Stay consistent.

#### Decision 2: Default value of the enum
**Options considered:**
- (A) Order members `M0, M1, M2` so `default(MarginLevel) == M0` (spec FR-1).
- (B) Keep `MarginLevel.M2` as the property default and accept that `default(MarginLevel)` would be `M0`.
- (C) Reorder to `M2, M0, M1` so `default(MarginLevel) == M2` and matches the property default.

**Chosen approach:** (B) — members ordered `M0, M1, M2`; the request DTO explicitly initializes `= MarginLevel.M2`.

**Rationale:** The spec is right to order `M0, M1, M2` (categorical, no `default` semantic baked in). Property-level defaulting is explicit (`public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;`), so the wire contract preserves the legacy "omitted → M2" behavior. (C) would couple member order to a default-value trick — fragile.

#### Decision 3: Throw vs swallow on undefined enum values
**Options considered:**
- (A) `_ => throw new ArgumentOutOfRangeException(...)` (spec FR-4).
- (B) `_ => product.M2Amount` to preserve legacy.

**Chosen approach:** (A).

**Rationale:** The entire point of the change. Once the parameter is typed, the default arm is only reachable via `(MarginLevel)99` — a programmer error that must surface, not hide. Invalid wire input is rejected by model binding *before* it reaches the switch (Decision 4).

#### Decision 4: How invalid wire input is rejected
**Options considered:**
- (A) Rely on default ASP.NET Core enum model binding (case-insensitive by default for query strings; `JsonStringEnumConverter` already globally registered at `Program.cs:142`).
- (B) Add a FluentValidation rule explicitly checking enum membership.

**Chosen approach:** (A).

**Rationale:** No custom code needed. Invalid query value (`marginLevel=M9`) yields a 400 via ModelState. Invalid JSON body value yields a 400 via the converter. (B) is redundant. Verify behaviorally — see Prerequisites.

#### Decision 5: Response DTO field type
**Options considered:**
- (A) Change `GetProductMarginSummaryResponse.MarginLevel` from `string` to `MarginLevel` (echoes the request type symmetrically).
- (B) Leave the response field as `string` (smaller diff).

**Chosen approach:** (A).

**Rationale:** Spec misses this. Symmetry matters: a request-side enum + response-side string forces the TS generator to emit two different types for the same conceptual value, and a future caller could legitimately complain. The serialized wire payload (`"M2"`) is identical either way.

## Implementation Guidance

### Directory / Module Structure

**New file:**
- `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginLevel.cs`

**Modified files (backend):**
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs` — property type.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryResponse.cs` — property type (additive to spec).
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — `CalculateTotalMarginForLevel`, `GenerateTopProducts` parameter types.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — `IMarginCalculator.CalculateAsync`, `IMarginCalculator.GetMarginAmountForLevel`, and the implementations.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — `IMonthlyBreakdownGenerator.Generate`, plus private `GenerateMonthlySegments` and `ProcessGroupForMonth` parameter types.

**Modified files (tests):**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`:
  - Update setup verifications (`"M2"` → `MarginLevel.M2`) in `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`.
  - **Delete** `Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin` (`"m1"` is no longer accepted — model binding handles casing, that test is about a string-API contract that no longer exists).
  - **Delete or invert** `Handle_UnknownMarginLevel_FallsBackToM2` — replace with `Handle_UndefinedEnumValue_ThrowsArgumentOutOfRangeException` at the calculator level (calling `GetMarginAmountForLevel(product, (MarginLevel)99)`).
  - Add a `WebApplicationFactory`-based integration test if one does not already exist: `Get_WithInvalidMarginLevelQueryString_Returns400`.

**Modified files (frontend):**
- `frontend/src/api/generated/api-client.ts` — regenerated on backend build (no manual edit).
- `frontend/src/api/hooks/useProductMarginSummary.ts` line 14 — the `string` parameter type tightens. The existing call sites pass `"M2"` literal — confirm the regenerated client accepts string literals or emitted enum members.
- `frontend/src/components/pages/ProductMarginSummary.tsx` lines 18, 47–48, 380 — the local `MarginLevelType` union may become redundant once the generator emits a constrained type; either drop it and import the generated type, or keep it and `satisfies` against the generated one. Decide after seeing what NSwag emits.

### Interfaces and Contracts

```csharp
// New
namespace Anela.Heblo.Domain.Features.Analytics;

public enum MarginLevel
{
    M0,
    M1,
    M2,
}
```

```csharp
// Changed
public interface IMarginCalculator
{
    Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        MarginLevel marginLevel = MarginLevel.M2,
        CancellationToken cancellationToken = default);

    decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel);
    // (unchanged members elided)
}

public interface IMonthlyBreakdownGenerator
{
    List<MonthlyProductMarginDto> Generate(
        MarginCalculationResult calculationResult,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        MarginLevel marginLevel = MarginLevel.M2);
}

public class GetProductMarginSummaryRequest : IRequest<GetProductMarginSummaryResponse>
{
    // ...
    public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;
    // ...
}

public class GetProductMarginSummaryResponse : BaseResponse
{
    // ...
    public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;  // was string
    // ...
}
```

```csharp
// Implementation body
public decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel) =>
    marginLevel switch
    {
        MarginLevel.M0 => product.M0Amount,
        MarginLevel.M1 => product.M1Amount,
        MarginLevel.M2 => product.M2Amount,
        _ => throw new ArgumentOutOfRangeException(nameof(marginLevel), marginLevel, null),
    };
```

### Data Flow

1. Client issues `GET /api/analytics/product-margin-summary?marginLevel=M1&...`.
2. ASP.NET Core binds query string `M1` → `MarginLevel.M1` (default case-insensitive enum binder).
   - Invalid value (`M9`, `xyz`, empty if explicitly required) → 400 via ModelState before handler executes.
3. MediatR dispatches the typed request to `GetProductMarginSummaryHandler`.
4. Handler forwards `request.MarginLevel` (enum) to `IMarginCalculator.CalculateAsync` and `IMonthlyBreakdownGenerator.Generate`.
5. Both delegate to `MarginCalculator.GetMarginAmountForLevel(product, marginLevel)`, which enum-switches into `product.{M0,M1,M2}Amount`.
6. Response carries `MarginLevel` enum back; `JsonStringEnumConverter` serializes it as `"M1"` over the wire. Frontend receives the same string it sent.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Catalog.MarginLevel` (value object class) and new `Analytics.MarginLevel` (enum) share a name — confusing for future readers and for tooling that suggests `using` directives | Medium | Always reference by fully qualified name in cross-module locations; add an XML doc comment on the new enum stating "Discriminator for which M{0,1,2} amount Analytics reports; distinct from the Catalog.MarginLevel value object." Do not add a `using static`. |
| Existing tests encode silent-fallback and case-insensitivity behavior that this change removes — spec's "all previously-passing tests still pass" criterion is wrong and a literal reading would block the change | High | Amend FR-7 (see Amendments below). Delete the two legacy tests by name; add the new throw-test. |
| Internal pass-throughs in `MonthlyBreakdownGenerator` and `IMarginCalculator.CalculateAsync` are not enumerated in FR-3/FR-5; partial change would leave a `string`-typed interface alive | High | Treat "no `string`-typed `marginLevel` remains anywhere in `Application/Features/Analytics`" as the acceptance gate (FR-5 already says this — but explicitly include `IMonthlyBreakdownGenerator.Generate` and `IMarginCalculator.CalculateAsync` in the list of mandatory changes). |
| `GetProductMarginSummaryResponse.MarginLevel` left as `string` produces an asymmetric request-enum / response-string contract on the wire client | Medium | Convert response field to `MarginLevel` enum (additive to spec, Decision 5). |
| NSwag output for `MarginLevel` may emit a TS `enum` (with members `M0=0,M1=1,M2=2`) instead of a string-literal union; that would break the current `setSelectedMarginLevel(e.target.value as MarginLevelType)` cast at `ProductMarginSummary.tsx:380` since `e.target.value` is a string and the emitted TS enum would be number-based | Medium | Verify NSwag config emits *string* enums (it should, given backend uses `JsonStringEnumConverter`). If the generated TS shape is a numeric enum, adjust frontend to use enum members instead of raw strings. Cross-check with how `ProductGroupingMode` is currently consumed on the frontend before committing. |
| ASP.NET Core query-string model binding for enums could reject `"m1"` lowercase if older case-sensitive behavior is in play | Low | Default binder is case-insensitive for enums; verify in the integration test by sending `m1` and `M1` and asserting equivalent responses. Documented as wire behavior — no code needed unless the test fails. |
| OpenAPI spec is regenerated on backend build; if the C# client `Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs` is committed to repo, it must be regenerated and committed as part of the same PR | Low | Run `dotnet build` in Debug to regenerate both clients; commit both alongside the source change. |

## Specification Amendments

1. **FR-3 / FR-5 (expand the surface list).** Add explicit mention of:
   - `IMarginCalculator.CalculateAsync` — change `string marginLevel = "M2"` → `MarginLevel marginLevel = MarginLevel.M2`.
   - `IMonthlyBreakdownGenerator.Generate` and its private helpers `GenerateMonthlySegments`, `ProcessGroupForMonth`.
   - The handler's private `CalculateTotalMarginForLevel(List<AnalyticsProduct>, string marginLevel)` and the `marginLevel` parameter threaded through `GenerateTopProducts`.

2. **New FR-8 — Response DTO symmetry.** Change `GetProductMarginSummaryResponse.MarginLevel` from `string` to `MarginLevel`. Default stays `MarginLevel.M2`. Wire format unchanged.

3. **FR-7 correction — tests to delete, not "keep passing".** Two existing tests assert legacy behavior that this change explicitly removes:
   - `Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin` (`GetProductMarginSummaryHandlerTests.cs` lines 282–329) — delete.
   - `Handle_UnknownMarginLevel_FallsBackToM2` (lines 331–379) — delete.
   Replace with:
   - `GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException` (calculator unit test).
   - `Get_WithInvalidMarginLevelQueryString_Returns400` (`WebApplicationFactory`-based integration test, if a similar harness exists in the test project; if not, accept that ASP.NET binding behavior is covered by framework guarantees and skip).
   - Optionally: a binder smoke test asserting `marginLevel=m1` (lowercase) binds to `MarginLevel.M1` (documents the case-insensitivity that survives via the framework).

4. **Risk acknowledgement** in FR-1: Add a sentence noting the name collision with `Catalog.MarginLevel` and the XML-doc mitigation.

## Prerequisites

- **No infrastructure or migration prerequisites.** Wire format unchanged, schema unchanged, no DI changes.
- **Verify before starting:**
  - `JsonStringEnumConverter` is registered globally — confirmed at `backend/src/Anela.Heblo.API/Program.cs:142`. No action needed.
  - NSwag generator emits string-named TS enums (or string-literal unions) for sibling enums like `ProductGroupingMode`. Inspect `frontend/src/api/generated/api-client.ts` for `ProductGroupingMode` shape before assuming `MarginLevel`'s output. This determines whether frontend cleanup at `ProductMarginSummary.tsx:380` is needed.
  - The Analytics test project (`backend/test/Anela.Heblo.Tests/Features/Analytics/`) is the only test location touching `marginLevel`. Grep confirmed only one file (`GetProductMarginSummaryHandlerTests.cs`).
- **Build order during implementation:**
  1. Add enum.
  2. Change all interface and implementation signatures in one pass; the compiler is the migration guide.
  3. `dotnet build` — must succeed before the frontend regenerates.
  4. `dotnet format`, `npm run build`, `npm run lint`.
  5. Run Analytics test project; expect the two legacy tests to fail (this is correct); replace them.