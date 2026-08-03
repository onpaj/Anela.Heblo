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
| `TS_AUTHKEY` | repo secret | Tailscale tagged auth key (see Reachability) |

## Reachability (Tailscale)

ReportPortal is self-hosted on the Tailnet (`nas.*.ts.net`), which GitHub-hosted
runners cannot resolve on their own. Each RP-reporting job therefore joins the Tailnet
first via `tailscale/github-action@v4`, gated on `RP_ENABLE` and marked
`continue-on-error` so a failed/absent connection never breaks CI — reporting simply
no-ops (it is non-fatal on every layer).

Set up once in the Tailscale admin console (**Settings → Keys → Generate auth key**):
1. Create an **auth key** that is **Reusable**, **Ephemeral**, and **tagged `tag:ci`**.
   (Ephemeral so each CI node is auto-removed; reusable so it works across many runs.)
2. Add it as the `TS_AUTHKEY` repo secret.
3. Ensure `tag:ci` exists in `tagOwners` and your ACLs let it reach the RP host:port.

> Auth keys expire (max 90 days) — rotate `TS_AUTHKEY` before then. If your plan offers
> OAuth clients, prefer those (non-expiring): swap the `authkey` input for
> `oauth-client-id` / `oauth-secret` + `tags: tag:ci`.

> Reporting is non-fatal by design: if ReportPortal (or the Tailnet hop) is unreachable,
> tests still pass and deploys still proceed — you just get no data for that run.

## Layer wiring

### Backend — `dotnet test` (xUnit)

Backend reporting is **asynchronous**, decoupled from the `backend-tests` job that gates
`build-and-push`/`deploy-production`. This is different from every other layer in this
doc, for a specific reason: `ReportPortal.VSTest.TestLogger` reports live, making one HTTP
round-trip per test. Against 6,000+ backend tests and a NAS-hosted RP instance reachable
only over Tailscale, that took the `backend-tests` job from ~4 minutes to 1–2 **hours** —
and because GitHub Actions `needs:` waits for job completion (not just success), that
latency blocked every deploy on `main`.

Instead:

- `backend/test/Directory.Build.props` adds `JunitXml.TestLogger` to every backend test
  project. `ci-main-branch.yml`'s `backend-tests` job always passes
  `--logger "junit;LogFilePath=junit-results.xml"` (no network calls, safe to run locally
  too) and uploads the resulting `coverage/junit-results.xml` as a build artifact when
  `RP_ENABLE=true`.
- A separate `backend-report-portal` job — which nothing else `needs:` — downloads that
  artifact and `POST`s it to ReportPortal's `junit/import` endpoint
  (`POST {RP_ENDPOINT}/plugin/{RP_PROJECT}/junit/import`) after `backend-tests` finishes.
  It runs in parallel with `build-and-push`/`deploy-production`, so however long the
  import takes has zero effect on deploy latency.
- `ReportPortal.VSTest.TestLogger` (the live logger) is still present in
  `Directory.Build.props` for local, single-project debugging against your own instance —
  it's just no longer wired into CI. Enable it locally with:

```bash
reportportal_enabled=true \
reportportal_server_url=https://rp.example/api/v1 \
reportportal_server_project=heblo \
reportportal_server_apikey=<key> \
dotnet test Anela.Heblo.sln -l:ReportPortal
```

- Launch: `heblo-backend`, attributes `layer:backend`, `ci:<run-number>` — same as before,
  just arriving asynchronously instead of live.
- Known risk: ReportPortal's JUnit import endpoint has timed out on files as small as
  ~1.3MB on under-resourced servers (see `reportportal/reportportal#2474`). The import
  job is `continue-on-error: true` at every step and nothing downstream depends on it, so
  a timeout just means that run's data doesn't make it into RP — consistent with the
  "reporting is non-fatal by design" principle above.

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
