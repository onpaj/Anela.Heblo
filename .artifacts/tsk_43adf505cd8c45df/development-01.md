# Development — Remove the phantom backend C# client pipeline

## Summary

Implemented plan-01.md / design-01.md / architecture-01.md as specified, with no deviations. The empty, unreferenced `Anela.Heblo.API.Client` project is deleted, its three GUID references are removed from the `.sln`, and all four documented-but-fictional references to a generated backend C# client (`api-client-generation.md`, `filesystem.md`, `module-map.md`) are corrected. The real, working frontend TypeScript client pipeline is untouched.

## Files changed

- **Deleted** `backend/src/Anela.Heblo.API.Client/Anela.Heblo.API.Client.csproj`
- **Deleted** `backend/src/Anela.Heblo.API.Client/Generated/.gitkeep`
- **`Anela.Heblo.sln`** — removed all three references to GUID `{3D738B41-81F8-4743-8F64-98790902405F}`: the `Project(...)...EndProject` declaration block, the 12-line `ProjectConfigurationPlatforms` entry, and the 1-line `NestedProjects` mapping.
- **`docs/development/api-client-generation.md`** — rewrote the intro/Overview to describe a single (frontend TypeScript) client instead of "two clients"; deleted the entire "Backend C# Client" section (Location/Auto-Generation/Manual Generation/Configuration/Usage Example); corrected the frontend section's "generated in two ways" claim (there is no PostBuild path — only the prebuild script); dropped the "Backend builds (Debug mode)... regenerates both clients" claim from Automatic Regeneration; dropped the `dotnet msbuild ... -t:GenerateBackendClient` line from Manual Regeneration.
- **`docs/architecture/filesystem.md`** — removed the `Anela.Heblo.API.Client/` row from the directory-tree diagram; deleted the `### Backend C# Client` subsection; corrected the Frontend TypeScript Client's "Auto-generation" line (no backend PostBuild path); removed the `Backend Client` row from the Generated Code list.
- **`docs/architecture/module-map.md`** — corrected Part #42's purpose statement ("two generated clients (C# and TypeScript)" → "the generated TypeScript client"); removed `backend/src/Anela.Heblo.API.Client/` from Part #42's Owns list; removed the deleted directory's half of the "deliberately unassigned … generated" note (line ~1110).

## Deviations from design-01.md

None. Every diff in design-01.md's Component design sections 1–5 was applied verbatim; line numbers and surrounding context matched the live tree exactly. Applied the architecture-01.md required fix to the cross-file verification command (widened exclude pattern to also skip the root-level `artifacts/` directory, not just `.artifacts/`).

## Verification performed

1. `grep -c "3D738B41-81F8-4743-8F64-98790902405F" Anela.Heblo.sln` → 0 hits.
2. Per-file targeted greps (all empty, as each design section specified):
   - `grep -n "GenerateBackendClient\|AnelaHebloApiClient\|Backend C# Client\|two clients\|both clients" docs/development/api-client-generation.md`
   - `grep -n "AnelaHebloApiClient\|Anela.Heblo.API.Client" docs/architecture/filesystem.md`
   - `grep -n "Anela.Heblo.API.Client" docs/architecture/module-map.md`
3. Repo-wide cross-file verification (architecture-01.md's corrected command):
   ```bash
   grep -rn "GenerateBackendClient\|swagger2csclient\|AnelaHebloApiClient\|Anela.Heblo.API.Client" \
     --include="*.md" --include="*.sln" --include="*.csproj" --include="*.cs" . \
     | grep -v '^\./\.\?artifacts/\|^\./docs/superpowers/plans/'
   ```
   → empty output (0 hits outside the excluded historical-artifact directories).
4. `dotnet build Anela.Heblo.sln` (from repo root, `~/.dotnet/dotnet` on PATH) → **Build succeeded, 0 Errors, 251 pre-existing warnings** (all in test files, unrelated to this change — confirms the trimmed `.sln` parses and nothing referenced the deleted project).
5. `dotnet format Anela.Heblo.sln --verify-no-changes` → exit code 0, no formatting violations.
6. No FE files were touched, so `npm run build` / `npm run lint` are not implicated; skipped per design-01.md's note that they're unaffected.
7. `git status` confirms exactly the expected file set changed: 2 deletions (project dir), `Anela.Heblo.sln` modified, 3 doc files modified. No stray edits.

## How to verify

```bash
export PATH="$HOME/.dotnet:$PATH"   # dotnet SDK 8.0.423 is here, not on default PATH in this env
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
grep -rn "GenerateBackendClient\|swagger2csclient\|AnelaHebloApiClient\|Anela.Heblo.API.Client" \
  --include="*.md" --include="*.sln" --include="*.csproj" --include="*.cs" . \
  | grep -v '^\./\.\?artifacts/\|^\./docs/superpowers/plans/'
# expect: build succeeds with 0 errors; format reports no changes; grep is empty
```

## Notes

- No tests were added/changed — this task is a project deletion plus documentation correction with no runtime behavior; there is no code surface to unit test. The `dotnet build` + `dotnet format` gates plus the grep verifications are the applicable checks per plan-01.md's acceptance criteria (FR-1 through FR-4) and CLAUDE.md's BE validation gate.
- `dotnet` was not on `PATH` in this environment by default; found at `~/.dotnet/dotnet` (SDK 8.0.423) and added to `PATH` for the verification commands above.
