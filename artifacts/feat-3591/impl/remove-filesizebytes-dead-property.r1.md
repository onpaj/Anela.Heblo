# Implementation: remove-filesizebytes-dead-property

## What was implemented
Removed the dead `FileSizeBytes` property from `UploadDocumentRequest` and its corresponding assignment in `KnowledgeBaseController.UploadDocument`. A repo-wide grep confirmed no other code in the KnowledgeBase upload path reads or references this property; the property was purely write-only (set from `file.Length` but never consumed by the handler or anywhere downstream).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs` — removed `public long FileSizeBytes { get; set; }`
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — removed `FileSizeBytes = file.Length,` from the `UploadDocumentRequest` object initializer in the `UploadDocument` action

## Tests
- `dotnet build Anela.Heblo.sln` from repo root — succeeded, 0 errors (250 pre-existing warnings, none related to this change).
- `dotnet format Anela.Heblo.sln --include <the two changed files> --verify-no-changes` — passed with no formatting diffs.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase"` — 231 passed, 15 failed, 246 total. All 15 failures are in `KnowledgeBaseRepositoryIntegrationTests` and fail with `System.ArgumentException: Docker is either not running or misconfigured` (Testcontainers trying to spin up a PostgreSQL container). This is a pre-existing sandbox/environment limitation (no Docker daemon available here) — unrelated to this change. No test references `FileSizeBytes` in the KnowledgeBase feature, and no test failure relates to `UploadDocumentRequest` or `UploadDocument`.

## How to verify
1. `git show 99797d5` (or `git diff` on the two files) to see the two-line removal.
2. `dotnet build Anela.Heblo.sln` from the repo root to confirm compilation.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase"` on a machine with Docker available to get a clean pass including the integration tests.
4. Confirm via `grep -rn FileSizeBytes backend/src/Anela.Heblo.Application/Features/KnowledgeBase backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` that no references remain.

## Notes
- Did not touch `FileSizeBytes` usages in Photobank, Leaflet, or FileStorage — those are separate, unrelated features per the task instructions, and grep confirmed they were left untouched.
- The 15 `KnowledgeBaseRepositoryIntegrationTests` failures are pre-existing infrastructure gaps (no Docker/testcontainers support in this sandbox), not caused by this change. They fail identically on a clean checkout of this branch before these edits (same error, same test names, same count), so this is environmental, not a regression.
- `artifacts/feat-3591/state.json` was modified by the pipeline harness during this session (task status tracking); it was intentionally left unstaged/uncommitted since it's pipeline-managed state, not part of this code change.

## PR Summary
Removes the dead `FileSizeBytes` property from `UploadDocumentRequest` in the KnowledgeBase upload use case, along with its assignment in `KnowledgeBaseController.UploadDocument`. The property was set from `file.Length` on every upload but never read by the handler or any other consumer — a pure two-line dead-code deletion with no behavioral change. Verified via `dotnet build` (0 errors), `dotnet format --verify-no-changes` (clean), and the KnowledgeBase test suite (231/246 passing; the 15 failures are pre-existing Docker-dependent integration tests unrelated to this change).

### Changes
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs` — removed unused `FileSizeBytes` property
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — removed the now-unneeded `FileSizeBytes = file.Length,` assignment in `UploadDocument`

## Status
DONE
