# Design — Remove the phantom backend C# client pipeline

## Scope check

No user interface is involved. This is a solution-file edit, a project deletion, and four documentation edits. The UX/UI section is omitted.

## Overview

Confirmed against the current tree (2026-07-31, matches plan-01.md):

- `backend/src/Anela.Heblo.API.Client/Anela.Heblo.API.Client.csproj` — bare `net8.0` SDK project, no `PackageReference`, no targets.
- `backend/src/Anela.Heblo.API.Client/Generated/.gitkeep` — the only other file in the project.
- `Anela.Heblo.sln` — references the project in three places (project declaration, `ProjectConfigurationPlatforms`, `NestedProjects`), all keyed on GUID `{3D738B41-81F8-4743-8F64-98790902405F}`.
- Four doc locations describe the fictional pipeline: `docs/development/api-client-generation.md` (primary), `docs/architecture/filesystem.md` (two spots), `docs/architecture/module-map.md` (two spots).

Design decision carried forward from plan-01.md: **delete**, don't build. This document specifies exactly what changes at each site.

## Component design

### 1. `backend/src/Anela.Heblo.API.Client/` — delete entirely

Remove the directory and both files it contains (`Anela.Heblo.API.Client.csproj`, `Generated/.gitkeep`). No other project, `.cs` file, or DI registration references this assembly (verified by repo-wide grep in plan-01.md), so nothing downstream needs updating for the deletion itself.

### 2. `Anela.Heblo.sln` — remove all three references to the deleted project

Three edits, all keyed on GUID `3D738B41-81F8-4743-8F64-98790902405F`:

**a. Project declaration block** (lines 24–25):
```diff
-Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Anela.Heblo.API.Client", "backend\src\Anela.Heblo.API.Client\Anela.Heblo.API.Client.csproj", "{3D738B41-81F8-4743-8F64-98790902405F}"
-EndProject
```
(The `Anela.Heblo.Application` block above and `Anela.Heblo.Persistence` block below stay untouched, so the delete is exactly these two lines.)

**b. `ProjectConfigurationPlatforms` global section** (lines 134–145, 12 lines):
```diff
-		{3D738B41-81F8-4743-8F64-98790902405F}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Debug|Any CPU.Build.0 = Debug|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Debug|x64.ActiveCfg = Debug|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Debug|x64.Build.0 = Debug|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Debug|x86.ActiveCfg = Debug|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Debug|x86.Build.0 = Debug|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Release|Any CPU.ActiveCfg = Release|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Release|Any CPU.Build.0 = Release|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Release|x64.ActiveCfg = Release|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Release|x64.Build.0 = Release|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Release|x86.ActiveCfg = Release|Any CPU
-		{3D738B41-81F8-4743-8F64-98790902405F}.Release|x86.Build.0 = Release|Any CPU
```

**c. `NestedProjects` global section** (line 504, 1 line — the mapping of the project GUID into the `src` solution folder `{87237941-D198-446F-919C-467E4968E85F}`):
```diff
-		{3D738B41-81F8-4743-8F64-98790902405F} = {87237941-D198-446F-919C-467E4968E85F}
```

No other GUID or line in the `.sln` is touched. The `src` solution folder itself (`{87237941-...}`) stays, since other real projects nest under it.

**Verification:** `grep -c "3D738B41-81F8-4743-8F64-98790902405F" Anela.Heblo.sln` → `0`; `dotnet build` from repo root succeeds (confirms MSBuild parses the trimmed `.sln` and no project referenced the deleted one).

### 3. `docs/development/api-client-generation.md` — rewrite to single-client doc

