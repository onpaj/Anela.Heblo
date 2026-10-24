# Implementation Plan: Frontend knip backlog triage

**Goal:** Burn down the initial knip report for `frontend/` (dead files, deps, exports, duplicates, config false positives) and promote the CI knip step to blocking only if `npm run knip` exits 0.

**Architecture:** Hygiene only. Edits are file deletions, `export` keyword removals, `frontend/knip.json` config, `frontend/package.json` + lockfile, and one CI workflow line. No behavior, contract or UI change. `src/api/generated/**` is never touched.

**Tech stack:** knip, npm, CRA/react-scripts (jest), GitHub Actions.

**Rules for every task:** work in `frontend/`; one commit per task (independent revert, NFR-3); after each task run `npm run build`, `npm run lint`, `npm test -- --watchAll=false`, and `npm run knip` (record delta); never edit `src/api/generated/**`; keep the `overrides` block in package.json intact. Read `docs/development/feature-flags.md` before the feature-flag task.

### task: baseline-knip

Establish a fresh baseline (the 2026-08-15 report is stale).

Files: none modified (scratch output only, in the scratchpad dir).

- [ ] `cd frontend && npm ci`
- [ ] `npm run knip 2>&1 | tee <scratchpad>/knip-baseline.txt`; also try `npx knip --reporter json` for machine-readable counts.
- [ ] Record per-section counts (files, deps, devDeps, unlisted, unresolved, exports, types, duplicates). Confirm `useTransportBoxTransitions.ts` is absent (covered by #3889).
- [ ] Confirm `npm run build`, `npm run lint`, `npm test -- --watchAll=false` pass before any change, so later failures are attributable.
- [ ] No commit (nothing changed).

Verify: baseline file exists; counts noted for the PR description.

### task: delete-dead-files

Delete files with zero consumers after per-file verification (FR-1).

Files (delete, only after verification): `useManualCatalogRefresh.ts`, `useRecalculatePurchasePrice.ts`, `authConfig.ts`, `src/components/customer/cooling/weatherIcons.tsx`, `KnowledgeBaseAskTab.tsx`, `KnowledgeBaseSearchTab.tsx`, `CreateManufactureOrderModal.tsx`, plus any other file in the fresh baseline's "Unused files" section. Locate exact paths with `git ls-files | grep <name>`. Leave the feature-flag files to the next task.

- [ ] For each file, grep `src`, `test`, `docs`, `public` for the basename and path, including `import(`, `React.lazy`, `require(`, and string references. Also grep the matching test file (`*.test.ts(x)`).
- [ ] For `weatherIcons.tsx`: confirm `./weatherIcons` resolves to `.ts` (extension order in tsconfig/CRA: `.ts` before `.tsx`) and that no consumer needs JSX exports unique to the `.tsx` twin. Diff the two files' export lists first.
- [ ] `git rm` each verified-dead file and its own test/story/mocks, if any.
- [ ] If a file has a live consumer or is docs-stated API, do not delete; add it to `knip.json` `entry` and note it for the PR description.
- [ ] Run build, lint, tests, knip. Unused-files section must be empty (or only the justified kept entries).
- [ ] Commit: `chore(frontend): remove dead files flagged by knip (#3931)`.

Verify: knip "Unused files" empty; build/lint/tests green; no orphaned test.

### task: feature-flag-files

Resolve `featureFlags.ts` and `useFeatureFlag.ts` (FR-1, docs-gated).

Files: `featureFlags.ts` (locate with `git ls-files | grep -i featureFlags`; path-level check, basename also matches `useFeatureFlagsAdmin.ts`/`client.ts`), `useFeatureFlag.ts`, possibly `docs/development/feature-flags.md`.

- [ ] Read `docs/development/feature-flags.md`. If it names these files as the intended frontend API, keep them and add them to `knip.json` `entry` (record why); otherwise delete.
- [ ] If deleting, grep for path-level and basename imports (distinguish from `useFeatureFlagsAdmin`), remove their tests, and update the doc if it references them.
- [ ] Run build, lint, tests, knip.
- [ ] Commit: `chore(frontend): resolve unused feature-flag files (#3931)`.

Verify: knip no longer lists either file; doc consistent with code.

### task: triage-dependencies

Resolve dependency sections and unresolved import (FR-2, FR-3).

Files: `frontend/package.json`, `frontend/package-lock.json`, `frontend/knip.json`.

- [ ] Verify each is unused before removal: `ajv`, `web-vitals` (grep `src`, `test`, config files, `package.json` scripts, `.github/workflows`), `@types/react-router-dom` (installed react-router is v7 with bundled types; confirm via `npm ls react-router react-router-dom`), `jest-junit` (grep workflows for `--reporters`, `JEST_JUNIT_`, and package.json jest config; keep if used).
- [ ] Remove verified-unused entries with `npm uninstall <pkg>` (updates the lockfile). Do not touch `overrides`.
- [ ] Add `ignoreDependencies` to `knip.json`: `@reportportal/agent-js-jest`, `@reportportal/agent-js-playwright`, `lint-staged`, `eslint-config-react-app` (confirm it resolves via react-scripts), plus any package verified config-only (e.g. `jest-junit` if kept).
- [ ] Declare `jest-environment-jsdom` as a devDependency matching the jest major used by react-scripts (`npm ls jest`); `npm install -D jest-environment-jsdom@<matching>`.
- [ ] `rm -rf node_modules && npm ci` must succeed; then build, lint, tests, knip.
- [ ] Commit: `chore(frontend): triage knip dependency findings (#3931)`.

Verify: knip dependency, devDependency, unlisted and unresolved sections empty; `npm ci` succeeds.

### task: duplicate-exports-config

Settle the `Name|default` convention via config (FR-5).

Files: `frontend/knip.json`, optionally a short note in `docs/architecture/development_guidelines.md` only if a suitable section exists.

- [ ] Add `"rules": { "duplicates": "off" }` (or the equivalent `ignoreIssues`/`exclude: ["duplicates"]` supported by the installed knip version; check `npx knip --help` / knip docs for the installed version).
- [ ] Add a note recording the decision (knip.json is JSON, so put the rationale in the PR description and, if appropriate, the docs note).
- [ ] Run knip; the duplicates section must be empty.
- [ ] Commit: `chore(frontend): allow intentional named+default dual exports in knip (#3931)`.

Verify: duplicates section empty; no source files changed.

### task: trim-hook-exports

Reduce unused exports/types in `src/api/hooks/*` (FR-4).

Files: `frontend/src/api/hooks/*` entries listed by knip.

- [ ] From fresh knip output, list unused exports/types in `src/api/hooks/*`. Work in batches of about 10 files.
- [ ] For each symbol: if used locally, drop `export`; if unused entirely, delete it (and any import left unused). Keep `export` for anything another file or test imports.
- [ ] After each batch run `npx tsc --noEmit` (or `npm run build`) and knip delta.
- [ ] Commit per batch or once: `refactor(frontend): trim unused hook exports (#3931)`.

Verify: no hooks entries remain in knip exports/types sections; build and tests green.

### task: trim-changelog-and-remaining-exports

Reduce remaining unused exports/types (FR-4).

Files: `frontend/src/features/changelog/*` and any other non-generated files left in knip's export/types sections.

- [ ] Remove dead barrel re-exports in `src/features/changelog` that nothing imports; de-export or delete symbols per the same rule as above.
- [ ] Re-run knip. For any residual that is genuinely required (public API consumed outside knip's view), add a narrow `ignoreIssues`/`ignoreExportsUsedInFile` entry in `knip.json` with the reason noted for the PR description. Do not add blanket ignores.
- [ ] Commit: `refactor(frontend): trim unused changelog and remaining exports (#3931)`.

Verify: knip exports and types sections empty or only explicitly allow-listed.

### task: promote-ci-and-docs

Conditionally make the knip CI step blocking (FR-6) and satisfy process-docs gate.

Files: `.github/workflows/ci-feature-branch.yml`, any owning `docs/processes/` doc if CI process-docs check demands it.

- [ ] Run `npm run knip`; note the exit code.
- [ ] If exit code is 0: remove `continue-on-error: true` from the knip step only (keep the report artifact upload). If not 0: leave the workflow untouched and list residuals for the PR description.
- [ ] Run `python3 scripts/process-docs/check.py index` from the repo root; if the workflow edit triggers an owning doc, update it (or bump `verified_at`) in the same PR.
- [ ] Final full validation: `npm run build`, `npm run lint`, `npm test -- --watchAll=false`, `npm run knip`; confirm `git diff --stat` touches only the allowed paths and not `src/api/generated/**`.
- [ ] Commit: `ci(frontend): make knip step blocking (#3931)` (or skip commit if left non-blocking).

Verify: step is blocking iff local knip exits 0; PR notes list baseline vs final counts, kept-file justifications, and the duplicates decision.
