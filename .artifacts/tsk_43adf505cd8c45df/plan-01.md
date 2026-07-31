# Plan — ApiClient: phantom backend C# client pipeline

## Summary

`backend/src/Anela.Heblo.API.Client/` and `docs/development/api-client-generation.md`'s "Backend C# Client" section describe a generated C# API client that has never existed in this codebase — no MSBuild target, no NSwag config, no generated code, and zero references anywhere. This plan removes the phantom project and its documentation, keeping only the real, working frontend TypeScript client pipeline documented and referenced correctly.

## Context

Verified directly against the repo (2026-07-31):

- `backend/src/Anela.Heblo.API.Client/Anela.Heblo.API.Client.csproj` is a bare `net8.0` project — no `PackageReference`, no NSwag, no MSBuild target. `Generated/` contains only `.gitkeep`.
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` has exactly one client-generation target, `GenerateFrontendClientManual` (line 95), which runs `dotnet nswag run nswag.frontend.json` — a real, working TypeScript pipeline. No `GenerateApiClient`/`GenerateBackendClient` target exists anywhere in the repo (`grep` for `GenerateBackendClient`, `swagger2csclient`, `AnelaHebloApiClient` in live code returns zero hits — only doc files and historical `.artifacts`/`docs/superpowers/plans` task write-ups that copied the doc's fictional claims).
- `backend/src/Anela.Heblo.API/nswag.frontend.json` exists; there is no `nswag.backend.json` or equivalent.
- `Anela.Heblo.sln:24` lists `Anela.Heblo.API.Client`, but no other `.csproj` references it and no `.cs` file uses it (repo-wide grep, excluding its own directory).
- Git history: the project has exactly two commits (`246b87ca`, `1d997169`) — initial creation and one unrelated refactor pass. No commit ever wired up generation.
- The stated use case — "internal backend testing and server-to-server communication" — has no real counterpart: backend integration tests (`backend/test/Anela.Heblo.Tests/...`) use `WebApplicationFactory` in-process, not an HTTP client.
- `docs/architecture/module-map.md:1110` already independently flags `backend/src/Anela.Heblo.API.Client/Generated/` as "generated (noted in #42)" and assigns ownership of the whole client-generation surface (including this doc) to Part #42 — this task's part.
- This phantom has already leaked into other AI-authored planning artifacts (`.artifacts/feat-3487/...`, `.artifacts/feat-3446/...`, `docs/superpowers/plans/2026-06-01-decouple-ireportbuilderservice.md`, etc.), which cite regenerating `AnelaHebloApiClient.cs` as a real verification step. Those are historical/generated planning artifacts, not live code — out of scope to edit, but worth naming so nobody mistakes their existence for evidence the pipeline is real.

**Decision:** abandon the backend C# client, don't build it. Rationale: zero real usage anywhere in two years of history, no genuine "server-to-server" caller exists, the documented generation mechanism was apparently never implemented (not just decayed), and building it now would be speculative infrastructure with no consumer — against this repo's "don't design for hypothetical future requirements" rule. The frontend TypeScript pipeline is real, used, and stays untouched.

## Functional requirements

**FR-1 — Remove the empty backend client project from the solution.**
- Delete `backend/src/Anela.Heblo.API.Client/` (csproj + `Generated/.gitkeep`) entirely.
- Remove its `Project(...)` block and any `ProjectConfigurationPlatforms`/`NestedProjects` entries for GUID `{3D738B41-81F8-4743-8F64-98790902405F}` from `Anela.Heblo.sln`.
- Acceptance: `grep -n "Anela.Heblo.API.Client" Anela.Heblo.sln` returns nothing; the directory no longer exists; `dotnet build` from repo root succeeds with no missing-project errors.

**FR-2 — Rewrite `docs/development/api-client-generation.md` to describe only the real pipeline.**
- Remove the entire "Backend C# Client" section (current lines ~13–71: location, auto-generation PostBuild snippet, manual `GenerateBackendClient` command, configuration, usage example).
- Remove the "Two clients are generated" framing in the Overview; replace with a single-client description (TypeScript only).
- In "Regeneration Workflow" (manual regeneration list) and "Automatic Regeneration", drop the `dotnet msbuild ... -t:GenerateBackendClient` line and the "Building `Anela.Heblo.API` in Debug mode automatically regenerates both clients" claim — only the frontend TypeScript client is generated, and only via `GenerateFrontendClientManual` (manual target) / `npm run generate-client` (prebuild script). There is no automatic PostBuild client generation at all (`GenerateFrontendClient` PostBuild target in the csproj is present but `Condition="false"` — permanently disabled).
- Acceptance: `grep -n "GenerateBackendClient\|AnelaHebloApiClient\|Backend C# Client" docs/development/api-client-generation.md` returns nothing.

**FR-3 — Fix the other doc that repeats the same phantom claim.**
- `docs/architecture/filesystem.md:208–215` has a "Backend C# Client" subsection (Location/Auto-generation/Tool/Output) mirroring the same fiction. Remove it, keeping only the Frontend TypeScript Client subsection.
- Acceptance: `grep -rn "AnelaHebloApiClient\|Anela.Heblo.API.Client" docs/architecture/filesystem.md` returns nothing.

**FR-4 — Update `docs/architecture/module-map.md` Part #42 ownership list.**
- Line 944 lists `backend/src/Anela.Heblo.API.Client/` as owned by Part #42 — remove that line since the directory no longer exists.
- Line 1110's "deliberately unassigned … generated" note for `backend/src/Anela.Heblo.API.Client/Generated/` becomes stale once the directory is deleted — remove that clause (keep the `frontend/src/api/generated/` half of the line).
- Acceptance: `grep -n "Anela.Heblo.API.Client" docs/architecture/module-map.md` returns nothing.

## Non-functional requirements

- No behavior change to the frontend client pipeline (`GenerateFrontendClientManual`, `nswag.frontend.json`, `scripts/regenerate-api-client.sh`, `npm run generate-client`) — none of these are touched.
- `dotnet build` and `dotnet format` must succeed after the `.csproj`/`.sln` edit (per repo validation gate).
- Doc edits are deletions/corrections only — no new sections, no speculative "how to add this back later" guidance (matches this repo's no-speculative-design rule).

## Data model

Not applicable — this is a dead-code and documentation removal task; no runtime data model is affected.

## Interfaces

Not applicable — no API surface changes. The real (frontend) OpenAPI contract and generated TypeScript client are unaffected.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.API.Client/` (delete)
- `Anela.Heblo.sln` (remove project reference)
- `docs/development/api-client-generation.md` (rewrite, drop backend section)
- `docs/architecture/filesystem.md` (drop backend client subsection)
- `docs/architecture/module-map.md` (drop stale ownership/deliberately-unassigned lines for the deleted directory)

