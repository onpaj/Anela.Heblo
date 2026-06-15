# Specification: Strongly-Type `MarginLevel` in Analytics Module

## Summary
Replace the stringly-typed `MarginLevel` parameter in the Analytics module with a proper C# enum, matching the convention already used by every other discriminator (`AnalyticsProductType`, `ProductGroupingMode`, `BankStatementDateType`, `ImportDateType`). This eliminates the silent-fallback-to-M2 bug on invalid input, constrains the generated TypeScript client to a union type, and makes the API surface consistent.

## Background
The `MarginLevel` field on `GetProductMarginSummaryRequest` is currently a raw `string` with allowed values `"M0"`, `"M1"`, `"M2"`. `MarginCalculator.GetMarginAmountForLevel` resolves it via `marginLevel.ToUpperInvariant()` inside a `switch` whose default arm silently returns M2 data. Any typo, casing slip in a future code path that skips normalisation, or unmapped value produces wrong margin data with no error surfaced to the caller.

Peer discriminators in the same module (`ProductGroupingMode`, `AnalyticsProductType`) are already enums. The inconsistency:
1. Makes the API harder to use correctly — clients have to know the magic-string contract.
2. Generates a permissive `string` parameter in the OpenAPI TypeScript client instead of a constrained union.
3. Hides invalid input behind a silent default, which is a correctness bug.

Filed by the daily arch-review routine on 2026-06-07.

## Functional Requirements

### FR-1: Introduce `MarginLevel` enum in the Domain layer
Add a new enum type `MarginLevel` to the Analytics domain feature folder with members `M0`, `M1`, `M2` (in that order, so the default `default(MarginLevel)` resolves to `M0`, not `M2` — the new behaviour must NOT preserve the legacy silent-default-to-M2 semantic; see FR-4).

**Location:** `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginLevel.cs` (alongside `ProductGroupingMode` and other Analytics enums — verify exact path during implementation against `docs/architecture/filesystem.md`).

**Acceptance criteria:**
- Enum is declared in the `Anela.Heblo.Domain.Features.Analytics` namespace (or the namespace used by sibling enums — match existing convention).
- Members are `M0`, `M1`, `M2` with no explicit integer values assigned (the enum is a categorical discriminator, not a numeric ordinal).
- Public, accessible from Application and API layers.

### FR-2: Convert `GetProductMarginSummaryRequest.MarginLevel` to the enum type
Change the property type on `GetProductMarginSummaryRequest` from `string` to `MarginLevel`.

**File:** `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs`, line 13.

**Acceptance criteria:**
- Property declaration becomes `public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;` (default preserved as M2 to keep the existing API contract — callers that omit the field continue to receive M2 data).
- The class remains a class (per `docs/architecture/development_guidelines.md`: DTOs are classes, never records).
- ASP.NET Core model binding round-trips the enum from query string and JSON body. Verify `JsonStringEnumConverter` is registered globally (it should be, given other enum DTOs work); if not, add it.

### FR-3: Update `IMarginCalculator.GetMarginAmountForLevel` signature
Change the parameter type from `string` to `MarginLevel` on both the interface and implementation.

**File:** `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`, lines 116–124, and the corresponding interface (`IMarginCalculator`).

**Acceptance criteria:**
- New signature: `decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel)`.
- Body is a `switch` expression over the enum:
  ```csharp
  return marginLevel switch
  {
      MarginLevel.M0 => product.M0Amount,
      MarginLevel.M1 => product.M1Amount,
      MarginLevel.M2 => product.M2Amount,
      _ => throw new ArgumentOutOfRangeException(nameof(marginLevel), marginLevel, null),
  };
  ```
- No `ToUpperInvariant()` call — string normalisation is no longer needed.
- The default arm throws on unmapped values rather than silently returning M2.

### FR-4: Remove silent-fallback behaviour
The previous default arm `_ => product.M2Amount` silently masked invalid input. The new implementation must surface invalid input as an exception (`ArgumentOutOfRangeException`). Because the parameter is now an enum, the only path to the default arm is a future enum member added without updating the switch — exactly the case where throwing is correct.

**Acceptance criteria:**
- Passing a non-declared enum value (e.g. `(MarginLevel)99`) throws `ArgumentOutOfRangeException`.
- An invalid query-string value (e.g. `marginLevel=M9`) is rejected by ASP.NET Core model binding with a 400 Bad Request before reaching the handler — verify the existing global model-binding error response shape is returned.

### FR-5: Update `GetProductMarginSummaryHandler` and any internal pass-throughs
Update all `marginLevel` references in the handler and any helper methods to use the enum type. No remaining `string`-typed `marginLevel` parameter should exist anywhere in the Analytics module after this change.

**File:** `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`.

