# Code Review: relocate-onedrive-services

## Summary
The implementation moves `IOneDriveService`/`OneDriveFile` to `Shared.Rag`, and `GraphOneDriveService`, `MockOneDriveService`, `GraphFolderResolver`, and the renamed `GraphDriveModels.cs` (formerly `GraphApiHelpers.cs`) to `Shared.Rag.OneDrive`, updating every consumer exactly as the spec prescribed. Verified against the actual `HEAD` commit diff, file system state, a clean `dotnet build` (0 errors), and a full `dotnet test --no-build` run targeting the affected classes plus `ModuleBoundariesTests` (51/51 passed).

## Review Result: PASS

### task: relocate-onedrive-services
**Status:** PASS

## Overall Notes
- All 14 spec steps verified directly against the repo: `git mv`-based renames preserved (confirmed via `git show -1 -p` rename detection), namespaces changed exactly as specified, and no unrelated lines touched.
- `IOneDriveService`/`OneDriveFile` correctly moved to `Shared.Rag` as a `record` (per the spec's explicit carve-out from the "DTOs are classes" CLAUDE.md rule — this is an internal Application-layer service type, not an OpenAPI-serialized contract).
- `GraphFolderResolver` remains `internal`; `Common/Graph/GraphApiHelpers.cs` (the real, unrelated Graph helper) is untouched and still referenced via `using Anela.Heblo.Application.Common.Graph;` in `GraphOneDriveService.cs`/`GraphFolderResolver.cs` — confirmed by direct file inspection.
- `GraphDriveModels.cs` content matches the spec's expected file body exactly (byte-for-byte comparison of the three internal classes).
- `KnowledgeBaseModule.cs` DI registration block was correctly left untouched — only the new `using Anela.Heblo.Application.Shared.Rag.OneDrive;` was added, matching the "registration ownership moves in the next task" instruction.
- `LeafletIngestionJobTests.cs` deviates slightly from the spec's literal instruction ("change using to Shared.Rag") — the dev instead deleted the old `Features.KnowledgeBase.Services` using line outright, since `using Anela.Heblo.Application.Shared.Rag;` was already present in that file (added by the prior `relocate-document-extractors` task). This achieves the same net result the spec intended (namespace resolves via the pre-existing `Shared.Rag` using) without a redundant duplicate using statement — a reasonable, correct judgment call, not a defect.
- The old test directory `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/` no longer contains `GraphOneDriveServiceTests.cs`; the file now lives at `backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/GraphOneDriveServiceTests.cs` with correct namespace/using.
- `ModuleBoundariesTests.cs` still contains two allowlist entries referencing the old `Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService`/`.OneDriveFile` paths. These are now stale/unused (the types no longer live there), but this is explicitly out of scope per the task list — cleanup is task 4 (`clean-boundary-allowlist-and-verify`) — and their presence does not cause `ModuleBoundariesTests` to fail (verified: it passes).
- Build verification: `dotnet build Anela.Heblo.sln` — Build succeeded, 0 errors (254 pre-existing warnings unrelated to this change).
- Test verification: `dotnet test ... --no-build --filter "FullyQualifiedName~Shared.Rag.OneDrive|...KnowledgeBaseIngestionJobTests|...KnowledgeBaseArticleStyleGuideSourceTests|...LeafletIngestionJobTests|...ModuleBoundariesTests"` — Passed: 51, Failed: 0, Skipped: 0.
- Noted but irrelevant to this review: a pre-existing, unrelated MSBuild target (`GenerateAccessMatrix` in `Anela.Heblo.API.csproj`, `BeforeTargets="Build"`, `ContinueOnError="true"`) throws an unhandled `JsonException` in the `Anela.Heblo.AccessMatrixGen` tool during Debug builds (its `Exec` command's argument order doesn't match `Program.cs`'s expected `args[0]`=manifest path). This is swallowed by `ContinueOnError="true"` and does not fail the build or tests; it predates this task and is out of scope here, but may be worth a follow-up issue.
