# CLAUDE.md

Guidance for Claude Code working in this repository.

## Project

**Anela Heblo** — cosmetics company workspace, Clean Architecture monorepo (.NET 8 + React).
MediatR + MVC controllers, Vertical Slice organization, single Docker image, Azure Web App for Containers.

## Documentation map

Read the relevant doc **before** implementation work touches that area. No architectural changes without consulting these first — when in doubt, ask.

**Architecture**
- `docs/📘 Architecture Documentation – MVP Work.md` — modules, data flow, business logic
- `docs/architecture/filesystem.md` — directory layout, component placement, naming conventions
- `docs/architecture/development_guidelines.md` — DTO/contract rules, module boundaries, persistence
- `docs/architecture/infrastructure.md` — deployment, CI/CD, Docker
- `docs/architecture/environments.md` — port mappings, CORS, Azure config
- `docs/architecture/testing-strategy.md` — BE/FE/E2E testing approach

**Development**
- `docs/development/setup.md` — commands, auth setup, Docker, code formatting
- `docs/development/api-client-generation.md` — OpenAPI client generation (C# + TypeScript)
- `docs/development/feature-flags.md` — read before adding or checking a feature flag

**Design**
- `docs/design/ui_design_document.md` — design system, colors, typography, components
- `docs/design/layout_definition.md` — page layout standards (read before any UI work)

**Testing**
- `docs/testing/playwright-e2e-testing.md` — E2E setup, authentication, commands
- `docs/testing/test-data-fixtures.md` — available test data for E2E tests
- `docs/testing/e2e-module-guide.md` — module boundaries for parallel E2E execution

**Integrations**
- `docs/integrations/mcp-server.md` — MCP tools, endpoints, client config (15 tools)
- `docs/integrations/shoptet-api.md` — Shoptet REST API findings

**Features** — `docs/features/` has per-feature specs.

## Coding behavior

**Think before coding.** State assumptions explicitly before starting. If multiple interpretations exist, present them — don't pick silently. If something is unclear, stop, name what's confusing, and ask. Push back when a simpler approach exists.

**Surgical changes.** Touch only what the task requires. Don't improve adjacent code, comments, or formatting. Match existing style even if you'd do it differently. If you notice unrelated dead code, mention it — don't delete it. Every changed line should trace directly to the request.

**Goal-driven execution.** For multi-step tasks, state a brief plan with a verifiable check per step before writing code. Strong success criteria let you loop independently; weak criteria require constant clarification.

## Project-specific rules

These encode **project-specific** gotchas not covered by the global rules in `~/.claude/rules/`.

- **DTOs are classes, never C# records.** OpenAPI client generators mishandle record parameter order. Internal domain types may still be records. (See `docs/architecture/development_guidelines.md`.)
- **API hooks use absolute URLs.** Construct as `${apiClient.baseUrl}${relativeUrl}` — relative URLs hit port 3001 instead of 5001. (See `docs/development/api-client-generation.md`.)
- **E2E tests auth via `navigateToApp()`.** Using `createE2EAuthSession()` alone skips the frontend session and triggers the Entra ID login screen. (See `docs/testing/playwright-e2e-testing.md`.)
- **E2E tests use fixtures from `frontend/test/e2e/fixtures/test-data.ts`.** Throw (don't skip) when expected data is missing. (See `docs/testing/test-data-fixtures.md`.)
- **E2E tests live in their module folder** under `frontend/test/e2e/<module>/`. (See `docs/testing/e2e-module-guide.md`.)
- **Shoptet API findings must be documented before use.** No sandbox — every call hits a live store. Write new endpoints, status values, and quirks to `docs/integrations/shoptet-api.md` before relying on them.

## Validation before completion

Before declaring any task done:

- BE: `dotnet build` + `dotnet format`
- FE: `npm run build` + `npm run lint`
- All tests touched by the change must pass
- E2E: `./scripts/run-playwright-tests.sh` against staging

## Project facts

- Solo developer + AI-assisted PR review.
- Database migrations are manual (not automated in deployment).
- OpenAPI TypeScript client is auto-generated on build.
- Docker images are pushed to Docker Hub.
- E2E suite runs **nightly**, not in PR CI.
- GitHub access via `gh` CLI only — never use MCP GitHub tools.

## Memory

Cross-session knowledge lives in `memory/`. Read relevant files at session start; write learnings during the session.

- `memory/decisions/` — architectural and library choices with reasoning
- `memory/patterns/` — confirmed implementation patterns for this codebase
- `memory/gotchas/` — bugs, edge cases, hard-won lessons
- `memory/context/state.md` — current branch, in-flight work, blockers (update at session end)
