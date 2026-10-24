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