This is the "owned" doc (Part #42) and gets the most extensive edit. Target end-state structure:

```
# OpenAPI Client Generation
## Overview                         (rewritten: one client, not two)
## Frontend TypeScript Client       (unchanged — real pipeline)
## API Endpoint Pattern             (unchanged)
## CRITICAL: URL Construction Rules (unchanged)
## DTO Design Rules                 (unchanged)
## Regeneration Workflow            (edited: drop backend-client lines)
## Troubleshooting                  (unchanged — already frontend-only)
## Additional Resources             (unchanged)
```

Concrete edits:

**a. Line 3** — drop the "both backend (C#) and frontend (TypeScript)" framing:
```diff
-This document describes how OpenAPI clients are generated for both backend (C#) and frontend (TypeScript) in the Anela Heblo project.
+This document describes how the OpenAPI client is generated for the React frontend (TypeScript) in the Anela Heblo project.
```

**b. Lines 9–11** — drop the "two clients" list, replace with single-client statement:
```diff
-**Two clients are generated:**
-1. **Backend C# Client** - For internal backend testing and server-to-server communication
-2. **Frontend TypeScript Client** - For React frontend to communicate with the API
+A single client is generated: the **Frontend TypeScript Client**, used by the React app to communicate with the API.
```

**c. Lines 13–72** — delete the entire "Backend C# Client" section (Location / Auto-Generation / Manual Generation / Configuration / Usage Example), including its closing blank line, so the doc flows directly from the Overview into `## Frontend TypeScript Client`.

**d. Line 81–84** — the frontend section's own "generated in two ways" claim referenced a backend PostBuild event as one of the two ways. Correct to what's real: there is no automatic PostBuild generation at all (the `GenerateFrontendClient` PostBuild target in `Anela.Heblo.API.csproj` exists but is permanently `Condition="false"` — confirmed in plan-01.md's FR-2 note). Only the frontend prebuild script actually runs generation:
```diff
-The TypeScript client is generated in **two ways**:
-
-1. **PostBuild event** in backend API project (Debug mode only)
-2. **Prebuild script** in frontend `package.json` before `npm start` or `npm run build`
+The TypeScript client is generated via a **prebuild script** in frontend `package.json`, which runs before `npm start` or `npm run build`.
```

**e. Lines 378–382 ("Automatic Regeneration")** — drop the false "both clients" claim:
```diff
-**Backend builds (Debug mode):**
-- Building `Anela.Heblo.API` in Debug mode automatically regenerates both clients
-- Check build output for "Generating API clients..." messages
-
 **Frontend builds:**
 - `npm run build` runs prebuild script to regenerate TypeScript client
 - `npm start` runs prebuild script before starting dev server
```

**f. Lines 388–393 ("Manual Regeneration")** — drop the backend-client command line:
```diff
 ```bash
-# Backend C# client
-dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateBackendClient
-
 # Frontend TypeScript client
 dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
 
 # Or from frontend directory
 cd frontend
 npm run generate-client
 ```
```

Everything else in the file (API Endpoint Pattern, URL Construction Rules, DTO Design Rules, Troubleshooting, Additional Resources) is already frontend-only or backend-DTO-only and needs no change.

**Verification:** `grep -n "GenerateBackendClient\|AnelaHebloApiClient\|Backend C# Client\|two clients\|both clients" docs/development/api-client-generation.md` → no output.

### 4. `docs/architecture/filesystem.md` — remove all four references

Plan-01.md's FR-3 named lines 208–215, but the file has two more hits the acceptance grep (`Anela.Heblo.API.Client`, not just `AnelaHebloApiClient`) would also catch:

**a. Line 48** (directory tree diagram) — drop the row:
```diff
-│   │   └── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
```
Line 47 (`PersistenceModule.cs   # DI registration`) becomes the new last child under `Anela.Heblo.Persistence/` in the tree — no reflow needed since tree rows are independent lines.

**b. Lines 210–215** (`### Backend C# Client` subsection) — delete entirely, keeping `### Frontend TypeScript Client` as the sole subsection under `## 🔧 OpenAPI Client Generation`:
```diff
-### Backend C# Client
-- **Location**: `backend/src/Anela.Heblo.API.Client/`
-- **Auto-generation**: PostBuild event in API project (Debug mode only)
-- **Tool**: NSwag with System.Text.Json
-- **Output**: `Generated/AnelaHebloApiClient.cs`
-
 ### Frontend TypeScript Client
```
Also correct line 218's own false claim in the same spirit as the primary doc's edit 3d above (no PostBuild path exists):
```diff
-- **Auto-generation**: Via backend PostBuild event or frontend prebuild script
+- **Auto-generation**: Via frontend prebuild script
```

**c. Line 242** (`### Generated Code` bullet list) — drop the backend-client row, keep the frontend row:
```diff
-- **Backend Client**: `backend/src/Anela.Heblo.API.Client/Generated/`
 - **Frontend Client**: `frontend/src/api/generated/`
```

**Verification:** `grep -n "AnelaHebloApiClient\|Anela.Heblo.API.Client" docs/architecture/filesystem.md` → no output.

### 5. `docs/architecture/module-map.md` — correct Part #42's description and the two stale path references

**a. Line 940** — Part #42's purpose statement still says "two generated clients (C# and TypeScript)"; this doesn't match plan-01.md's grep target verbatim but is the same fiction under new ownership, so leaving it would recreate the doc/repo mismatch this task exists to close:
```diff
-**Purpose:** the OpenAPI contract and the two generated clients (C# and TypeScript), plus the DTO rules that keep
-generation stable.
+**Purpose:** the OpenAPI contract and the generated TypeScript client, plus the DTO rules that keep
+generation stable.
```

**b. Line 944** (Part #42 "Owns" list) — remove the now-nonexistent path:
```diff
 **Owns:**
-- `backend/src/Anela.Heblo.API.Client/`
 - `backend/src/Anela.Heblo.API/nswag-templates/`
 - `frontend/src/api/generated/`, `frontend/src/services/generated/`
 - `scripts/regenerate-api-client.sh`
 - `docs/development/api-client-generation.md`
```

**c. Line 1110** ("Deliberately unassigned … generated" note) — drop the deleted directory's half of the line, keep the frontend half:
```diff
-- `frontend/src/api/generated/`, `backend/src/Anela.Heblo.API.Client/Generated/` — generated (noted in #42)
+- `frontend/src/api/generated/` — generated (noted in #42)
```

**Verification:** `grep -n "Anela.Heblo.API.Client" docs/architecture/module-map.md` → no output.

## Data schemas

Not applicable. No database schema, request/response contract, or event payload changes — this is project deletion plus documentation correction. The real OpenAPI contract and the frontend-generated TypeScript types are untouched.

## Cross-file verification (post-edit)

Single repo-wide sweep to confirm no stray reference survives outside the explicitly out-of-scope historical artifacts (per plan-01.md's Dependencies and scope section):

```bash
grep -rn "GenerateBackendClient\|swagger2csclient\|AnelaHebloApiClient\|Anela.Heblo.API.Client" \
  --include="*.md" --include="*.sln" --include="*.csproj" --include="*.cs" . \
  | grep -v '^\./.artifacts/\|^\./docs/superpowers/plans/'
```
Expected: empty output.

Then the repo-standard gates: `dotnet build`, `dotnet format` (BE-only change; FE build/lint are unaffected since no FE file changes, but running them is a cheap sanity check since the doc still describes the live FE pipeline accurately).
