# Receive Materials with Lots — E2E Test (PO + Freeform) on iPhone-Max

**Date:** 2026-07-15
**Status:** Approved design → planning
**Scope:** B — write the automated Playwright e2e spec covering both flows at iPhone-Max viewport, **and** fix the real UX/data bugs found so the process is genuinely clean, with the passing spec as durable proof.

## Problem

The "receive materials with lots" workflow is the terminal **Identifikace šarže** wizard: a warehouse worker scans pre-printed container labels (`Mxxxxxxxx`) and assigns each to a material + supplier lot code, optionally against a purchase-order line. Two entry paths exist:

- **PO flow** — `/terminal/lot-identification/po/...`: pick an in-transit PO → pick a line/material → enter lot → scan containers → optionally mark PO `Received`.
- **Freeform flow** — `/terminal/lot-identification/freeform/...`: enter a material code not tied to any PO → enter lot → scan containers.

Neither path is covered by a running e2e test. An existing spec (`frontend/test/e2e/terminal/lot-identification.spec.ts`) exercises only the freeform path, **invents random container codes**, and lives in a `terminal/` folder that is **not a registered Playwright project** — so it has never run against staging and would fail if it did (see Discovery).

## Discovery (verified against code)

- **Assignment requires a pre-existing Unassigned container.** `CreateMaterialContainersHandler` calls `GetByCodeAsync(code)`; `null` → `UnknownMaterialContainerCode`, non-`Unassigned` → `MaterialContainerCodeExists`. Invented codes cannot be assigned. The existing freeform spec is therefore dead/broken.
- **Containers become Unassigned only via `print-labels`.** `PrintMaterialContainerLabelsHandler` generates codes (`IMaterialContainerCodeGenerator`), persists them as `Unassigned`, **then physically prints** via `ILabelPrintingService.PrintZplAsync`.
- **Printing is broken on staging today.** `CupsLabelPrintingService.PrintZplAsync` throws `InvalidOperationException` when `CupsOptions.LabelPrinterName` is unset, and otherwise targets a CUPS printer that does not exist on staging. So `print-labels` cannot be called on staging as-is.
- **PO flow's finish step mutates shared data.** `FinishPoStep` can flip a real PO `InTransit → Received`, which would break fixture reuse on the next run.
- **`terminal/` is not a Playwright project.** `playwright.config.ts` defines projects `catalog, issued-invoices, stock-operations, transport, manufacturing, core, marketing, finance, baleni` — `terminal/` and `leaflet-generator/` are on disk but unrun.

## Goals

1. A green Playwright spec covering **both** the PO-linked and non-PO (freeform) receive flows at an iPhone-Max viewport, run against staging.
2. The spec is **repeatable and non-destructive**: fresh unique codes per run, fixture PO left `InTransit`, and created containers cleaned up afterward.
3. Real UX/data defects surfaced during live driving are fixed in the terminal feature code.

## Non-goals

- Changing the intended assignment semantics (labels are pre-printed, then scanned to assign). The `UnknownMaterialContainerCode` guard stays.
- Lot expiration-date capture (managed via the separate Lot entity/UI, not this terminal loop).
- Registering `leaflet-generator/` as a project.

## Design

### 1. Backend — printing behind a feature flag

- **New flag** `is-label-printing-enabled`:
  - `FeatureFlagKeys.LabelPrintingEnabled = "is-label-printing-enabled"`.
  - `FeatureFlagRegistry` entry, **`DefaultValue: true`** — printing is existing behavior; the flag exists to *suppress* it, so production/fail-safe keeps printing.
  - `appsettings.Staging.json` → `"is-label-printing-enabled": false`. Admin UI (DB override) can flip it on staging to test a real printer.
  - Mirror the key in `frontend/src/features/feature-flags/featureFlags.ts` for consistency (no FE consumer required now).
- **Gate at the shared seam** with a `FeatureGatedLabelPrintingService` decorator implementing `ILabelPrintingService`:
  - Flag on → delegate to the real `CupsLabelPrintingService`.
  - Flag off → log and no-op (skip physical print).
  - Registered so the decorator wraps the CUPS implementation. No edits to the four print handlers.
- **Effect:** on staging, `print-labels` still generates codes and persists Unassigned containers, skipping only the physical print — the test's provisioning path via the real production endpoint.

**Flag-direction rationale:** defaulting `true` preserves current production behavior even if config is missing (fail-open to printing). Only `appsettings.Staging.json` turns it off. This matches the "flag suppresses existing behavior" intent, distinct from dry-run flags that gate *new* risky behavior off-by-default.

### 2. Test provisioning helper

A helper (in `frontend/test/e2e/helpers/`) that, using the authenticated E2E session, POSTs to `` `${apiClient.baseUrl}/api/material-containers/print-labels` `` with `{ count, mediaChangeConfirmed: true }` and returns the freshly minted Unassigned codes from the response. Absolute URL per the api-client rule. `mediaChangeConfirmed: true` avoids the `RequiresMediaChangeConfirmation` short-circuit.

### 3. The spec — `frontend/test/e2e/terminal/lot-identification.spec.ts`

`test.use({ viewport: devices['iPhone 14 Pro Max'].viewport })` (430×932 — "Max"). Auth via `navigateToApp(page)`.

**Scenario 1 — PO flow (materials from a predefined purchase order):**
1. Seed 2 Unassigned codes via the helper.
2. Terminal → *Příjem podle objednávky* → pick in-transit fixture PO (`inTransitNoInvoice`, PO20251113-1251).
3. Pick a PO line/material → enter a unique lot code.
4. Scan both seeded codes; assert per-scan "Uloženo" and running "Naskenováno: N".
5. Finish step → **"leave InTransit"** (non-destructive; PO status unchanged).
6. **Data assert:** `GET /api/material-containers?code=…` returns each container `Assigned` with the expected `PurchaseOrderLineId`.

