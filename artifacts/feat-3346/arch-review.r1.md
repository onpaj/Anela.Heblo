# Architecture Review: test(e2e): Target "Anela" Sidebar Section Instead of "Personální"

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-correction task with no production code changes. The failing tests contain a stale label ("Personální") that never matched the actual sidebar component, which has always used "Anela" (`id: "anela", name: "Anela"`) since its definition in `frontend/src/components/Layout/Sidebar.tsx`. The fix is isolated to one test file and touches no shared helpers, fixtures, or production code. It fits entirely within the existing E2E testing layer.

The ordering assertion is also incorrect: the test asserts "Personální" sits between "Sklad" and "Automatizace", but the real sidebar renders the "Anela" section at position 2 (immediately after "Dashboard"), well before "Sklad" (position 9, `id: logistika`) and "Administrace" (position 10, `id: automatizace`, display name "Administrace" — not "Automatizace"). The ordering test must be rewritten to assert the real relative order: Anela → Sklad → Administrace.

## Proposed Architecture

### Component Overview

Only one file changes:

| File | Change |
|------|--------|
| `frontend/test/e2e/core/sidebar-navigation.spec.ts` | Replace "Personální" with "Anela"; rewrite ordering assertion |

No other files are touched. Sidebar.tsx, helpers, fixtures, and all other test files remain untouched.

### Key Design Decisions

1. **Test the label, not the id.** The Playwright selectors use `getByRole('button', { name: /…/i })`, which matches the accessible button text rendered by the component (`section.name`). The correct value is `"Anela"`, matching `Sidebar.tsx` line 95.

2. **Ordering assertion strategy.** The current test uses a single combined filter regex to locate three buttons in one locator, then checks their positional indices against each other. This approach is valid and should be kept — only the filter strings, variable names, and assertion logic need updating. The new filter must match `Anela`, `Sklad`, and `Administrace` (the actual display name of the `automatizace` section). Matching "Automatizace" will return zero results because no button with that text exists.

3. **Ordering direction.** The spec states the assertion should be Anela → Sklad → Administrace. Confirmed against `Sidebar.tsx`: Anela is at index 1, Sklad (`logistika`) at index 8, Administrace (`automatizace`) at index 9 in `allSections`. Because sections are filtered by permissions at runtime, the indices in the DOM may differ, but the relative order is stable — Anela always precedes Sklad, and Sklad always precedes Administrace.

4. **No title change needed for the third test.** The test description "should display Personální section between Sklad and Automatizace" should be updated to describe the real behaviour to prevent future confusion.

## Implementation Guidance

### Directory / Module Structure

```
frontend/test/e2e/core/
  sidebar-navigation.spec.ts   ← only file to edit
```

No new files. No moved files.

### Interfaces and Contracts

There are no interface or contract changes. The selectors rely on ARIA role `button` with accessible name derived from `section.name` in Sidebar.tsx. The contract between the test and the component is:

- Section header buttons render with text equal to `section.name`.
- The "Anela" section header button is labelled "Anela".
- The "Sklad" section header button is labelled "Sklad".
- The "Administrace" section header button is labelled "Administrace".

### Data Flow

No data flow changes. The tests authenticate via `navigateToApp(page)` (the correct helper per project rules), wait for `domcontentloaded`, then query the DOM. This is unchanged.

### Specific changes required in `sidebar-navigation.spec.ts`

**Test 1 — "should display Personální section with Struktura link" (lines 13–28)**

- Line 13: rename test to `'should display Anela section with Struktura link'`
- Line 15: change selector from `/Personální/i` to `/Anela/i`
- Line 18: rename variable `personalniSection` → `anelaSection` (for clarity; variable name has no runtime effect)

**Test 2 — "should open Struktura in new window" (lines 30–53)**

- Line 32: change selector from `/Personální/i` to `/Anela/i`
- Variable rename `personalniSection` → `anelaSection` (optional but recommended)

**Test 3 — "should display Personální section between Sklad and Automatizace" (lines 55–71)**

- Line 55: rename test to `'should display Anela section before Sklad and Administrace'`
- Line 57: update filter regex from `/^(Sklad|Personální|Automatizace)$/` to `/^(Anela|Sklad|Administrace)$/`
- Lines 63–65: rename variables `skladIndex`, `personalniIndex`, `automatizaceIndex` → `anelaIndex`, `skladIndex`, `administraceIndex` and update the `findIndex` predicates accordingly
- Lines 68–70: update assertions to:
  - `expect(anelaIndex).toBeGreaterThanOrEqual(0)`
  - `expect(skladIndex).toBeGreaterThan(anelaIndex)`
  - `expect(administraceIndex).toBeGreaterThan(skladIndex)`

## Risks and Mitigations

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Permission filter hides "Anela" section for the test user | Low | `navigateToApp()` authenticates as a configured test user. Verify that user has at least one permission granting access to a route under the `anela` section (`/automation/meeting-tasks` or `#org-chart`). If not, the section will be hidden by the `canSeeItem` filter in Sidebar.tsx and the test will still fail — but that is a fixture/permission issue, not a code issue. |
| "Administrace" regex matches a different button | Negligible | The regex `/^Administrace$/` (anchored) will only match the exact text. No other sidebar button uses this label. |
| Test 3 still fails because "Anela" appears at index 0 in the DOM | Negligible | `expect(anelaIndex).toBeGreaterThanOrEqual(0)` only requires it to be found. The relative ordering assertions (`skladIndex > anelaIndex`, `administraceIndex > skladIndex`) do not require any specific absolute index. |

## Specification Amendments

None required. The spec is fully accurate. One clarification for the implementer: the spec says "Automatizace" in FR-2 but the sidebar's `automatizace` section renders with the display name **"Administrace"**. The regex and variable name in the test must use "Administrace", not "Automatizace". The spec's Background section already captures this correctly in the sidebar order list (item 10: "Administrace (id `automatizace`, display name 'Administrace')").

## Prerequisites

- No migrations, no dependency installs, no environment changes.
- Confirm the E2E test user (used by `navigateToApp`) has permission to see the "Anela" section items. Check `frontend/test/e2e/fixtures/test-data.ts` and the user's RBAC assignments for `#org-chart` or `/automation/meeting-tasks`.
- After editing, run `./scripts/run-playwright-tests.sh` against staging targeting `sidebar-navigation.spec.ts` to confirm all three tests pass before merging.
