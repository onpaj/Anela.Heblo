# Implementation: clean-boundary-allowlist-and-verify

## What was implemented
Removed the four resolved `LeafletAllowlist` entries (and their justification comments) from `ModuleBoundariesTests.cs`, verified the `"Leaflet -> KnowledgeBase"` boundary rule passes both before and after the edit, confirmed zero remaining references to the relocated types under the old namespace, ran `dotnet format` across the whole solution, and ran a final full build + full backend test suite.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — `LeafletAllowlist` reduced from 4 justified entries + comment block to an empty `HashSet<string>` with a one-line "resolved" comment. No other allowlist in the file touched.

## Tests
1. **Baseline** — `ModuleBoundariesTests` before the edit: **Passed! Failed: 0, Passed: 28, Skipped: 0, Total: 28**
2. **After emptying the allowlist** — `ModuleBoundariesTests`: **Passed! Failed: 0, Passed: 28, Skipped: 0, Total: 28** (including `"Leaflet -> KnowledgeBase"` with the now-empty allowlist)
3. **Grep verification** (FR-3 acceptance criterion) — zero matches for any `Anela.Heblo.Application.Features.KnowledgeBase.Services.{IDocumentTextExtractor,IOneDriveService,OneDriveFile,GraphOneDriveService,MockOneDriveService}` reference anywhere in `backend/src`/`backend/test`; old files (`DocumentExtractors/`, `GraphOneDriveService.cs`, `MockOneDriveService.cs`, `GraphFolderResolver.cs`, `GraphApiHelpers.cs`, `IDocumentTextExtractor.cs`, `IOneDriveService.cs`) confirmed gone from `Features/KnowledgeBase/Services/`.
4. **`dotnet format Anela.Heblo.sln`** — ran clean; `git diff --stat` showed no changes beyond the allowlist edit itself and the checkpoint `state.json` — no unrelated reformatting to revert.
5. **Final full build** — `dotnet build Anela.Heblo.sln`: **Build succeeded, 0 errors** (13 pre-existing warnings, unrelated).
6. **Full backend test suite** — `dotnet test Anela.Heblo.sln`: 149 failed total across all test projects (Flexi.Tests, Shoptet.Tests, Anela.Heblo.Tests), but every single failure falls into one of three pre-existing, environment-level categories confirmed by exception type and test name, none touching `KnowledgeBase`/`Leaflet`/`Shared.Rag`:
   - `System.ArgumentException: Docker is either not running or misconfigured` (Testcontainers/PostgreSQL — no Docker daemon in this sandbox) — includes the same 26 `KnowledgeBase`/`Leaflet` integration tests already known-failing before this task (task 2's review), plus other modules' Postgres-backed integration tests (Bank, GridLayouts, Photobank, MeetingTasks, Catalog, Article, Smartsupp) that never touched the relocated types.
   - `System.ArgumentNullException`/`FlexiIntegrationTestFixture` fixture errors (Flexi adapter integration tests — same Testcontainers root cause).
   - `System.InvalidOperationException: Missing Shoptet:StatusId... / Shoptet API token is invalid or expired / Integration test must not run against live environment` (Shoptet adapter tests requiring live/test-environment credentials not present in this sandbox).

   No failure references `IDocumentTextExtractor`, `IOneDriveService`, `OneDriveFile`, `Shared.Rag`, or any file touched by this feature.

## How to verify
1. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"` — 28/28 pass.
2. `grep -rn "Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor\|...IOneDriveService..." backend/src backend/test --include=*.cs` — no output.
3. `dotnet build Anela.Heblo.sln` — 0 errors.

## Notes
The full-suite failure count (149) is higher than a first glance at NFR-2 ("0 failed tests") might suggest, but every failure is a pre-existing, Docker/credential-gated integration test unrelated to this refactor — confirmed by exception type and namespace, not just assumed. This sandbox has no Docker daemon and no live Shoptet test credentials, so these tests cannot pass in this environment regardless of this change; they are not part of this task's diff surface.

## Status
DONE_WITH_CONCERNS
