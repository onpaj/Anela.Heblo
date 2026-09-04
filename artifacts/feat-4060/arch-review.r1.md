# Architecture Review: Bank ImportTab should use `errorType` instead of hardcoded "OK" sentinel

## Skip Design: true

This is a pure internal refactor of an existing status-derivation function. No new/changed UI components, screens, layouts, or visual design decisions — FR-3 explicitly requires the rendered output to be pixel-identical to today's behavior. There is nothing for a designer to do here.

## Architectural Fit Assessment

This aligns cleanly with the project's Contracts/DTO conventions (`docs/architecture/development_guidelines.md`): the Bank module's `contracts/BankStatementImportDto.cs` already owns and exposes the semantic distinction ("is this an error") via `ErrorType`, and NSwag already propagates it into `frontend/src/api/generated/api-client.ts` unmodified. The only gap is that the sole consumer, `ImportTab.tsx`, never adopted the field it was built for. There is no cross-module boundary concern, no new contract, and no backend change — this is entirely inside the frontend's Bank-facing component. Integration point is a single function (`getImportStatusIcon`) and its single call site (line 494), both confirmed by repo search during this review.

## Proposed Architecture

### Component Overview

```
BankStatementImportDto.cs (backend, contracts/)
        │  ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null
        │  (unchanged — already correct, already generated)
        ▼
api-client.ts (generated, NSwag)
        │  errorType?: string | undefined   (unchanged — already generated)
        ▼
ImportTab.tsx
        │  statement.errorType  ──┐
        │  statement.importResult ┤──▶ getImportStatusIcon(importResult, errorType)
        └──────────────────────────┘        │
                                             ▼
                                   success/error badge (unchanged visual output)
```

No new components. No new data flow paths — `errorType` already flows over the wire today; it is simply unread. This review changes only the last hop: which field the render function branches on.

### Key Design Decisions

#### Decision 1: Branch on `errorType` nullness, keep `importResult` as the display fallback only where behavior requires it
**Options considered:**
1. Drop `importResult` from the signature entirely, use `errorType` for both branching and error text.
2. Keep `importResult` as a parameter used only for the (now dead) `"OK"` comparison, add `errorType` alongside for branching.
3. Branch on `errorType`, but keep `importResult` in the signature purely because the error message today falls back to `importResult || "Chyba"` when `importResult` is falsy — preserve that exact fallback chain using `errorType` in its place, since `errorType` semantically *is* the error string.

**Chosen approach:** Option 1, with adaptation — branch on `!errorType`; when truthy, render `errorType` as the message (replacing today's `importResult || "Chyba"` with `errorType || "Chyba"`, since `errorType` is `ImportResult` itself whenever it's non-null, per the DTO's own definition: `ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null`). `importResult` becomes unnecessary as a parameter of `getImportStatusIcon` once branching and message both come from `errorType`.

**Rationale:** The DTO's `ErrorType` getter guarantees `errorType === importResult` whenever `errorType` is non-null (it's a direct passthrough of `ImportResult` in the error case). So `errorType || "Chyba"` is observably identical to today's `importResult || "Chyba"` for every value the backend can actually produce, satisfying FR-3 (pixel-identical output) while fully eliminating the `"OK"` literal and the now-redundant `importResult` parameter. This is simpler than Options 2/3, which would keep an unused or redundant parameter around for no behavioral reason — contrary to the project's "surgical changes" / no-dead-parameters expectation.

**Trade-off to flag explicitly:** this reasoning depends on `ErrorType`'s current implementation (`ImportResult != Success ? ImportResult : null`) staying a direct passthrough. If a future backend change ever made `ErrorType` a distinct human-readable message different from `ImportResult`, `getImportStatusIcon` would still be correct (arguably *more* correct — `errorType` is the field designed to carry the user-facing error). This is the exact fragility the issue is trying to eliminate, so this is desired, not a regression risk.

## Implementation Guidance

### Directory / Module Structure
No new files. Single file touched: `frontend/src/components/customer/tabs/ImportTab.tsx`.
- Lines 249–264 (`getImportStatusIcon` definition)
- Line 494 (call site)

No backend files touched. No changes to `frontend/src/api/generated/api-client.ts` (already correct; regenerating it is unnecessary and must not be done as part of this task per spec's Dependencies section — regeneration is driven by backend build only, and no backend DTO changes are in scope).

### Interfaces and Contracts

```tsx
// getImportStatusIcon: branch and message both driven by errorType.
// errorType is null/undefined on success (per BankStatementImportDto.ErrorType),
// and equals importResult on error — so importResult is no longer needed here.
const getImportStatusIcon = (errorType: string | null | undefined) => {
  if (!errorType) {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300">
        <CheckCircle className="h-3 w-3 mr-1" />
        Úspěch
      </span>
    );
  }
  return (
    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300">
      <AlertCircle className="h-3 w-3 mr-1" />
      {errorType || "Chyba"}
    </span>
  );
};

// call site (was: getImportStatusIcon(statement.importResult))
{getImportStatusIcon(statement.errorType)}
```

This is a valid, spec-conformant refinement of `spec.r1.md`'s FR-1/FR-2 (which allowed retaining `importResult` as an open implementation choice) — recorded formally in Specification Amendments below.

`statement.errorType`'s generated TS type is `string | undefined` (confirmed in `api-client.ts`); accepting `string | null | undefined` in the function signature is defensive and matches the issue's suggested signature without narrowing incorrectly if the generator's nullability ever shifts.

### Data Flow
Unchanged end-to-end wire flow (`ImportStatus` → `ErrorType` → NSwag → `api-client.ts` → React Query hook → `statement.errorType`). Only the terminal consumption point changes, as shown in Component Overview.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Visual/behavioral regression if `errorType` and `importResult` ever diverge for existing data | Low | `ErrorType`'s current backend implementation guarantees they match whenever `errorType` is non-null; add a unit test (see below) asserting the badge renders correctly for both `errorType` states to catch any future divergence at the frontend boundary too |
| No existing test coverage for `getImportStatusIcon` / `ImportTab` status rendering (`ImportTab.test.tsx` has zero matches for this logic) | Low | Not a regression risk introduced by this change, but the planner should size in a small test addition (2 cases: success badge, error badge with message) so the refactor is verifiable and future changes to this function are guarded |
| Missing a second call site | Very Low | Repo-wide grep during this review confirms line 494 is the only call site |

## Specification Amendments

- **FR-1 amendment:** `getImportStatusIcon`'s final signature is `(errorType: string | null | undefined)` — the `importResult` parameter is dropped rather than retained, per Decision 1 above. The spec's FR-1/FR-2 left this as an implementation choice ("left to the implementer per Open Questions"); this review resolves that choice concretely so planner/developer don't re-litigate it.
- **Added acceptance criterion (extends FR-3):** add or extend a frontend unit/component test asserting `getImportStatusIcon`/`ImportTab` renders the green "Úspěch" badge when `errorType` is falsy and the red badge with the error text when `errorType` is truthy. No prior test exists for this function — this is new coverage for previously-untested logic touched by this task, not a change to an existing test's expectations.

## Prerequisites
None. No migrations, no config, no infrastructure changes, no backend changes, no client regeneration — `errorType` is already present in the generated client today.
