# ReportPortal — self-hosted stack for Anela Heblo

Self-hosted [ReportPortal](https://reportportal.io) is the source of truth for test
history, flakiness, and failure clustering across **all three** Heblo test layers:

| Layer | Runner | Reports via |
|---|---|---|
| Backend | `dotnet test` (xUnit) | `ReportPortal.VSTest.TestLogger` |
| Frontend unit | Jest (CRA) | `@reportportal/agent-js-jest` |
| E2E | Playwright | `@reportportal/agent-js-playwright` |

`docker-compose.yml` here is the **official upstream ReportPortal compose** (pinned to the
5.15.x service images), vendored unmodified so it can be re-synced from upstream. Everything
Heblo-specific lives in `.env`, the CI wiring, and the test-agent config in `frontend/` and
`backend/test/`.

## 1. Bring up the server

Pick a host that stays up (home-lab box or a small VPS) — **not** an ephemeral CI runner.
ReportPortal needs a few GB of RAM (OpenSearch + Postgres + RabbitMQ + the services).

```bash
cd reportportal
cp .env.example .env
# edit .env: set RP_INITIAL_ADMIN_PASSWORD and the Postgres/RabbitMQ credentials
docker compose up -d
```

The UI comes up on **http://<host>:8080**. First login is `superadmin` / the
`RP_INITIAL_ADMIN_PASSWORD` you set. Change it immediately.

> Port 8080 is hardcoded in the vendored compose (the Traefik gateway). Put it behind your
> own reverse proxy / TLS if it's reachable beyond localhost.

## 2. Create the project and an API key

1. In the UI, create a project named **`heblo`** (Administrate → Projects → Add).
2. Create a CI user (or reuse superadmin), open **Profile → API keys**, and mint a key.
   That key is the `RP_API_KEY` the test agents authenticate with.

## 3. Wire CI

The test agents are **opt-in** — they stay completely dormant unless a master switch is on,
so local `dotnet test` / `npm test` / `npx playwright test` runs are unaffected. CI turns
them on by providing these GitHub **repository variables** and **secret**:

| Name | Kind | Example | Purpose |
|---|---|---|---|
| `RP_ENABLE` | variable | `true` | Master switch. Anything other than `true` = fully off. |
| `RP_ENDPOINT` | variable | `https://rp.anela.cz/api/v1` | RP REST API base (note the `/api/v1`). |
| `RP_PROJECT` | variable | `heblo` | Target project. |
| `RP_API_KEY` | secret | `heblo_xxx...` | API key from step 2. |

Set them under **Settings → Secrets and variables → Actions** (Variables tab for the first
three, Secrets tab for the key). With `RP_ENABLE` unset, every workflow behaves exactly as
before.

Wired workflows:
- **`ci-main-branch.yml`** — backend xUnit + frontend Jest report on every push to `main`.
- **`e2e-nightly-regression.yml`** — each Playwright module reports as its own launch attribute.

Feature-branch CI is intentionally left off RP to avoid flooding history with per-push noise;
flip it on there too by adding the same block to `ci-feature-branch.yml` if you want it.

## 4. Launch naming convention

All agents are configured to tag launches so triage can slice by layer and origin:

- **Launch name**: `heblo-backend`, `heblo-frontend`, `heblo-e2e`.
- **Attributes**: `layer` (backend/frontend/e2e), `module` (E2E only — catalog, transport, …),
  `ci` (github-run number), `branch`.

## 5. Optional — MCP server (triage / auto-healing track)

`mcp-server.compose.yml` is an **optional** overlay for the triage-agent track (Track 1 in the
handoff). It is **not** required for reporting. It is also **unverified** against upstream —
read the header in that file before using it.

```bash
docker compose -f docker-compose.yml -f mcp-server.compose.yml up -d
```

## Re-syncing the vendored compose

```bash
curl -sSL -o docker-compose.yml \
  https://raw.githubusercontent.com/reportportal/reportportal/master/docker-compose.yml
```

Re-check the pinned image tags and this README's port/env references after any re-sync.
