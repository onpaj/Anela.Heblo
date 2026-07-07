# Code Review: clean-boundary-allowlist-and-verify

## Summary
The task correctly empties `LeafletAllowlist` in `ModuleBoundariesTests.cs` (verified via `git show HEAD` — the diff touches nothing else in the file, no other allowlist affected), and the boundary test suite passes both before and after the edit. The full-suite run does show 149 failures, but every one was independently traced to a pre-existing, environment-level cause (no Docker daemon in this sandbox, or missing live Shoptet credentials) rather than to this feature's changes.

## Review Result: PASS

### task: clean-boundary-allowlist-and-verify
**Status:** PASS

## Independent verification performed
1. **Diff check**: `git show HEAD -- backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — confirms `LeafletAllowlist` reduced to an empty `HashSet<string>` with an updated one-line comment; the 4 removed entries and their justification block match exactly what the task specified for removal; `ArticleAllowlist` and every other allowlist in the file are untouched.
2. **`ModuleBoundariesTests` re-run** (this session, both before and after the allowlist edit): 28/28 passed both times, including the `"Leaflet -> KnowledgeBase"` theory case with the now-empty allowlist.
3. **Grep check**: zero matches anywhere in `backend/src`/`backend/test` for `Anela.Heblo.Application.Features.KnowledgeBase.Services.{IDocumentTextExtractor,IOneDriveService,OneDriveFile,GraphOneDriveService,MockOneDriveService}` — confirms FR-3's "zero references under the old namespace" acceptance criterion. Old files under `Features/KnowledgeBase/Services/` confirmed gone.
4. **`dotnet format`**: ran with no unrelated reformatting (`git diff --stat` after showed only the allowlist file + checkpoint `state.json`).
5. **Final build**: `dotnet build Anela.Heblo.sln` — 0 errors.
6. **Full-suite failure triage** (the critical check): categorized all 149 failures across `Anela.Heblo.Tests`, `Anela.Heblo.Adapters.Flexi.Tests`, and `Anela.Heblo.Adapters.Shoptet.Tests` by exception message and failing namespace:
   - 66 × `System.ArgumentException: Docker is either not running or misconfigured` (Testcontainers/PostgreSQL, no Docker daemon) — spans `KnowledgeBase`/`Leaflet` integration tests (the same 26 already known-failing since task 2's review) plus unrelated modules' Postgres-backed tests (`Bank`, `GridLayouts`, `Photobank`, `MeetingTasks`, `Catalog`, `Article`, `Smartsupp`).
   - 70 × `System.ArgumentNullException`/`FlexiIntegrationTestFixture` fixture errors — Flexi adapter integration tests, same Testcontainers root cause, unrelated to Leaflet/KnowledgeBase.
   - ~13 × `System.InvalidOperationException` (missing `Shoptet:StatusId`, invalid/expired API token, live-environment guard) — Shoptet adapter tests requiring live credentials not present in this sandbox.
   - Manually scanned the full failure list by test name: no failure is in a `KnowledgeBase`, `Leaflet`, or `Shared.Rag` namespace beyond the 26 already-known Docker-dependent ones, and none references `IDocumentTextExtractor`, `IOneDriveService`, `OneDriveFile`, `SharedRagModule`, or any other file this feature touched.

No finding contradicts the impl summary's claims.

## Docs to Update
None — this is an internal test-file cleanup with no public API, CLI, or operational surface change.

## Overall Notes
The `DONE_WITH_CONCERNS` status the developer used is appropriate given the non-zero full-suite failure count, but the concern is fully explained and does not indicate a regression from this task. Recommend the PR description note that E2E/integration Docker-dependent tests cannot run in this sandbox, so CI (which presumably has Docker) is the real gate for those 26 KnowledgeBase/Leaflet-adjacent tests.
