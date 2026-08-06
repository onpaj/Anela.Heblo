# Architecture Review: Migrate `useManufacturingStockAnalysis` to the generated OpenAPI client

## Skip Design: true

## Architectural Fit Assessment

This is a textbook conformance fix, not new architecture. The codebase's established pattern — confirmed in both `docs/development/api-client-generation.md` and every sibling Manufacture hook (`useManufactureBatch.ts`, `useManufactureOrders.ts`, `useManufactureSettings.ts`) — is: obtain the client via `getAuthenticatedApiClient()`, call a generated `apiClient.<operationId>(...)` method, and let NSwag-generated classes carry the request/response shape end-to-end. `useManufacturingStockAnalysis.ts` is the outlier: it hand-declares six types that duplicate generated ones field-for-field, builds the query string itself, and reaches into `(apiClient as any).baseUrl` / `.http.fetch`.

This is not a hypothetical anti-pattern the spec is inventing — the project's own docs call out this exact construct by name:

> **❌ AVOID**: `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`
> These reach into private fields of the NSwag-generated class. If NSwag renames those fields, the code breaks at runtime with no compile-time warning.
> — `docs/development/api-client-generation.md`

I independently verified the spec's central factual claim: `frontend/src/api/generated/api-client.ts` already contains `manufacturingStockAnalysis_GetStockAnalysis(timePeriod, customFromDate, customToDate, productFamily, criticalItemsOnly, majorItemsOnly, adequateItemsOnly, unconfiguredOnly, searchTerm, pageNumber, pageSize, sortBy, sortDescending, salesMultiplier, isExport)` (line ~7622) returning `Promise<GetManufacturingStockAnalysisResponse>`, plus generated `GetManufacturingStockAnalysisResponse`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, `ManufacturingStockSortBy` (string enum, 13 members) and `ManufacturingStockSeverity` (string enum: `Critical`/`Major`/`Minor`/`Adequate`/`Unconfigured`) all present and structurally matching the hand-coded frontend types. No backend or codegen work is needed. This confirms the spec's scope is correct: two call sites in the frontend need to be rewired, nothing else.

One thing worth flagging explicitly for implementers: the hand-coded `ManufacturingStockSeverity` in the current hook is a **numeric** enum (`Critical = 0, Major = 1, ...`), while the generated one is a **string** enum (`Critical = "Critical", ...`). I checked every consumer in `ManufacturingStockAnalysis.tsx` (`grep` for `Severity` and numeric literal comparisons) and confirmed all ~17 usages compare against the enum member (`ManufacturingStockSeverity.Critical`, etc.), never a raw `0`/`1`/`2` literal or template-interpolated number. So swapping the underlying representation is safe by construction — but it's exactly the kind of thing that would silently misbehave (e.g. a CSS class keyed by numeric value, or a `<select>` option value) if a future change assumed the numeric encoding. Worth a one-line mention in the PR description.

## Proposed Architecture

### Component Overview

No new components. Two existing call sites move from a hand-rolled fetch path to the standard generated-client path already used by every other Manufacture hook:

```
ManufacturingStockAnalysis.tsx
        │
        ├── useManufacturingStockAnalysisQuery(request)  ──▶  useManufacturingStockAnalysis.ts
        │         (TanStack Query hook, unchanged shape)         │
        │                                                        ▼
        │                                          apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)
        │                                                        │
        └── handleExport()  ─────────────────────────────────────┘
                  (same generated method, isExport=true)
                                                                   ▼
                                                    GET /api/manufacturing-stock-analysis
                                                    (ManufacturingStockAnalysisController — unchanged)
```

Before: both the hook's `queryFn` and `handleExport` independently reconstruct the same query string and both bypass typing via `(apiClient as any).http.fetch`. After: both call the single generated method; the URL-building, encoding, and response parsing/error-throwing (`throwException` on non-2xx) live in exactly one place — the NSwag-generated code — as the codebase's convention dictates everywhere else.

### Key Design Decisions

#### Decision 1: Re-export generated types from the hook module vs. repoint imports in the page component
**Options considered:**
- (a) Change `ManufacturingStockAnalysis.tsx` and `ManufactureBatchPlanning.tsx` to import `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto` directly from `../../api/generated/api-client`.
- (b) Keep `useManufacturingStockAnalysis.ts` as the single import surface: import the generated types there, re-export them, leave the two consumer files' import statements untouched.

