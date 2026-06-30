# Specification: test(e2e): Target "Anela" Sidebar Section Instead of "Personální"

## Summary

Three E2E tests in `sidebar-navigation.spec.ts` fail nightly because they look for a sidebar section named "Personální" that does not exist. The sidebar component (`Sidebar.tsx`) has always used the label **"Anela"** for the section that contains the "Struktura" org-chart link. The fix is a targeted update to the test file: replace every reference to "Personální" with "Anela" and correct the section-ordering assertion to reflect the real sidebar order.

## Background

Nightly E2E run 28147951139 (2026-06-25) reported three failures in `frontend/test/e2e/core/sidebar-navigation.spec.ts` at lines 13, 30, and 55. All three tests locate the sidebar section via `getByRole('button', { name: /Personální/i })`. In `frontend/src/components/Layout/Sidebar.tsx` the section is defined as `{ id: "anela", name: "Anela", … }` and its ARIA-accessible button text is therefore "Anela". No "Personální" section exists anywhere in the sidebar. The decision captured in the brief is to update the tests (not rename the UI label), because "Anela" is the intentional name.

Additionally, the ordering test asserts that the "Personální" section sits **between** "Sklad" and "Automatizace". In the real sidebar order (reading `allSections` top-to-bottom in `Sidebar.tsx`) the sections are:

1. Dashboard
2. **Anela**
3. Finance
4. Zákaznické
5. Produkty
6. Marketing
7. Nákup
8. Výroba
9. **Sklad** (id `logistika`)
10. **Administrace** (id `automatizace`, display name "Administrace")

"Anela" therefore comes **before** "Sklad", and "Sklad" comes before "Administrace". The test must be rewritten to match this real order. Note also that the section previously called "Automatizace" in the test is now labelled "Administrace" in the component.

## Functional Requirements

### FR-1: Replace "Personální" selector with "Anela" in all three tests

Every `getByRole('button', { name: /Personální/i })` call in `sidebar-navigation.spec.ts` must be changed to `getByRole('button', { name: /Anela/i })`. This affects the locators at (current) lines 15, 32, and 57.

**Acceptance criteria:**
- No occurrence of the string `Personální` remains in `sidebar-navigation.spec.ts`.
- The locator pattern `/Anela/i` is used wherever the section header button is targeted.

### FR-2: Correct the section-ordering assertion

The third test ("should display Personální section between Sklad and Automatizace") must be updated to:

1. Rename the test description to reflect the real section name and position, e.g. "should display Anela section before Sklad and Administrace".
2. Change the filter regex from `/^(Sklad|Personální|Automatizace)$/` to `/^(Anela|Sklad|Administrace)$/`.
3. Rename the local variable `personalniIndex` to `anelaIndex` (or equivalent) to match the new section name.
4. Change the variable referencing "Automatizace" text content to match "Administrace".
5. Rewrite the positional assertions so they verify:
   - `anelaIndex` is found (≥ 0).
   - `skladIndex` is greater than `anelaIndex` (Sklad comes after Anela).
   - `administraceIndex` is greater than `skladIndex` (Administrace comes after Sklad).

**Acceptance criteria:**
- The test description no longer references "Personální" or "Automatizace".
- The filter regex targets "Anela", "Sklad", and "Administrace".
- Positional assertions express the order Anela → Sklad → Administrace.
- The test passes against the live sidebar.

### FR-3: "Open Struktura in new window" flow continues to work

The second test ("should open Struktura in new window") must still:
- Click the "Anela" section button to expand it.
- Click the "Struktura" sub-item button.
- Assert the new page URL is `https://orgchart.anela.cz/`.

No changes to the URL assertion or new-page-event logic are required; only the section locator label changes.

**Acceptance criteria:**
- The test expands the "Anela" section, clicks "Struktura", and successfully intercepts the new page at `https://orgchart.anela.cz/`.

## Non-Functional Requirements

### NFR-1: Test isolation

No changes are made to test helpers, fixtures, or any file outside `sidebar-navigation.spec.ts`. The fix is self-contained.

### NFR-2: No production code changes

`Sidebar.tsx` and all other production files remain untouched. This specification explicitly rules out renaming the sidebar section label.

## Data Model

Not applicable. This change touches only E2E test code.

## API / Interface Design

Not applicable. No API endpoints are added or changed.

## Dependencies

- `frontend/test/e2e/core/sidebar-navigation.spec.ts` — the single file being modified.
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` (via `navigateToApp`) — unchanged; must continue to work.
- The live staging sidebar must render the "Anela" section with items "Porady" and "Struktura" for the tests to pass.

## Out of Scope

- Renaming the "Anela" sidebar section to "Personální" in production code.
- Adding new sidebar-navigation tests.
- Modifying any other test files or test helpers.
- Changes to `Sidebar.tsx` or any backend code.

## Open Questions

None.

## Status: COMPLETE