**Acceptance criteria:**
- `grep -ri "marginLevel" backend/src/Anela.Heblo.Application/Features/Analytics` shows no `string`-typed `marginLevel` parameter or field.
- Handler compiles without casts or string conversions.
- All call sites of `GetMarginAmountForLevel` pass `MarginLevel` enum values.

### FR-6: Regenerate and adopt TypeScript client
After the backend change builds, the OpenAPI generator must produce a TypeScript union type (or generated enum) for `marginLevel` on the corresponding client method, replacing the previous `string` parameter.

**Acceptance criteria:**
- Generated client surfaces `marginLevel` as a constrained type (`"M0" | "M1" | "M2"` union or an emitted TS enum — whichever the generator currently produces for sibling enums like `ProductGroupingMode`).
- Frontend call sites for `GetProductMarginSummary` compile against the new type without `as any` or string casts.
- Any frontend code that constructed the request with a literal `"M0"`/`"M1"`/`"M2"` continues to work because string-literal union accepts the literals; if the generator emits a TS enum instead, update call sites to use enum members.

### FR-7: Update unit tests
Existing `MarginCalculator` and `GetProductMarginSummaryHandler` tests that pass `"M0"`/`"M1"`/`"M2"` strings must be updated to pass `MarginLevel.M0`/`M1`/`M2`. Add a test for the new throw-on-undefined-enum behaviour (FR-4).

**Acceptance criteria:**
- All previously-passing tests in `backend/test/**/Analytics/**` still pass.
- New test: `GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException`.
- New test (if integration-test coverage exists for the endpoint): invalid query string `marginLevel=M9` returns HTTP 400.
- Per `~/.claude/rules/testing.md`, coverage on touched files stays at or above 80%.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. Enum switch is equivalent or marginally faster than the prior string switch with `ToUpperInvariant()`. No new allocations.

### NFR-2: Backwards Compatibility (API contract)
The wire format for the request stays the same: `marginLevel=M0|M1|M2` in query string or JSON body. Existing API consumers (current frontend, any external integrations) continue to work without change, because `JsonStringEnumConverter` serialises enum members as their string names. The only behavioural change is that invalid values now produce a 400 instead of silently returning M2 data — this is the intended fix, not a regression.

### NFR-3: Consistency
After this change, every discriminator in the Analytics module is a strongly-typed enum. No stringly-typed discriminators remain.

### NFR-4: Validation gates
Per `CLAUDE.md`, before declaring done:
- `dotnet build` succeeds.
- `dotnet format` produces no changes.
- `npm run build` succeeds (verifies generated TS client and frontend compile).
- `npm run lint` succeeds.
- All Analytics tests pass.

## Data Model

No database schema changes. `MarginLevel` is a request-only discriminator that selects between three already-existing columns/properties on `AnalyticsProduct` (`M0Amount`, `M1Amount`, `M2Amount`). No migrations.

**New type:**
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public enum MarginLevel
{
    M0,
    M1,
    M2,
}
```

## API / Interface Design

**Endpoint:** `GET /api/analytics/product-margin-summary` (or whatever the existing route is — unchanged).

**Request DTO change:**
```csharp
public class GetProductMarginSummaryRequest
{
    // ...
    public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;
    // ...
}
```

**Wire format (unchanged):**
```
GET /api/analytics/product-margin-summary?marginLevel=M1&...
```

**Service interface change:**
```csharp
public interface IMarginCalculator
{
    decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel);
    // ...
}
```

**TypeScript client (regenerated):**
```ts
// before
marginLevel?: string;
// after (one of the two — depends on generator config)
marginLevel?: "M0" | "M1" | "M2";
// or
marginLevel?: MarginLevel; // emitted TS enum
```

## Dependencies

- **`JsonStringEnumConverter`** — must be globally registered so the enum serialises to/from its string name (`"M0"`, `"M1"`, `"M2"`) in JSON. Almost certainly already registered (other Analytics enums round-trip correctly today). Verify, don't assume.
- **ASP.NET Core query-string model binding for enums** — binds string enum names by default; no configuration needed.
- **OpenAPI / NSwag generation pipeline** — already in place per `docs/development/api-client-generation.md`. Re-runs on backend build.
- **No external services, no library additions.**

## Out of Scope

- Refactoring other stringly-typed parameters elsewhere in the codebase. This change is scoped to `MarginLevel` in the Analytics module only.
- Renaming `M0`/`M1`/`M2` to more descriptive names (`Gross`, `Contribution`, `Net`, etc.). Names stay as-is to preserve API contract.
- Database schema changes. None needed.
- Adding numeric values to the enum members. Categorical only.
- Localisation of margin-level display names in the frontend — separate concern.

## Open Questions

None.

## Status: COMPLETE