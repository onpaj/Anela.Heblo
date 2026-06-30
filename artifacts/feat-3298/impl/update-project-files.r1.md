# Implementation: update-project-files

## What was implemented

### Step 1 — InternalsVisibleTo verification
`Anela.Heblo.Adapters.Microsoft365.csproj` confirmed to contain `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` (line 20). No action needed.

### Step 2 — PackageReference check
`Anela.Heblo.Application.csproj` currently contains both:
- `<PackageReference Include="Microsoft.Graph" Version="5.92.0" />`
- `<PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />`

### Step 3 — Safety check (BLOCKING FINDING)
A `grep` across `backend/src/Anela.Heblo.Application/` revealed **6 files** still importing `Microsoft.Graph` or `Microsoft.Identity`:

| File | Usages |
|------|--------|
| `Features/MeetingTasks/Services/GraphPlannerService.cs` | `Microsoft.Identity.Client`, `Microsoft.Identity.Web` |
| `Features/KnowledgeBase/KnowledgeBaseModule.cs` | `Microsoft.Identity.Web` |
| `Features/KnowledgeBase/Services/GraphOneDriveService.cs` | `Microsoft.Identity.Web` |
| `Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` | `Microsoft.Identity.Client`, `Microsoft.Identity.Web` |
| `Features/Article/Admin/BackfillArticleRequestedByHandler.cs` | `Microsoft.Identity.Client`, `Microsoft.Graph.Models.ODataErrors.ODataError` |
| `Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` | `Microsoft.Identity.Client`, `Microsoft.Graph.Models.ODataErrors.ODataError` |

These services have **not yet been migrated** to the `Anela.Heblo.Adapters.Microsoft365` project. Removing the PackageReferences now would break the build.

### Step 4 — No changes made to Application.csproj
The PackageReferences were **not removed** because the safety check (Step 3) showed active consumers still in the Application layer.

### Step 5 — Build verification
`dotnet build Anela.Heblo.sln` completed with **0 errors** (254 pre-existing warnings). The solution file is at the repo root, not `backend/backend.sln`.

### Step 6 — No commit
No files were modified, so no commit was made.

## Files created/modified
- `artifacts/feat-3298/impl/update-project-files.r1.md` — this report

## Tests
N/A — no project file changes were made

## How to verify
```bash
# Confirm InternalsVisibleTo is present
grep "InternalsVisibleTo" backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj

# Confirm remaining consumers in Application layer
grep -r "Microsoft\.Graph\|Microsoft\.Identity" backend/src/Anela.Heblo.Application/ --include="*.cs" -l

# Confirm build still passes
dotnet build Anela.Heblo.sln
```

## Notes

**The PackageReferences cannot be removed yet.** Six files in the Application layer still consume `Microsoft.Graph` / `Microsoft.Identity.*`. These services need to be migrated to `Anela.Heblo.Adapters.Microsoft365` first (or their Microsoft.Graph dependencies removed) before the PackageReferences can be dropped from `Anela.Heblo.Application.csproj`.

The remaining files to migrate are:
- `GraphPlannerService.cs` (MeetingTasks)
- `KnowledgeBaseModule.cs` + `GraphOneDriveService.cs` (KnowledgeBase)
- `GraphCatalogDocumentsStorage.cs` (CatalogDocuments)
- `BackfillArticleRequestedByHandler.cs` (Article/Admin)
- `GetGroupMembersHandler.cs` (UserManagement) — this one may already be in scope for feat-3298

The solution file is at the worktree root (`Anela.Heblo.sln`), not at `backend/backend.sln` as the task spec assumed.

## PR Summary

No code changes were made in this task. The prior task's `InternalsVisibleTo` addition was verified as present. The PackageReference removal was correctly blocked by the safety check: six Application-layer files still depend on `Microsoft.Graph` and `Microsoft.Identity.*`. These must be migrated first.

### Changes
- No source files modified
- `artifacts/feat-3298/impl/update-project-files.r1.md` — added (this report)

## Status
DONE (with deviation: PackageReferences not removed — consumers still present in Application layer)
