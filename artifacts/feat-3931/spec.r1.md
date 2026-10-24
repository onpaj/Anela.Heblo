# Specification: Frontend knip backlog triage

## Summary
Burn down the initial knip report (issue #3927 follow-up) for `frontend/`: remove genuinely dead files and dependencies, reduce dead exports, settle the `named|default` duplicate-export convention, and fix knip config false positives, so the CI knip step can later be promoted from non-blocking to blocking.

## Background
knip runs as `continue-on-error: true` in `.github/workflows/ci-feature-branch.yml` with config `frontend/knip.json` (entry: `src/index.tsx`, `src/setupTests.ts`, tests; `jest` and `playwright` plugins disabled). The 2026-08-15 report lists 10 unused files, 2 unused deps, 5 unused devDeps, 1 unlisted dep, 1 unresolved import, 100 unused exports, 98 unused exported types, 15 duplicate exports. Repo verification at planning time found:
- `useTransportBoxTransitions.ts` no longer exists (covered by #3889) - drop from scope.
- Zero importers (grep over `src`, `test`) for `useManualCatalogRefresh`, `useRecalculatePurchasePrice`, `authConfig`, `KnowledgeBaseAskTab`, `KnowledgeBaseSearchTab`, `CreateManufactureOrderModal`, `useFeatureFlag`. `featureFlags` basename matches only `useFeatureFlagsAdmin.ts`/`client.ts` (needs path-level check; likely a different module).
- `weatherIcons.ts` AND `weatherIcons.tsx` both exist in `src/components/customer/cooling/`; `./weatherIcons` imports resolve to `.ts`, so the `.tsx` twin is the dead file (confirm no `.tsx`-specific JSX consumer).
- `@reportportal/agent-js-jest` is used by `test/reportportal-jest.reporter.js`; `@reportportal/agent-js-playwright` by `playwright.config.ts`; `lint-staged` by the `lint-staged` block in `package.json` and husky (`.husky/pre-commit` currently runs `npm test`); these are config-referenced false positives.

## Functional Requirements

### FR-1: Triage and resolve unused files
For each of the 9 remaining files decide delete vs. keep-with-knip-config. Delete those with zero importers (including dynamic imports, lazy routes, tests, docs references). Any file kept must be justified by a `knip.json` `entry`/`ignore` rule with a comment-equivalent note in the PR description.
**Acceptance criteria:**
- `npm run knip` no longer lists any of the 10 files (or #3889 already removed one).
- `npm run build`, `npm run lint` and `npm test` pass.
- Deleted files' own tests, if any, are removed with them; no orphaned test remains.

### FR-2: Triage dependencies
Remove `ajv`, `web-vitals` from dependencies and `@types/react-router-dom`, `jest-junit` from devDependencies only if verified unused (CI sets `JEST_JUNIT_OUTPUT_*` env vars in the workflow, so `jest-junit` is likely used via `--reporters` and must be checked before removal). Retain config-referenced packages (`@reportportal/*`, `lint-staged`) and tell knip about them via config rather than removing. Declare `jest-environment-jsdom` in devDependencies (or switch the test docblock), keeping the lockfile consistent.
**Acceptance criteria:**
- Dependencies knip reports are either removed or explicitly handled in `knip.json` (`ignoreDependencies`).
- `package-lock.json` updated via `npm install`; `npm ci` succeeds; build and tests pass.

### FR-3: Resolve unresolved import
Handle `eslint-config-react-app` in `.eslintrc.json` via `ignoreUnresolved`/`ignoreDependencies` in `knip.json` after confirming it resolves via `react-scripts`.

### FR-4: Reduce unused exports and exported types
Work through the `src/api/hooks/*` and `src/features/changelog/*` clusters: remove the `export` keyword where a symbol is used only locally, delete symbols with no use at all, and trim barrel re-exports nothing imports. Do not touch `src/api/generated/**`. Exports that are public API for tests only should be assessed (knip counts test entries, so these will not be reported).
**Acceptance criteria:**
- Unused exports/types count is reduced to zero, or the remainder is explicitly allow-listed in `knip.json` with a reason.
- No behavior change; build, lint, tests pass.

### FR-5: Decide duplicate-export convention
Pick one policy for `Name|default` pairs. Recommended: keep the intentional dual-export pattern and suppress via knip `rules`/`ignoreIssues` configuration (`duplicates` issue type), documented in `docs/` or knip.json, rather than churning 15 files.
**Acceptance criteria:** duplicates section is empty in the report; decision recorded in PR description.

### FR-6: Promote CI step (conditional)
If the full report is clean after FR-1..FR-5, remove `continue-on-error: true` from the knip step in `ci-feature-branch.yml` (keep the report artifact upload). If not clean, leave non-blocking and list residuals in the PR.
**Acceptance criteria:** step blocking iff `npm run knip` exits 0 locally.

## Non-Functional Requirements

### NFR-1: Performance
No runtime impact; bundle size may only shrink.

### NFR-2: Security
Dependency removals must not drop security overrides (`overrides` block in package.json stays intact).

### NFR-3: Safety
Incremental commits per FR so any removal can be reverted independently; one PR.

## Data Model
None.

## API / Interface Design
None. Generated OpenAPI client under `src/api/generated` is untouched.

## Dependencies
- #3927 (knip setup), #3889 (removes `useTransportBoxTransitions`).
- Local knip run requires `npm ci` in `frontend/`.

## Out of Scope
- Backend dead code.
- Refactoring components beyond export-keyword changes.
- Changing generated API client output.
- E2E suite changes.

## Open Questions
None. Assumptions: the `.tsx` weatherIcons twin is the dead file; `useFeatureFlag.ts`/`featureFlags.ts` removal is acceptable since `docs/development/feature-flags.md` must be checked and updated if it references them; if a doc names them as the intended API, keep them via knip entry instead.

## Status: COMPLETE
