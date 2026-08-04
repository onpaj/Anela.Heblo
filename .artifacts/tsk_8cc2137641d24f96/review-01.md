# Review: Remove dead legacy API-client scaffolding in `frontend/src/services`

## Verdict

**done.**

## What I checked

Independently re-verified the diff (commit `5bc22a11`), not just the artifact claims:

1. **Diff scope matches plan/design/architecture exactly.** Seven files touched, six deletions:
   - `frontend/src/services/api.ts` (FR-1)
   - `frontend/src/services/generated/api-client.ts` + `.gitkeep` (FR-2)
   - `frontend/src/components/test/ApiTestComponent.tsx` (FR-3)
   - `frontend/scripts/check-api-and-generate.js`, `frontend/scripts/generate-api-client.js` (architecture review's recommended optional FR-5, correctly justified — these were the sole source of the deleted placeholder content and are unreferenced by `package.json`/`.github/`/`Dockerfile*`)
   No other files touched. No production code, no behavior change.

2. **FR-4 (versionService untouched)** — `git diff HEAD~1 -- src/services/versionService.ts src/services/__tests__/versionService.test.ts` is empty. Confirmed.

3. **No dangling references** — reran the grep sweep myself (not trusting the artifact's transcript): `services/api['"]`, `services/generated`, `AnelaHebloApiClient`, `ApiTestComponent`, `check-api-and-generate`/`generate-api-client` all return nothing live in `src`/`test` other than the unrelated `regenerate-api-client.sh`.

4. **Build** — ran `npm run build` myself: `Compiled successfully.` No TS/webpack errors.

5. **Tests** — ran `CI=true npm test -- --testPathPattern=services --watchAll=false` myself: `versionService.test.ts` 9/9 passed, matching the development artifact's claim. (Note: an initial raw `npx jest` invocation on my part produced a false-alarm TS parse failure because it bypassed CRA's babel/TS config — not a real regression; the correct `npm test` invocation confirms all green.)

6. Working tree is clean (`git status`) — nothing left uncommitted.

## Assessment against spec/architecture

- Meets all FRs in `plan-01.md` (FR-1 through FR-4), plus the architecture review's non-blocking recommended FR-5, with accurate justification recorded in `development-01.md`.
- Removal boundary matches `design-01.md` exactly — no collateral damage to `frontend/src/api/**` or `versionService.ts`.
- Restores the single-client-seam invariant documented in `docs/architecture/filesystem.md`, as the architecture step intended.
- No security, concurrency, or correctness concerns — this is subtractive dead-code removal with independently confirmed zero importers.

No issues found. Nothing to send back.
