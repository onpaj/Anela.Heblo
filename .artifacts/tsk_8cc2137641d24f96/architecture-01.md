# Architecture review: Remove dead legacy API-client scaffolding in `frontend/src/services`

## Verdict

**Approved as scoped.** The plan and design correctly identify a pure, zero-risk dead-code removal. I re-verified every load-bearing claim independently against the current tree (not just re-reading the artifacts) and found the removal boundary sound. One factual correction to the evidence trail below (does not change the outcome) and one additional orphaned file worth folding into the same removal.

## Alignment with existing invariants

- `docs/architecture/filesystem.md:217,243` names `frontend/src/api/generated/api-client.ts` as **the** frontend TypeScript client location and `frontend/src/api/` (line 56) as **the** API client + co-located tests seam. There is no mention of `frontend/src/services/` as a client location anywhere in that doc. Removing `services/api.ts` and `services/generated/` doesn't just delete unused code — it collapses the tree back to matching the documented single-seam invariant.
- Re-ran the importer checks independently (`git grep` across `frontend/src` and `frontend/test`, plus `.github/` and `Dockerfile*`): zero importers of `services/api.ts`, zero importers of `services/generated/api-client.ts`, zero importers of `components/test/ApiTestComponent.tsx`, confirmed. Every hit for the bare symbol `ApiClient` outside the two dead files resolves to the canonical `frontend/src/api/**` seam (`api/client.ts`, `api/generated/api-client.ts`, `api/hooks/*`) — none of it touches `services/`.
- `frontend/scripts/check-api-and-generate.js` (not mentioned in plan/design) is the script that originally *wrote* the `services/generated/api-client.ts` placeholder (it hardcodes the same `AnelaHebloApiClient`/`WeatherForecast` stub verbatim). This raised a real question: could this script regenerate the file after deletion, silently undoing the fix? Checked `frontend/package.json` scripts, `.github/` workflows, and `Dockerfile*` — **nothing invokes `check-api-and-generate.js` or `generate-api-client.js`**. Both scripts are themselves orphaned/unwired. So there's no regeneration hazard, but this is worth flagging to the implementer (see below).
- No dynamic/string-based imports (`require(`, template-literal path construction) of any of the three target paths exist — matches the plan's claim.
- `versionService.ts` (the one file in `services/` staying behind) already imports from `../api/client` — confirmed, correctly excluded from scope.

## Correction to the issue/plan evidence (non-blocking)

The original issue states `services/api.ts` "reads `process.env.REACT_APP_API_URL`, an env var the app does not use." That's not accurate: `frontend/src/config/runtimeConfig.ts:25-102` — which backs the **canonical** `api/client.ts` via `getConfig().apiUrl` — reads the same `REACT_APP_API_URL`, and `package.json`'s `start`/`start:automation` scripts set it. The var is real and actively used by the canonical seam; it's `services/api.ts` duplicating that same variable read in a second, weaker client that's the actual problem (no auth header, no 401 handling, hardcoded fallback host), not the variable itself. This doesn't affect the removal decision — it only means the design shouldn't be read as "this env var is dead too." No action needed; flagging so nobody re-derives a wrong conclusion from the issue text later.

(Separately, `frontend/src/api/README.md:77` documents the base-URL var as `REACT_APP_API_BASE_URL`, which doesn't match the actual var name `REACT_APP_API_URL` read in `runtimeConfig.ts`. That's a pre-existing doc/code drift, unrelated to this deletion — out of scope here, but worth a follow-up issue if not already tracked.)

## Additional orphan worth including in the same change

`frontend/scripts/check-api-and-generate.js` and `frontend/scripts/generate-api-client.js` are themselves dead — confirmed zero references from `package.json`, `.github/`, or `Dockerfile*`. `check-api-and-generate.js` is the sole source of the `AnelaHebloApiClient`/`WeatherForecast` placeholder content that `services/generated/api-client.ts` contains, so leaving it behind after deleting `services/generated/` leaves a second landmine: if anyone ever wires it back into a build step (e.g. copy-pasting a "regenerate on missing API" pattern), it recreates exactly the dead seam this task removes, at the exact path being deleted.

This is not required to satisfy the issue's stated scope (the issue only names `services/api.ts`, `services/generated/`, and `ApiTestComponent.tsx`), so I'm not expanding FR-1..FR-4. But given it directly writes to the path FR-2 deletes and is unreferenced by anything, I recommend the implementer fold it in as an FR-5 (or a one-line follow-up note in the PR description) rather than leave a dangling script that regenerates dead scaffolding no build step calls. If the implementer prefers strict scope discipline, leaving it for a separate cleanup issue is also acceptable — flagging as a judgment call, not a blocker.

## Risks — none material

- **Regeneration hazard**: addressed above — not wired into any build path today. If FR-5 (deleting `check-api-and-generate.js`/`generate-api-client.js`) is skipped, this residual risk persists but is inert until someone re-wires the script.
- **Empty directory git semantics**: correct as designed — git doesn't track empty dirs, no extra step needed after both files under `services/generated/` are removed.
- **Build/lint/test gate**: the design's verification plan (grep sweep + `npm run build` + `npm run lint` + full test suite) is sufficient to catch anything the static analysis here missed (e.g. TS path-mapping or barrel exports). No stronger gate is needed for a deletion-only change.

## Implementation guidance

No new components, interfaces, or data flow — this is subtractive. Sequence for the implementer:
1. `git rm frontend/src/services/api.ts`
2. `git rm frontend/src/services/generated/api-client.ts frontend/src/services/generated/.gitkeep`
3. `git rm frontend/src/components/test/ApiTestComponent.tsx`
4. (Recommended, not required by the issue) `git rm frontend/scripts/check-api-and-generate.js frontend/scripts/generate-api-client.js` — confirm first with `git grep -n "check-api-and-generate\|generate-api-client.js"` across `frontend/package.json`, `.github/`, `Dockerfile*` that nothing was added since this review that calls them.
5. Re-run the full grep sweep from `design-01.md` §Verification design.
6. `npm run build && npm run lint` in `frontend/`, then the frontend test suite.

## Prerequisites before implementation begins

None — plan and design are implementation-ready as written. No open architectural questions remain.
