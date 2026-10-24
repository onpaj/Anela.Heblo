# Implementation: trim-changelog-and-remaining-exports

## What was implemented
Cleared every remaining knip unused-export/type entry outside generated code:
- dropped `export` where the symbol is used locally;
- deleted fully dead symbols (e.g. legacy `apiClient` alias, `getApiClient`, `msalConfig`, `graphConfig`, `useLoading`, `useFeatureFlagReady`, unused changelog barrel type re-exports, unused default exports, dead dashboard tile re-exports) plus imports orphaned by those deletions;
- `src/auth/authRecovery.ts`: `clearAuthRecoveryState` and `nextRecoveryAttempt` are consumed only through a dynamic `require` in tests, so they are annotated `@public` instead of un-exported. No blanket ignores added to `knip.json`.

## Verify
- `cd frontend && npx knip` exits 0 (no output).
- `npx tsc --noEmit`: no errors under src.
- `npm run build` succeeds; eslint on all changed files: 0 errors/warnings (repo-wide lint errors are pre-existing test-file issues).
- Jest for all touched directories: 309 suites pass (authRecovery verified after keeping its exports).

## Status
DONE
