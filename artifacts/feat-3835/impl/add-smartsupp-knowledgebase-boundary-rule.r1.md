# Implementation: add-smartsupp-knowledgebase-boundary-rule

## What was implemented
Added a CI-enforced `Smartsupp -> KnowledgeBase` module boundary rule to `ModuleBoundariesTests`, with an empty allowlist (`SmartsuppKnowledgeBaseAllowlist`), mirroring the existing `Article -> KnowledgeBase` rule. This locks in the fix from the previous two tasks against future regressions.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added `SmartsuppKnowledgeBaseAllowlist` (empty) and a new `ModuleBoundaryRule` entry for `Smartsupp -> KnowledgeBase`

## Tests
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"` → Passed! - Failed: 0, Passed: 35, Skipped: 0, Total: 35 (includes the new Smartsupp -> KnowledgeBase theory case).
- `dotnet test Anela.Heblo.sln` (full suite) — pre-existing failures unrelated to this change: Docker/Testcontainers is unavailable in this sandbox (all Postgres-integration tests fail with `Docker is either not running or misconfigured`), and a pre-existing DI circular-dependency in the Catalog/ExpeditionList/Logistics chain (`ICatalogRepository` cycle) causes unrelated controller test failures. Neither touches Smartsupp, KnowledgeBase, or ModuleBoundariesTests code paths.

## How to verify
Run the filtered `ModuleBoundariesTests` command above.

## Notes
Full-suite failures are environmental (no Docker) and a pre-existing unrelated DI cycle — not introduced by this change.

## Status
DONE
