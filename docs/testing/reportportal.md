# ReportPortal integration

[ReportPortal](https://reportportal.io) is the aggregation layer / source of truth for test
history, flakiness, and failure clustering across all three Heblo test layers. It is the
prerequisite that the E2E auto-healing and triage tracks build on: a triage agent reading
ReportPortal sees run history and flakiness signal instead of a flat single-night snapshot.

The self-hosted server stack and its bring-up instructions live in
[`reportportal/`](../../reportportal/README.md). This document covers how the **tests**
report into it.

## Design: opt-in, zero effect when off

Every agent is dormant unless a master switch is on. The switch is the GitHub **repository
variable** `RP_ENABLE`; when it isn't `true`, nothing reaches out to ReportPortal — local
`dotnet test`, `npm test`, and `npx playwright test` are completely unaffected, and so is any
CI run in a repo that hasn't configured RP.

| Config | Kind | Notes |
|---|---|---|
| `RP_ENABLE` | repo variable | master switch, must be exactly `true` |
| `RP_ENDPOINT` | repo variable | RP REST base incl. `/api/v1` |
| `RP_PROJECT` | repo variable | target project, default `heblo` |
| `RP_API_KEY` | repo secret | API key from the RP UI |

## Layer wiring

### Backend — `dotnet test` (xUnit)

- `backend/test/Directory.Build.props` adds the `ReportPortal.VSTest.TestLogger` package to
  every backend test project and copies the shared `backend/test/ReportPortal.config.json`
  next to each test assembly.
- The config ships with `"enabled": false`. The logger only activates when the run passes
  `-l:ReportPortal` **and** `reportportal_enabled=true` is in the environment.
- `ci-main-branch.yml` adds `--logger ReportPortal` and the `reportportal_*` env overrides
  only when `RP_ENABLE=true`.
- Launch: `heblo-backend`, attributes `layer:backend`, `ci:<run-number>`.

Enable locally (against your own instance) with:

```bash
reportportal_enabled=true \
reportportal_server_url=https://rp.example/api/v1 \
reportportal_server_project=heblo \
reportportal_server_apikey=<key> \
dotnet test Anela.Heblo.sln -l:ReportPortal
```

### Frontend unit — Jest (CRA)

- `@reportportal/agent-js-jest` is wrapped by `frontend/test/reportportal-jest.reporter.js`,
  which feeds it the env-derived config from `frontend/reportportal.config.js`. The wrapper is
  needed because Create React App forbids a `reporters` key in the package.json Jest config
  and Jest can't pass reporter options on the CLI.
- `ci-main-branch.yml` appends `--reporters=./test/reportportal-jest.reporter.js` to the Jest
  run only when `RP_ENABLE=true`. When off, the wrapper degrades to a no-op reporter even if
  loaded.
- Launch: `heblo-frontend`, attributes `layer:frontend`, `ci`, `branch`.

### E2E — Playwright

- `frontend/playwright.config.ts` appends `@reportportal/agent-js-playwright` to the reporter
  list only when `rpEnabled()` returns true (`frontend/reportportal.config.js`). The existing
  html/junit/json/list reporters are untouched.
- `e2e-nightly-regression.yml` passes the `RP_*` env to each module matrix job, with
  `RP_MODULE=<matrix.module>` so every module reports as its own launch tagged `module:<name>`.
- Launch: `heblo-e2e`, attributes `layer:e2e`, `module:<name>`, `ci`, `branch`.

## Turning it on

1. Stand up the server — see [`reportportal/README.md`](../../reportportal/README.md).
2. Create the `heblo` project and mint an API key in the RP UI.
3. In GitHub → Settings → Secrets and variables → Actions:
   - Variables: `RP_ENABLE=true`, `RP_ENDPOINT=https://<host>/api/v1`, `RP_PROJECT=heblo`.
   - Secrets: `RP_API_KEY=<key>`.
4. Next push to `main` reports backend + frontend; the next nightly reports E2E per module.

To pause reporting, set `RP_ENABLE` to anything but `true` (or delete it) — no code change.

## Not yet verified

The optional ReportPortal **MCP server** overlay (`reportportal/mcp-server.compose.yml`),
used only by the triage/auto-healing track, was not confirmed against upstream from the build
environment. Reconcile its image tag and env keys with the current
[reportportal/mcp-server](https://github.com/reportportal/mcp-server) docs before relying on it.