**Scenario 2 — Freeform flow (material not on any purchase order):**
1. Seed 2 Unassigned codes.
2. Terminal → *Volný příjem* → material `AKL001` (Bisabolol, canonical stable fixture) → enter a unique lot.
3. Scan both seeded codes; assert "Uloženo" and running count.
4. Finish/Hotovo.
5. **Data assert:** containers `Assigned`, `PurchaseOrderLineId` null.

Screenshots captured at each key step for UX review. The previously broken freeform tests are rewritten to use seeded codes: the last-used-lot and duplicate-conflict cases both need a real seeded code (their first scan must *succeed* before the assertion — the duplicate case then re-scans the same code to get "je již přiřazen"). Only the invalid-format case keeps an invented code, since it asserts a purely client-side rejection that never reaches the backend.

**Cleanup (repeatability):** an `afterEach`/`afterAll` discards every container created during the test via `POST /api/material-containers/{id}/discard` (ids captured from seed + assign responses). Fresh unique codes per run avoid `MaterialContainerCodeExists`; leaving the PO `InTransit` keeps the fixture reusable.

### 4. Make the test run

- Add a `terminal` project to `playwright.config.ts` (Desktop Chrome device; the spec overrides viewport per-file).
- Map `terminal` in `scripts/run-playwright-tests.sh` module switch.
- Add `terminal` to the CI matrix and note it in `docs/testing/e2e-module-guide.md` (+ CLAUDE.md module list if applicable).

### 5. UX iteration loop

Run the spec against staging at iPhone-Max viewport, review the step screenshots, and fix terminal component UX issues found (scan feedback visibility, tap-target reachability, small-screen layout, error-message clarity). Re-run until both **data is correct** and **UX is clean**. Specific fixes are discovered during driving and will be enumerated in the implementation plan as they surface.

## Testing strategy

- **E2E (primary):** the two scenarios above, green against staging via `./scripts/run-playwright-tests.sh terminal`.
- **Backend unit:** a test for `FeatureGatedLabelPrintingService` — delegates when flag on, no-ops when flag off. Existing `PrintMaterialContainerLabelsHandler` tests remain green (decorator is transparent).
- **Contract:** any new `*Response` inherits `BaseResponse` (no new response types expected here).

## Implementation findings (2026-07-15)

Driving the flow locally at iPhone 15 Pro Max (frontend with mock auth against a local backend, printing flag off) surfaced two blockers outside the terminal feature itself:

1. **AuthGuard `returnUrl` bounce (fixed).** `AuthGuard` stored `auth.returnUrl` = the deep-linked path but only cleared it when it *differed* from the current path. A direct load of `/terminal/lot-identification` therefore left a stale `returnUrl` that bounced the first in-app navigation (tile tap → `/po` → snapped back home). This breaks the flow under E2E/mock auth (real Entra users are unaffected because the redirect cycle consumes `returnUrl`). Fixed to **consume-once**: remove `returnUrl` whenever authenticated, navigate only when it differs. Covered by an updated `AuthGuard.test.tsx` case.

2. **Changelog toaster overlaps the flow on mobile (mitigated in test; product fix deferred).** The global "Co je nové" toaster (`fixed top-4 right-4`, 400 px wide) blankets the first home tile on a 430 px phone and intercepts the tap. The spec dismisses it defensively in `beforeEach`. A product fix (suppress on `/terminal` or make it responsive) is recommended but left out of this change as it touches a global, separately-tested component.

The terminal feature's own screens (home tiles, PO pick, line pick, lot entry, scan loop, finish, freeform, error states) render cleanly at iPhone-Max with good tap targets and feedback — no changes needed there. Data correctness was verified end to end via the API: `print-labels` seeds Unassigned with the printing flag off (no printer 500), freeform assign yields `Assigned` with no PO line, PO assign yields `Assigned` linked to the PO line, and the PO stays `InTransit`.

## Flow revisions (2026-07-15, from review)

Two feature-flow corrections were made after driving the flow:

1. **PO flow is now multi-material.** After scanning a material's containers, the operator returns to the order's material list via a **"Zpět na objednávku"** button (was: straight to the mark-received step), picks the next material, and repeats. The order is completed explicitly with a **"Dokončit příjem objednávky"** button on the material list, which opens the existing mark-received / leave-in-transit step. `ContainerScanLoop` (PO mode) now routes to `/po/:id`; `PoLinePickStep` gained the finish button. The PO E2E test now receives two materials across two visits to the scan screen and asserts each container is `Assigned` to a PO line.

2. **Freeform picks material from an autocomplete, not a scan.** Materials have no barcode until a container label is assigned, so `FreeformMaterialStep` now uses the existing `CatalogAutocomplete` (react-select, `productTypes=[Material]`, `size="lg"` touch target, portal menu) instead of `ScanInput`. Selecting a material navigates to the lot step with its `productCode`. Validated at iPhone-Max that the combobox renders, is touch-friendly, keeps focus inside the scan shell, and positions its dropdown correctly (catalog data is only present on staging, where the E2E exercises real selection). `FreeformMaterialStep.test.tsx` was rewritten for the select→navigate behavior.

## Risks / open items

- **E2E user permissions:** the material-containers endpoints require `Manufacture_MaterialContainers` (Write for create/print). Confirm the synthetic E2E session carries this; if not, that's the first blocker to resolve.
- **Code generator format on staging:** `print-labels` returns whatever `IMaterialContainerCodeGenerator` produces; the spec must use the returned codes verbatim rather than assuming a prefix.
- **Discard is soft:** `Discard` marks `Discarded`, it does not delete rows. Acceptable — it frees the codes from the Assigned set and keeps staging queryable.
