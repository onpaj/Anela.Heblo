# Architecture Review: Remove Unused `Severe` Member from `StockSeverity` Enum

## Skip Design: true

This change has no UI/UX surface. It removes a dead enum value, regenerates the TypeScript client, and verifies no existing component already branches on the removed value. No new screens, layouts, or visual decisions are required.

## Architectural Fit Assessment

The change fits cleanly into the existing Clean Architecture vertical slice for the Purchase module:

- The enum lives next to its sole producer (`StockSeverityCalculator`) and sole consumer (`GetPurchaseStockAnalysisHandler`) inside the `Features/Purchase/UseCases/GetPurchaseStockAnalysis/` slice — single ownership, no cross-feature coupling.
- The contract is transport-only: `StockSeverity` appears solely in `StockAnalysisItemDto.Severity` (transient response DTO) — it is not persisted, cached, or shared with another bounded context.
- The frontend mirrors this through the auto-generated `frontend/src/api/generated/api-client.ts`, consumed by helpers in `usePurchaseStockAnalysis.ts` (the only Purchase-side consumer).
- The regeneration path is already automated by NSwag (`prebuild` script on `npm run build` per `docs/development/api-client-generation.md`), so no toolchain extension is required.

Verified dead-value claim:
- Producer side: `StockSeverityCalculator.DetermineStockSeverity` returns only `NotConfigured | Critical | Low | Optimal | Overstocked` (file `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs`). No code path returns `Severe`.
- Consumer side: `ShouldIncludeItem`, `CalculateSummary`, frontend `getSeverityColorClass`/`getSeverityDisplayText`, frontend tests, and backend tests have zero references to `StockSeverity.Severe`.
- Grep over the worktree confirms the only live references to `StockSeverity.Severe` are the declaration itself and the regenerated TS enum entry. The `GiftPackageSeverity.Severe` references in `frontend/src/components/pages/GiftPackageManufacturing/*` belong to a separate, active enum and are correctly out of scope.

Integration points touched: exactly two files — the backend enum declaration and the auto-regenerated TypeScript client.

## Proposed Architecture

### Component Overview

```
[ StockSeverityCalculator ]
            │  (returns)
            ▼
[ StockSeverity enum (transport-only) ]   ← single edit point
            │  (serialized as string via JsonStringEnumConverter)
            ▼
[ GetPurchaseStockAnalysisResponse DTO ]
            │  (NSwag OpenAPI)
            ▼
[ frontend/src/api/generated/api-client.ts → StockSeverity ]   ← auto-regenerated
            │
            ▼
[ usePurchaseStockAnalysis.ts helpers + components ]
```

The architectural surface of this change is one node (`StockSeverity` enum); every other node either already ignores `Severe` (consumers) or refreshes automatically (the generated client).

### Key Design Decisions

#### Decision 1: Remove `Severe` in place rather than reserving the slot
**Options considered:**
1. Delete `Severe` outright.
2. Mark `Severe` `[Obsolete]` and keep it for one deprecation cycle.
3. Replace `Severe` with `[Obsolete] Reserved` placeholder to preserve ordinals.

**Chosen approach:** Option 1 — delete.

**Rationale:** `StockSeverity` is serialized by name (the generated TS client emits `"Severe"`, `"Critical"`, etc.), not by ordinal. This codebase has a single first-party consumer (its own frontend) and a single-deployment Azure Web App; there is no out-of-band consumer relying on the string `"Severe"` because the backend never returned it. Deprecation cycles exist to protect external contracts — there is no such contract here. Option 2 perpetuates the misleading contract the spec exists to fix.

#### Decision 2: Trust the existing NSwag prebuild step for client regeneration
**Options considered:**
1. Let `npm run build` regenerate via its existing `prebuild → generate-client` script.
2. Manually invoke `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` before committing the regenerated file.

**Chosen approach:** Both, in sequence — manual regeneration first to confirm the diff, then `npm run build` as the final acceptance gate.

**Rationale:** The manual call produces a deterministic diff in `api-client.ts` for the PR; the `npm run build` step proves the prebuild pipeline is the same one CI/dev will run. Per `docs/development/api-client-generation.md`, the generated file is committed to the repo, so the regenerated client must be in the commit.

#### Decision 3: Treat enum string serialization as a hard precondition
**Options considered:**
1. Assume JSON enums are serialized as strings.
2. Verify `JsonStringEnumConverter` (or equivalent) is registered before the change.

**Chosen approach:** Verify.

**Rationale:** Removing a middle enum member shifts the integer ordinals of every subsequent member (`Low` 2→1, `Optimal` 3→2, etc.). The generated TS client shows string values (`Critical = "Critical"`), which is strong evidence string serialization is active, but the implementation MUST confirm `JsonStringEnumConverter` registration in the API's JSON options. If integer serialization is ever active, removing `Severe` becomes a breaking ordinal change. This single check eliminates the only realistic correctness risk.

## Implementation Guidance

### Directory / Module Structure

No new files. The change touches:

