# Architecture Review: Frontend knip backlog triage

## Skip Design: true
No new or changed visual UI; deletions, config and package changes only.

## Architectural Fit Assessment
Pure hygiene change within `frontend/`. Fits the existing knip setup (`frontend/knip.json`, `npm run knip`, CI step in `.github/workflows/ci-feature-branch.yml`). No module-boundary or contract changes. Docs to consult before implementing: `docs/development/feature-flags.md` (feature-flag files), `docs/architecture/filesystem.md`, `docs/development/api-client-generation.md` (generated client stays excluded).

## Proposed Architecture

### Component Overview
knip.json (entry/ignore/ignoreDependencies/rules) -> `npm run knip` -> CI step (non-blocking, then blocking). Source edits are deletions and `export` removals only.

### Key Design Decisions

#### Decision 1: Delete vs. configure for each unused file
**Options considered:** delete everything flagged; ignore everything; per-file verification.
**Chosen approach:** per-file verification (grep for static, dynamic `import()`, `React.lazy`, string references in docs and `test/`). Delete when no consumer; otherwise add `entry`.
**Rationale:** the brief itself warns about false positives. Notably `weatherIcons.ts` and `weatherIcons.tsx` coexist; module resolution picks `.ts`, so `.tsx` is dead.

#### Decision 2: Config-referenced packages are retained
**Chosen approach:** keep `@reportportal/agent-js-jest`, `@reportportal/agent-js-playwright`, `lint-staged` (used from `test/reportportal-jest.reporter.js`, `playwright.config.ts`, package.json `lint-staged` block) and add them to `ignoreDependencies`. Verify `jest-junit` against workflow `--reporters`/`JEST_JUNIT_*` use and `@types/react-router-dom` against installed react-router version (v7 ships own types, so likely removable). Note: `.husky/pre-commit` runs `npm test` and lint-staged is not invoked there; leave behavior unchanged (out of scope).
**Rationale:** removal would break reporting/CI.

#### Decision 3: Duplicate exports via config, not churn
**Chosen approach:** suppress `duplicates` for the dual named+default pattern in `knip.json` (`rules`/`ignoreIssues`), documented.
**Rationale:** intentional, widespread pattern; surgical-change principle in CLAUDE.md.

#### Decision 4: Exports - de-export, then delete
**Chosen approach:** per file, drop `export` for locally-used symbols, delete fully unused ones; remove dead barrel re-exports in `src/features/changelog`. Never edit `src/api/generated/**`.

#### Decision 5: Promotion gated on a clean run
Only flip `continue-on-error` when local `npm run knip` exits 0.

## Implementation Guidance

### Directory / Module Structure
Touch only: `frontend/knip.json`, `frontend/package.json`, `frontend/package-lock.json`, listed dead files, `src/api/hooks/*`, `src/features/changelog/*`, optionally the workflow file.

### Interfaces and Contracts
No public contracts change. Hooks' exported DTOs consumed by other files must keep `export`; verify via knip rerun after each batch.

### Data Flow
Edit -> `npm run knip` (delta) -> `npm run build` + `npm run lint` + `npm test`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Deleting a file used by dynamic import or docs-stated API | Medium | grep incl. `React.lazy`, docs; build + tests |
| Removing a dep used only in CI/config (jest-junit) | Medium | check workflow and scripts before removal |
| Lockfile churn / npm version drift | Low | run `npm install` with repo's npm; review diff |
| Process-docs CI gate (owned code touched) | Low | run `python3 scripts/process-docs/check.py index`; update owning docs if triggered |
| Stale knip numbers (report dated 2026-08-15) | Low | re-run knip first and use fresh output as the baseline |
| Removing exports that tests use | Low | tests are knip entries so they are accounted for |

## Specification Amendments
- `useTransportBoxTransitions.ts` already absent; drop from FR-1.
- Add explicit step: regenerate baseline with `npm ci && npm run knip` before any change.

## Prerequisites
`cd frontend && npm ci` to make node_modules available (not installed in the worktree at planning time).
