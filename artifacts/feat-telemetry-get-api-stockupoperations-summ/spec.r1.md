```markdown
# Specification: Investigate and resolve 403 storm on `GET /api/StockUpOperations/summary`

## Summary
Over a 7-day window ending 2026-06-12T15:12Z, 209 of 210 calls to `GET /api/StockUpOperations/summary` returned **403 Forbidden** (one returned 500). The same handler served 8,464 successful calls under the MVC operation_name `GET StockUpOperations/GetSummary`. This spec investigates whether the 403s indicate a real authorization defect, a misbehaving frontend caller, or expected gating, and prescribes the remediation that fits.

## Background
The route `/api/StockUpOperations/summary` is defined exactly once, by `StockUpOperationsController.GetSummary()` at `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:113`. The controller carries `[FeatureAuthorize(Feature.Warehouse_StockUp)]` at the class level (line 12), so the summary action requires `Warehouse_StockUp` Read at minimum.

The two telemetry rows (`GET /api/StockUpOperations/summary` vs. `GET StockUpOperations/GetSummary`) almost certainly point at the same controller action: when ASP.NET Core's authorization filter short-circuits before action selection, Application Insights logs the raw URL; on success, it logs `GET {Controller}/{Action}`. This is consistent with the 0 × 200s on the URL row and 100% success on the action-name row.

Frontend usage:
- `frontend/src/api/hooks/useStockUpOperations.ts:112` (`useStockUpOperationsSummary`) polls this endpoint **every 15 s** while mounted (`refetchInterval: 15000`).
- It is consumed by:
  - `frontend/src/components/pages/TransportBoxList.tsx:90`
  - `frontend/src/components/pages/GiftPackageManufacturing/index.tsx:34`

At ~42 403s/day, one user without `Warehouse_StockUp` sitting on one of those pages for ~10 minutes/day would produce the observed volume. The endpoint has been absent from telemetry for the last 2 days, so the source caller may have stopped using it (frontend change, user behavior, or the PR #2962 deploy on 2026-06-12).

PR #2962 ("open dashboard to all users with per-tile permission enforcement", merged 2026-06-12T12:53Z) does not touch StockUpOperations routing or attributes; correlation is coincidental.

## Functional Requirements

### FR-1: Confirm the route mapping and authorization gate
Verify in code (no runtime probing required) that `/api/StockUpOperations/summary` resolves only to `StockUpOperationsController.GetSummary` and is gated by `[FeatureAuthorize(Feature.Warehouse_StockUp)]` (Read). Confirm there is no additional or shadowing route (e.g., legacy controller, endpoint mapping, reverse-proxy rewrite).

**Acceptance criteria:**
- A grep over `backend/src/**/*.cs` yields exactly one route registration for `api/StockUpOperations/summary`.
- The controller class and the `GetSummary` action have no additional `[Authorize]` / `[FeatureAuthorize]` attributes beyond the class-level `Warehouse_StockUp` Read gate.
- No `MapGet`/`MapControllerRoute` override in `Program.cs` or extension modules adds a duplicate or stricter mapping.

### FR-2: Identify the 403 caller(s)
Use Application Insights to identify the principal(s) producing the 403s in the 5-day window 2026-06-05 → 2026-06-12. For each distinct user (or `user_AuthenticatedId`/`user_Id`):
- The page / Referer URL at the time of the call.
- Whether they hold `Warehouse_StockUp` Read in the current permission model.
- Whether their session was authenticated (anonymous calls indicate a different problem class).

**Acceptance criteria:**
- A short note in the resolution PR captures: number of distinct principals, the page(s) they were on, and their `Warehouse_StockUp` status.
- The note states whether each principal is "should-have-access" or "correctly-denied".

### FR-3: Choose and apply a remediation
Pick exactly one of the following based on FR-2 findings:

- **R-A (Frontend gate)** — if the 403 callers should NOT see stock-up summary tiles: gate the `useStockUpOperationsSummary` hook (or its callers in `TransportBoxList.tsx`, `GiftPackageManufacturing/index.tsx`) on the caller holding `Warehouse_StockUp` Read. Do not invoke the hook (and do not render the dependent UI) when the permission is absent.
- **R-B (Broaden the gate)** — if the 403 callers SHOULD see the summary: change the attribute on `GetSummary` (or split the controller) so the summary endpoint is reachable by the broader audience, while keeping write operations (`{id}/retry`, `{id}/accept`) at their current `Warehouse_StockUp` Write level.
- **R-C (No change, document)** — if the 403 source self-resolved (absent from telemetry for 2 days) and no caller is impacted: document the finding, leave the gate in place, add a guardrail (see FR-4), and close.

**Acceptance criteria:**
- Exactly one remediation path is taken and is justified in the PR description against FR-2 findings.
- Whichever path is chosen, no authenticated user who legitimately needs the data is left in a 403 state after the fix.
- Write-level actions on `StockUpOperationsController` remain gated at `Warehouse_StockUp` Write — they are out of scope for broadening.

### FR-4: Add regression detection
Add automated coverage that would catch a recurrence of the same misalignment between caller and gate:

- An authorization integration test (xUnit + `WebApplicationFactory`) asserting the current authorization contract on `GET /api/StockUpOperations/summary`: a principal without `Warehouse_StockUp` Read receives the documented outcome (403 if R-A/R-C, 200 if R-B), and a principal with the required permission receives 200.
- If R-A is chosen: a frontend unit test for the affected page(s) asserting `useStockUpOperationsSummary` is not invoked when the user lacks `Warehouse_StockUp` (mock the permission context, assert the hook's underlying client call is not made).

**Acceptance criteria:**
- New BE test lives under `backend/test/Anela.Heblo.Tests/Authorization/` (mirrors `DashboardControllerAuthorizationTests`, `GridLayoutsControllerAuthorizationTests`) and passes.
- New FE test (if applicable) lives next to the modified page under `__tests__/` and passes.

### FR-5: Investigate the single 500
One request in the 7-day window returned 500. Determine whether it is the same handler (it would be, given the route resolves only to `GetSummary`) and whether it indicates a pre-existing latent defect (e.g., unhandled exception in `GetStockUpOperationsSummaryHandler` when authorization is somehow skipped, malformed `sourceType`, DB timeout). If a defect is found, file a follow-up issue; this spec does not require a fix unless the defect is trivially adjacent to the work in FR-3.

**Acceptance criteria:**
- Either: the 500's root cause is identified and either fixed here or filed as a follow-up issue with a link in the PR; or: telemetry is insufficient to attribute it and that is stated.

## Non-Functional Requirements

### NFR-1: Performance
No regression. The current MVC route handles 8,464 requests/7d at p95 = 82 ms; any change must preserve that. If the summary polling continues, the 15 s polling interval and `staleTime: 14000` in `useStockUpOperationsSummary` are unchanged.

### NFR-2: Security
- Do not weaken authorization unless FR-2 demonstrates that the 403 callers legitimately need access. In that case, lowering the gate on the read-only summary endpoint is acceptable but write-level actions on the controller MUST remain at `Warehouse_StockUp` Write.
- Do not introduce client-side-only enforcement as the security boundary. R-A is a UX/cost optimization that reduces noise; the server `[FeatureAuthorize]` remains the authoritative gate.
- No PII or token data is involved beyond what already flows through the existing authorization pipeline.

### NFR-3: Observability
- After deployment, the new 403 rate on this URL should drop to near-zero for the production fleet within 24 hours.
- Add a one-line note to `docs/integrations/` or the relevant feature doc explaining the chosen remediation so the next telemetry-anomaly responder has context.

### NFR-4: Backwards compatibility
The OpenAPI client (`frontend/src/api/generated/api-client.ts:12039`) is generated from the BE; any change to the route or its response shape must regenerate the client and pass `npm run build`. The endpoint's response type (`GetStockUpOperationsSummaryResponse`) must not change in this work.

## Data Model
No persistence changes. The endpoint is read-only and aggregates from existing `StockUpOperation` rows via `GetStockUpOperationsSummaryHandler`.

## API / Interface Design
- Path: `GET /api/StockUpOperations/summary?sourceType={TransportBox|GiftPackageManufacture}`
- Auth (after fix, contingent on FR-3 outcome):
  - R-A / R-C: unchanged — `Warehouse_StockUp` Read.
  - R-B: broadened — TBD by FR-2 findings, documented in the PR.
- Response shape (unchanged): `GetStockUpOperationsSummaryResponse` with success/error envelope and per-state counts.

## Dependencies
- `Microsoft.AspNetCore.Authorization` infrastructure and the project's `FeatureAuthorizeAttribute` / `AccessRoles` mechanism.
- Application Insights access for FR-2 (caller attribution); the existing `docs/routines/telemetry-anomaly/` workflow describes the query pattern.
- React Query polling in `useStockUpOperationsSummary` (no library upgrade required).

## Out of Scope
- Refactoring write endpoints on `StockUpOperationsController` (`{id}/retry`, `{id}/accept`).
- Changing the underlying `GetStockUpOperationsSummaryHandler` query or its DB indexes (covered by `docs/superpowers/plans/2026-05-06-stockupoperations-summary-performance.md`).
- The polling cadence itself (15 s) — out of scope unless FR-3 chooses R-C and stakeholders want to lower telemetry noise from any remaining unauthorized callers.
- Dashboard tile authorization (covered by PR #2962); the correlation in the brief is incidental.
- Application Insights operation_name normalization (the dual-row symptom is a known telemetry quirk, not a defect).

## Open Questions
None.

## Status: COMPLETE
```