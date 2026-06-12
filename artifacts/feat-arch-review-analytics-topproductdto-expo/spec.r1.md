# Specification: Remove TopProductDto Backward-Compatibility Shims

## Summary
Remove the two backward-compatibility shim properties (`ProductCode`, `ProductName`) from `TopProductDto`, replacing all call-sites with the canonical `GroupKey` / `DisplayName` properties. This eliminates duplicate naming in the OpenAPI schema and aligns with the project rule against backward-compatibility hacks for unused code.

## Background
`TopProductDto` currently exposes two computed read-only properties whose only purpose is to alias the canonical fields:

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs, lines 26-27
// Keep for backward compatibility
public string ProductCode => GroupKey;
public string ProductName => DisplayName;
```

These shims:
- Violate the explicit CLAUDE.md guidance against backward-compatibility hacks.
- Surface as extra read-only properties on the generated TypeScript client, inflating the OpenAPI schema.
- Create ambiguity about which property name is canonical (`ProductCode` vs `GroupKey`, `ProductName` vs `DisplayName`).

Since this is a solo-developer project with no external API consumers, there is no compatibility constraint preventing direct removal.

## Functional Requirements

### FR-1: Remove shim properties from TopProductDto
Delete the `ProductCode` and `ProductName` computed properties (and the `// Keep for backward compatibility` comment) from `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs`.

**Acceptance criteria:**
- `TopProductDto.cs` no longer declares `ProductCode` or `ProductName`.
- The `// Keep for backward compatibility` comment is removed.
- The remaining properties (`GroupKey`, `DisplayName`, and any other existing fields) are unchanged.
- `dotnet build` succeeds for the backend solution.

### FR-2: Update backend call-sites
Identify and update every backend reference to `.ProductCode` / `.ProductName` on a `TopProductDto` instance to use `.GroupKey` / `.DisplayName` respectively. Scope includes production code under `backend/src` and tests under `backend/test`.

**Acceptance criteria:**
- `grep -r "\.ProductCode\|\.ProductName"` in `backend/src` and `backend/test` returns no matches against `TopProductDto` instances.
- All affected backend unit/integration tests pass: `dotnet test`.
- `dotnet format` reports no violations.

### FR-3: Regenerate the TypeScript OpenAPI client
After the backend change, regenerate the TypeScript OpenAPI client so that `TopProductDto` in the generated client no longer carries `productCode` / `productName` fields.

**Acceptance criteria:**
- The regenerated TypeScript `TopProductDto` type contains `groupKey` and `displayName` but not `productCode` or `productName`.
- `npm run build` succeeds in `frontend/`.

### FR-4: Update frontend call-sites
Identify and update every frontend reference to `.productCode` / `.productName` on a `TopProductDto` instance to use `.groupKey` / `.displayName`. This includes components, hooks, tests, and any TypeScript fixtures.

**Acceptance criteria:**
- `grep -r "\.productCode\|\.productName"` against `TopProductDto` usages in `frontend/src` returns no matches.
- `npm run lint` passes.
- `npm run build` succeeds.
- Affected frontend unit tests pass.
- Visual behavior of Analytics screens that render top-product data is unchanged (column headers, sort, filter still work).

### FR-5: Verify no UI regression on Analytics top-products view
Manually exercise (or rely on existing tests covering) the Analytics screens that display top-product data to confirm product code and name still render correctly after the rename.

**Acceptance criteria:**
- Pages/components consuming `TopProductDto` render the product identifier and display name identically to pre-change behavior.
- No console errors related to undefined `productCode` / `productName` properties.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The removed properties were trivial getters; their elimination marginally reduces serialization payload size.

### NFR-2: Security
No security impact. No authentication, authorization, or data-access semantics change.

### NFR-3: Maintainability
After the change, there must be exactly one canonical name per concept in `TopProductDto` (`GroupKey` for the product identifier, `DisplayName` for the human-readable name). This reduces cognitive load and prevents drift between aliased fields.

### NFR-4: Backwards Compatibility
Not required. Per project facts, the solution is a solo-developer monorepo with no external API consumers; the OpenAPI client is regenerated on every build and consumed only by the in-repo frontend.

## Data Model
No data model changes. `TopProductDto` is a transport-layer DTO; the underlying domain entities, database schema, and persistence layer are untouched. Only the DTO's surface shrinks from {`GroupKey`, `DisplayName`, `ProductCode`, `ProductName`, …} to {`GroupKey`, `DisplayName`, …}.

## API / Interface Design

### Backend DTO (after change)
```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs
public class TopProductDto
{
    public string GroupKey { get; set; }
    public string DisplayName { get; set; }
    // ... other existing properties unchanged
}
```

### Generated TypeScript client (after regeneration)
```typescript
interface TopProductDto {
  groupKey: string;
  displayName: string;
  // ... other existing properties unchanged
}
```

No endpoint routes, HTTP verbs, request shapes, or response envelopes change. Only the response body schema for endpoints returning `TopProductDto` shrinks by two redundant fields.

## Dependencies
- **OpenAPI client generation pipeline** — `npm run build` (or the documented client generation step in `docs/development/api-client-generation.md`) must run after the backend change to regenerate the TypeScript types.
- **Existing Analytics module** — no new external libraries or services introduced.
- **Test suites** — backend (`dotnet test`) and frontend (`npm test`) must pass after migration.

## Out of Scope
- Renaming `GroupKey` / `DisplayName` to anything else. The canonical names stay as-is.
- Refactoring other DTOs in the Analytics module or elsewhere.
- Auditing the rest of the codebase for similar backward-compatibility shims (filed separately if discovered).
- Modifying business logic, query handlers, or analytics calculations.
- Database schema changes or migrations.
- Adding new tests beyond what is needed to keep existing coverage green.

## Open Questions
None.

## Status: COMPLETE