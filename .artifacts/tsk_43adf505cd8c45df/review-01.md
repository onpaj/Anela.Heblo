# Review — Remove the phantom backend C# client pipeline

## Verdict: done

## What was checked

Independently re-verified the diff in commit `8e62825d` against the task's requirements (arch-review issue), plan-01.md, design-01.md, and architecture-01.md — not just re-read the development log's claims.

- **`Anela.Heblo.API.Client` deletion**: confirmed `backend/src/Anela.Heblo.API.Client/` no longer exists (`ls backend/src/` shows no client dir). Both files (`.csproj`, `Generated/.gitkeep`) removed, matching design-01.md section 1.
- **`Anela.Heblo.sln`**: confirmed all three references to GUID `3D738B41-81F8-4743-8F64-98790902405F` are gone (`grep -c` → 0): the `Project(...)...EndProject` block, all 12 `ProjectConfigurationPlatforms` lines, and the `NestedProjects` mapping. Diff matches design-01.md section 2 exactly, line for line. No other project/GUID touched.
- **Doc corrections**: read the full diffs for `docs/development/api-client-generation.md`, `docs/architecture/filesystem.md`, `docs/architecture/module-map.md`. All phantom backend-C#-client claims (the "two clients" framing, the `GenerateApiClient` PostBuild target snippet, `dotnet msbuild ... -t:GenerateBackendClient`, the `AnelaHebloApiClient` usage example, the `### Backend C# Client` doc subsections, the Part #42 "two generated clients (C# and TypeScript)" purpose statement and owned-path entry) are removed. The real frontend TypeScript pipeline (`GenerateFrontendClientManual`, prebuild script, `nswag-templates/`) is left untouched, as required.
- **Cross-file verification**: reran the corrected repo-wide grep (with the `artifacts/`-vs-`.artifacts/` exclusion fix that architecture-01.md required) myself — 0 hits outside excluded historical-artifact directories. Matches the claim in development-01.md.
- **Build gate**: ran `dotnet build Anela.Heblo.sln` myself (fresh, not trusting the log) — **Build succeeded, 0 Errors, 251 warnings** (all pre-existing, in test files, unrelated to this change — confirms the trimmed `.sln` still parses and nothing referenced the deleted project).
- **Format gate**: ran `dotnet format Anela.Heblo.sln --verify-no-changes` myself — **exit code 0**, no violations reported.
- **Scope**: `git show --stat` confirms only the expected files changed (2 deletions, `.sln` modified, 3 docs modified, plus the task's own artifact file) — no stray edits, no unrelated refactoring, consistent with CLAUDE.md's "surgical changes" rule.

## Assessment against the original issue

The issue's two acceptable resolutions were: restore the pipeline, or abandon it and align docs+repo. The team chose abandon (correctly reasoned in architecture-01.md: two years of zero real usage, no genuine server-to-server caller, the mechanism was seemingly never built rather than having decayed — building it now would be exactly the speculative infrastructure CLAUDE.md warns against). All four evidence points from the issue are now resolved:
- `GenerateBackendClient`/`swagger2csclient`/`AnelaHebloApiClient` — 0 hits repo-wide (verified).
- `Anela.Heblo.API.Client.csproj` empty project — deleted.
- `Generated/.gitkeep` placeholder — deleted along with the project.
- `Anela.Heblo.sln:24` reference — removed, along with the two other GUID sites the issue didn't explicitly call out but which the design correctly found and fixed too.

No functional requirement is unmet, no architecture conflict, no missing required tests (none applicable — this is a pure deletion/doc-correction with no runtime behavior surface, correctly noted in development-01.md), and no correctness bugs found. Every pipeline step (plan → design → architecture → development) tracked the same scope with no drift, and the one required fix from architecture review (the `artifacts/` exclusion pattern) was correctly folded into the implementation.

## Outcome

```json
{"outcome": "done", "summary": "Independently verified: Anela.Heblo.API.Client project and its 3 .sln GUID references are fully removed, all phantom backend-C#-client doc claims are corrected in the 3 target docs, repo-wide grep confirms 0 stray references, dotnet build succeeds (0 errors), and dotnet format --verify-no-changes passes. Diff matches design-01.md/architecture-01.md exactly with no scope creep. No functional gaps or correctness issues found."}
```
