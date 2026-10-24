# baseline-knip r1 -- implementation summary

No source files modified (baseline-only task). Install: `npm ci` fails with ERESOLVE (knip peer @types/node>=18 vs ^16); CI uses `npm install --legacy-peer-deps`, so `npm ci --legacy-peer-deps` was used and works.

## Fresh knip baseline (2026-10-24, `npm run knip`)
- Unused files: 9 (was 10; `useTransportBoxTransitions.ts` is absent -- covered by #3889)
- Unused dependencies: 2
- Unused devDependencies: 5
- Unlisted dependencies: 1
- Unresolved imports: 1
- Unused exports: 132 (was 100)
- Unused exported types: 280 (was 98)
- Duplicate exports: 15

## Pre-change health
- `npm run build`: passes
- `npm test -- --watchAll=false`: 422 suites / 3738 tests passed, 5 skipped
- `npm run lint`: FAILS at baseline (267 problems: 251 errors, 16 warnings, pre-existing, mostly testing-library rules). Later tasks must not add new lint problems; compare counts rather than expecting zero.