- **Edit:** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` — delete line 99 (`Severe,`). Preserve declaration order of remaining members exactly: `Critical, Low, Optimal, Overstocked, NotConfigured`.
- **Regenerate (do not hand-edit):** `frontend/src/api/generated/api-client.ts` — the `Severe = "Severe",` line under the `StockSeverity` enum (around line 34161) disappears via NSwag.

No changes anywhere else. `usePurchaseStockAnalysis.ts`, page components, handlers, tests, and `GiftPackageSeverity` remain untouched.

### Interfaces and Contracts

Post-change `StockSeverity` shape (TypeScript view):

```typescript
export enum StockSeverity {
    Critical = "Critical",
    Low = "Low",
    Optimal = "Optimal",
    Overstocked = "Overstocked",
    NotConfigured = "NotConfigured",
}
```

The OpenAPI schema for `StockSeverity` drops `"Severe"` from its `enum` array. No request DTO, response DTO field, or endpoint path changes. The `StockStatusFilter` enum is unaffected.

Contract invariants developers must maintain:
- `StockStatusFilter` cases in `ShouldIncludeItem` (`Critical | Low | Optimal | Overstocked | NotConfigured`) remain in 1:1 correspondence with the post-change `StockSeverity` members. If a future change adds a new severity, both enums must be extended together.
- The frontend helper `default` branch (`text-gray-600 bg-gray-50` / `"Neznámý"`) becomes effectively unreachable but should remain as a defensive fallback for `undefined` severities arriving over the wire — do NOT delete it as part of this change.

### Data Flow

For each item returned by `GetPurchaseStockAnalysisHandler.Handle`:

1. `StockSeverityCalculator.DetermineStockSeverity(...)` returns one of `{NotConfigured, Critical, Low, Optimal, Overstocked}`.
2. Handler stores the value on `StockAnalysisItemDto.Severity` and aggregates counts in `StockAnalysisSummaryDto`.
3. ASP.NET Core serializes the enum as its string name via `JsonStringEnumConverter`.
4. NSwag's generated TS client parses it into the regenerated `StockSeverity` enum.
5. `usePurchaseStockAnalysis.ts` helpers map the string to a Tailwind class and Czech display label.

No path produces or consumes `"Severe"` before or after the change, so the runtime data flow is byte-identical for every actual response.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Enum is serialized as integer (not string), making the ordinal shift a silent breaking change for any persisted/cached value. | Medium | Before merging, verify `JsonStringEnumConverter` is registered in the API's JSON options (look in `Program.cs` / `Startup.cs` for `AddJsonOptions` configuring `JsonSerializerOptions.Converters`). If found, risk is fully mitigated. If absent, stop and treat as a separate prerequisite. |
| Stale generated client committed (developer forgets to regenerate). | Low | Run `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` followed by `npm run build` in `frontend/`. CI / reviewer must see the `api-client.ts` diff in the PR — its absence indicates a missed step. |
| Future contributor reintroduces `Severe` by copying from `GiftPackageSeverity` (which legitimately keeps the value). | Low | Naming alone disambiguates; no code mitigation needed. Out-of-scope cleanup note: `docs/features/gift-package-manufacture.md:476` references `StockSeverity.Severe` where it should say `GiftPackageSeverity.Severe` — flag in PR description but do not fix in this PR (surgical-change rule). |
| Third-party tooling (Postman collections, dashboards) caches the old enum shape and surfaces `"Severe"` until refreshed. | Low | Out of scope — solo-developer workspace with no external API consumers. Note in PR description for awareness. |

## Specification Amendments

The spec is sound. Two minor clarifications that should be folded in:

1. **FR-1 acceptance criteria** lists the remaining members as `NotConfigured, Critical, Low, Optimal, Overstocked` (alphabetical-ish). The actual on-disk declaration order is `Critical, Low, Optimal, Overstocked, NotConfigured` after `Severe` is removed. The data-model section already says "Final member order matches the existing declaration with `Severe` removed in place," which is correct — FR-1's bullet wording should not be interpreted as requiring a reorder. **Amendment:** restate FR-1 acceptance criterion as "Members and order: `Critical, Low, Optimal, Overstocked, NotConfigured` (existing declaration minus `Severe` in place)."

2. **Add a verification step** to FR-1 or NFR-2: confirm that the API uses string-based enum serialization (`JsonStringEnumConverter`). This is the single load-bearing assumption behind "no behavior change" and should be explicit in the spec, not implicit. **Amendment:** add NFR-5 — "Enum serialization mode: implementer must verify `JsonStringEnumConverter` is registered in the API's JSON options. Acceptance: a grep for `JsonStringEnumConverter` in the API host project returns at least one registration site, OR the implementer cites the configuration source."

3. **Out of Scope** should explicitly call out the documentation reference at `docs/features/gift-package-manufacture.md:476` so a reader doesn't perceive that as a missed update. Recommended addition: "Stale doc reference to `StockSeverity.Severe` in `gift-package-manufacture.md` (should read `GiftPackageSeverity.Severe`); will be addressed in a separate docs fix."

No other amendments needed. The spec status `COMPLETE` is appropriate.

## Prerequisites

1. **Verify enum string serialization is active.** Grep the API host project (`backend/src/Anela.Heblo.API/`) for `JsonStringEnumConverter` and confirm it is added to `JsonSerializerOptions.Converters` in the JSON pipeline (commonly via `services.AddControllers().AddJsonOptions(...)`). This is the only prerequisite that affects correctness; if missing, the task expands to "either register the converter OR explicitly assign numeric values to all `StockSeverity` members before removing `Severe`."
2. **Confirm regeneration toolchain is functional locally.** Run `dotnet tool restore` once at the repo root; required by NSwag per `docs/development/api-client-generation.md`.
3. **No migrations, no config, no infrastructure changes.** `StockSeverity` is transport-only; nothing in the database, Redis, or Azure config references it.