# Project State

_Update this file at the end of significant sessions._

## Project Summary

**Anela Heblo** — cosmetics company workspace app. Monorepo: .NET 8 backend + React frontend. Single Docker image deployed to Azure Web App for Containers.

## Active Branches (as of 2026-03-29)

- `main` — stable, production
- `feat/444-shoptet-hydration` — Shoptet test environment hydration feature (in progress)
- `feat/405-memory-directory` — this memory directory implementation

## Recently Completed

- Shoptet test environment hydration (issue #444): added `SHOPTET_HYDRATE` env var gate, Rider launch profile, non-hardcoded storage
- MCP server: 15 tools across Catalog, Manufacturing, Batch Planning, Knowledge Base

## Pending / Known Issues

- Memory directory (issue #405): adding cross-session knowledge accumulation — this PR
- Database migrations are manual (not automated in deployment)

## Key Infrastructure Notes

- CI runs: frontend Jest + backend .NET tests on PR
- Nightly: full Playwright E2E against staging
- OpenAPI TypeScript client auto-generated on `dotnet build`
- Secrets managed via local `secrets.json` (never `dotnet user-secrets set`)
