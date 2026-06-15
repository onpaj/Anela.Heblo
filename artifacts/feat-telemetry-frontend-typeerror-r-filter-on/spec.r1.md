# Specification: Fix frontend TypeError `r.filter is not a function` at symbol `Yq`

## Summary
A `TypeError: r.filter is not a function` surfaced once in production telemetry on 2026-06-12, fired on the SPA root path `/` from a minified symbol `Yq`. The error indicates `.filter()` is invoked on a value that is `undefined` or `null` — almost certainly a hook/selector consuming API data before it resolves or without a `[]` fallback. This spec covers locating the call site, applying a null-safe guard, and verifying the fix.

## Background
Telemetry captured a browser exception during the P7D window 2026-06-05 – 2026-06-12 (Chrome 148.0). Occurrence count is low (1), but timing aligns with three UI changes merged the same day:

- PR #2962 — "open dashboard to all users with per-tile permission enforcement"
- PR #2943 — "Move Journal Search Presentation Logic to Frontend"
- PR #2948 — "Remove Manual refetch Calls from JournalList"

All three are plausible regression sources: each either changes data flow into a list/grid component or alters when/how data populates state. Because the error fires on `/` (the SPA entry path), the offending call is in something rendered on the dashboard or wired into its initial loading sequence. Even a single occurrence warrants a fix — minified runtime errors generally indicate a latent issue that will recur for users in different network/auth/timing conditions.

## Functional Requirements

### FR-1: Locate the `Yq` call site
Resolve minified symbol `Yq` back to its source-level identifier and file using the build's source maps (or fall back to bundle grep + source inspection).

**Acceptance criteria:**
- The exact source file and line of the `.filter()` call producing the error is identified.
- The variable being filtered is traced to its origin (hook return, prop, selector, etc.).
- The conditions under which it is `undefined`/`null` are documented (e.g. "before query resolves", "when API returns 204", "when user lacks permission").

### FR-2: Apply a null-safe fix at the call site
Add a defensive default so `.filter()` is never invoked on a non-array value.

**Acceptance criteria:**
- The fix uses one of: a default value (`data ?? []`), an explicit type/shape guard, or destructuring with a default (`const { items = [] } = data ?? {}`).
- No silent data-swallowing: if `undefined` is unexpected (vs. a normal loading state), the loading/empty path must be handled explicitly in the component rather than masked.
- The fix does not regress the rendered output when data is present.

### FR-3: Audit sibling call sites in the three suspect PRs
The same anti-pattern may exist in adjacent code touched by PRs #2962, #2943, #2948.

**Acceptance criteria:**
- Every `.filter()`, `.map()`, `.reduce()` call introduced or modified in those PRs is reviewed for a missing default.
- Any additional unsafe call sites get the same guard treatment as FR-2.
- The audit findings (sites reviewed, sites fixed, sites left as-is with rationale) are recorded in the PR description.

### FR-4: Regression test for the failing code path
Add a unit/component test that exercises the call site with `undefined`/`null` input.

**Acceptance criteria:**
- A Jest/RTL test renders the affected component with the upstream data source returning `undefined` (and `null`, and `[]`).
- The test fails against the pre-fix code and passes against the fixed code.
- The test lives next to the component being fixed, following the existing test file conventions in the module.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change to bundle size or first-paint timing. The fix is a single defensive default per call site.

### NFR-2: Security
None — this is a defensive bug fix with no auth, input, or data-exposure surface.

### NFR-3: Observability
The fix should not silently hide a real upstream bug. If `undefined` is reaching the call site because an API contract is being violated (e.g. server returns `null` where the OpenAPI schema declares an array), file a follow-up issue rather than only patching the consumer.

### NFR-4: Compatibility
The fix targets the same browsers currently supported. No change to TypeScript target, polyfills, or build config.

## Data Model
No persistent data model changes. The defect is in client-side consumption of one of the following (to be confirmed by FR-1):

- Dashboard tile list / permission-filtered tile set (PR #2962)
- Journal search result set or filter chip collection (PR #2943)
- JournalList items array (PR #2948)

## API / Interface Design
No new endpoints, no contract changes. If FR-1 reveals that the backend response shape differs from what the TypeScript client expects (e.g. nullable array where the schema says non-null), the follow-up will be a backend or OpenAPI spec correction handled separately.

## Dependencies
- Source maps for the production bundle that emitted the telemetry (build artifact for the deploy that included PRs #2962/#2943/#2948).
- Read access to the three referenced PRs to scope FR-3.
- Existing test infrastructure (`npm test`, Jest, React Testing Library) — no new tooling.

## Out of Scope
- Refactoring the dashboard tile permission system, journal search architecture, or `JournalList` data fetching.
- Adding global runtime guards (e.g. a wrapper that defaults every array). Fix is local to the call site(s).
- Telemetry alerting tuning for single-occurrence errors.
- E2E test for this scenario — unit/component coverage per FR-4 is sufficient given the deterministic nature of the bug.
- Backporting to older release branches (project ships a rolling main).

## Open Questions
None.

## Status: COMPLETE