**Out of scope:**
- The real frontend TypeScript client pipeline (`nswag.frontend.json`, `GenerateFrontendClientManual`, `scripts/regenerate-api-client.sh`) — works today, not touched.
- Historical planning artifacts under `.artifacts/` and `docs/superpowers/plans/` that mention `AnelaHebloApiClient.cs` — these are frozen records of past (AI-authored) task runs, not living docs; correcting them retroactively is not productive and is not part of this task.
- Building an actual backend C# client from scratch. Rejected per the Context section's decision — no real consumer, no history of intended use beyond the doc's own description.

## Rough plan

1. Delete `backend/src/Anela.Heblo.API.Client/` (project + `Generated/.gitkeep`).
2. Edit `Anela.Heblo.sln`: remove the `Project(...)...EndProject` block for `Anela.Heblo.API.Client` and its `{3D738B41-81F8-4743-8F64-98790902405F}` entries under `GlobalSection(ProjectConfigurationPlatforms)` (and `NestedProjects`/solution-folder mapping, if any).
3. Run `dotnet build` from repo root to confirm the solution still builds clean with the project gone.
4. Edit `docs/development/api-client-generation.md`: remove the "Backend C# Client" section, adjust the Overview's "two clients" framing to one client, and strip backend-client mentions from the Regeneration Workflow / Troubleshooting sections.
5. Edit `docs/architecture/filesystem.md`: remove the "Backend C# Client" subsection under the client-generation area.
6. Edit `docs/architecture/module-map.md`: remove the stale ownership line and the stale "deliberately unassigned … generated" clause for the deleted directory.
7. Re-run repo-wide check: `grep -rn "GenerateBackendClient\|swagger2csclient\|AnelaHebloApiClient\|Anela.Heblo.API.Client" --include="*.md" --include="*.sln" --include="*.csproj" .` (excluding `.artifacts/`, `docs/superpowers/plans/`) — expect zero hits outside the excluded historical artifacts.
8. Run `dotnet build` + `dotnet format` (BE) per repo validation gate. No FE changes, so `npm run build`/`npm run lint` are unaffected but can be run as a sanity check since `nswag.frontend.json`/`GenerateFrontendClientManual` are untouched.

## Open questions

- None blocking. The one judgment call — restore vs. abandon the backend client — is resolved above in favor of abandonment, based on: zero historical usage, no real "server-to-server" caller, and the repo's explicit anti-speculative-design guidance. If the operator disagrees and wants the backend client actually built, that is a materially different (net-new feature) task and should be scoped separately rather than folded into this doc/dead-code cleanup.