**Chosen approach:** (b), per spec FR-4.

**Rationale:** This matches the codebase's own convention — hook files are the local "API surface" for a feature area and other pages already import filter/request shapes from their corresponding hook module rather than reaching into `api/generated/api-client` directly. It also minimizes the diff blast radius (two import statements in `ManufacturingStockAnalysis.tsx`, one in `ManufactureBatchPlanning.tsx` stay untouched) — consistent with this project's "surgical changes" convention. The re-export is a thin passthrough (`export { ManufacturingStockSeverity, ManufacturingStockSortBy } from "../generated/api-client"`), so there's no meaningful abstraction cost.

#### Decision 2: `TimePeriod` enum duality — alias-and-cast vs. unify vs. mapping table
**Options considered:**
- Unify the app-level `TimePeriod`/`TimePeriodFilter` (`utils/timePeriod/timePeriod.ts`) with the generated client's `TimePeriod`.
- Write an explicit value-by-value mapping function between the two enums.
- Alias the generated import (`import { TimePeriod as GeneratedTimePeriod } from "../generated/api-client"`) and convert at the single call boundary via a same-string-value cast.

**Chosen approach:** Alias-and-cast at the boundary, per spec FR-3.

**Rationale:** I confirmed both enums are string enums with identical members (`PreviousQuarter`, `FutureQuarter`, `Y2Y`, `PreviousSeason`, `Q9M`, `CustomPeriod`) at `frontend/src/api/generated/api-client.ts:27891-27898`. TypeScript enums are nominal, so a direct assignment fails to compile even though the runtime values are identical strings — this is purely a type-system friction point, not a data-modeling one. Unifying the two enums is out of scope (`TimePeriod`/`TimePeriodFilter` is shared domain vocabulary used well beyond this endpoint) and a mapping table is unjustified ceremony for two enums whose only relationship is "coincidentally identical today." A single well-commented cast at the `queryFn`/`handleExport` boundary is the minimum-footprint fix; if the two enums ever diverge, TypeScript will not catch it, but a runtime string sent to a filter endpoint mismatching an enum member is the kind of drift `docs/architecture` doesn't currently protect against for *any* dual-enum situation in this codebase, so this is not a new class of risk introduced by this change — it already exists structurally between generated and app-level enums project-wide.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All changes are confined to:
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — delete the six hand-coded types (current lines ~27–111), import + re-export the five generated equivalents, rewrite `queryFn` to call `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)`, remove `formatDateForApi` if it becomes dead code.
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — rewrite `handleExport` (current lines ~175–247) to call the same generated method with `isExport: true`, replace the `any`-typed export column accessors with `ManufacturingStockItemDto`-typed ones. No import path changes here (see Decision 1).
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` — repoint the mock from `mockApiClient.http.fetch` to `apiClient.manufacturingStockAnalysis_GetStockAnalysis`.
- `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` — same mock update if it currently stubs `http.fetch` for the export path (verify at implementation time; file exists but wasn't inspected in depth here).

### Interfaces and Contracts
- The generated method's signature is **positional**, not an options object — implementers must map the existing `GetManufacturingStockAnalysisRequest`-shaped local object to the 15 positional arguments in the exact declared order (`timePeriod, customFromDate, customToDate, productFamily, criticalItemsOnly, majorItemsOnly, adequateItemsOnly, unconfiguredOnly, searchTerm, pageNumber, pageSize, sortBy, sortDescending, salesMultiplier, isExport`). A transposition here is a silent correctness bug (e.g. swapping `pageNumber`/`pageSize`), since both are `number | undefined` and TypeScript won't catch the swap.
- `customFromDate`/`customToDate` are typed `Date | null | undefined` on the generated method and the client serializes via `.toISOString()` internally — the existing `formatDateForApi` (`YYYY-MM-DD` truncation) becomes unnecessary; passing the `Date` object directly is correct and is a **behavior-preserving** simplification only if the backend's date binding tolerates full ISO-8601 (it does — `[FromQuery] DateTime?` model binding in ASP.NET Core accepts full ISO-8601 timestamps). Do not attempt to preserve the `YYYY-MM-DD`-only truncation by hand — that would be scope creep beyond what FR-2 asks for and isn't necessary for correctness.
- The current hook already special-cases `Q9M` (omits it from the query string when set, presumably because it's a backend default) — this conditional (`if (request.timePeriod && request.timePeriod !== TimePeriod.Q9M)`) must be preserved when mapping to the positional call: pass `undefined` for `timePeriod` when it's `Q9M`, not the literal value.
- Retain the exact re-export shape from `useManufacturingStockAnalysis.ts` so `ManufacturingStockAnalysis.tsx`'s two import statements (lines 18–29, line 38) and `ManufactureBatchPlanning.tsx`'s `calculateTimePeriodRange` import keep compiling unchanged.

### Data Flow
1. `ManufacturingStockAnalysis.tsx` builds a `filters` object (mirrors current behavior, unchanged).
2. `useManufacturingStockAnalysisQuery(filters)` — `queryFn` awaits `getAuthenticatedApiClient()`, converts `filters.timePeriod` (app `TimePeriodFilter`) to the generated `TimePeriod` via the aliased cast, and calls `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)` positionally with `isExport: false`.
3. The generated method builds the query string, performs the authenticated `fetch` (same transport, same auth headers as today — `NFR-2` unaffected), and either returns a parsed `GetManufacturingStockAnalysisResponse` or throws via `throwException` on non-2xx.
4. `handleExport` follows the identical path with `isExport: true`, then maps `result.items` (now `ManufacturingStockItemDto[]`, not `any[]`) into the existing `exportToXlsx` column definitions — column headers/order/values must stay byte-identical to today (FR-5 acceptance criterion), only the accessor typing changes from `(row: any) => row.code` to a typed accessor.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Positional-argument transposition when mapping the request object to the generated method's 15-parameter call (e.g. swapping two adjacent `boolean`/`number` params) | Medium | Write/adapt the unit test in `useManufacturingStockAnalysis.test.tsx` to assert the exact argument list passed to the mocked generated method, not just the resulting data shape |
| `ManufacturingStockSeverity` changes from numeric to string enum at runtime — any code path that serializes it into a non-enum-typed hole (cookie, URL param, localStorage, CSS module key) instead of comparing enum members would silently regress | Low | I found none in `ManufacturingStockAnalysis.tsx` (all 17 usages are `case`/`===` against the enum symbol) — still worth a manual smoke check after the change, per FR-2's manual-verification acceptance criterion |
| `handleExport`'s xlsx column set (FR-5) must be byte-identical to current output; a `ManufacturingStockItemDto` field name mismatch (e.g. backend renamed a field the hand-coded DTO no longer tracked) surfaces here first | Low | Diff the generated `ManufacturingStockItemDto` field list against the current 19 export columns before rewiring column accessors; the swap should be typed as a compile error if a field genuinely doesn't exist, which is the whole point of the change |
| `Q9M` special-casing (period omitted from query when set) is easy to lose in the rewrite since it's a small conditional buried in URL-building code being deleted wholesale | Low | Call out explicitly in code review; the acceptance criteria in FR-2 don't mention it directly, so it's easy to drop silently — flagged here as a Specification Amendment below |

## Specification Amendments

1. **FR-2 should explicitly require preserving the `Q9M` omission behavior.** The current `queryFn` conditionally omits `timePeriod` from the request when it equals `Q9M` (line ~139 area in the current file: `request.timePeriod !== TimePeriod.Q9M`). Neither FR-2 nor FR-5's acceptance criteria mention this, and it's the kind of small conditional that's easy to lose when deleting the surrounding URL-building code wholesale. Recommend adding an explicit acceptance criterion: "the `Q9M` sentinel continues to be passed as `undefined` to the generated method's `timePeriod` parameter, matching current omit-from-query-string behavior."
2. **FR-1's acceptance criteria should state the positional-argument mapping order explicitly** (or point implementers to the generated method's declared signature) to reduce the risk of a silent argument-transposition bug called out in the risk table above.

No other amendments — the spec's scope, sequencing, and out-of-scope boundaries (no backend changes, no enum consolidation, no export format changes) are all consistent with what the codebase actually contains.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — the generated client already contains everything required (verified directly in `frontend/src/api/generated/api-client.ts`), and the backend controller/handler/DTOs need no changes (FR-6). Implementation can start immediately.
