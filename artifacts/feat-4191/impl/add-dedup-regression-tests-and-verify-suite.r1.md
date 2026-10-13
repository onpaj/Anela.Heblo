# Implementation: add-dedup-regression-tests-and-verify-suite

## What was implemented

Added two handler-level regression tests to `CreateMarketingActionHandlerTests`
(`Anela.Heblo.Tests.Application.Marketing` namespace) covering the two dedup
behavior changes introduced by the prior task
(`replace-loops-with-bulk-replace-calls`):

1. `Handle_DedupesProductsCaseInsensitively_WhenDuplicateCodesDifferOnlyByCase`
   — submits `AssociatedProducts = ["abc", "ABC"]` and asserts the handler
   persists exactly one `ProductAssociations` entry with `ProductCodePrefix ==
   "ABC"`, locking in the case-insensitive dedup behavior of
   `ReplaceProductAssociations`.
2. `Handle_PersistsBothFolderLinks_WhenSameFolderKeyButDifferentFolderType`
   — submits two `FolderLinks` entries sharing the same `FolderKey` but
   different `FolderType` and asserts both are persisted (composite-key
   dedup via `ReplaceFolderLinks`, not single-key).

Both tests were inserted verbatim as specified in the task context, between
`Handle_PersistsFolderLinks_WhenProvided` and
`Handle_HonorsRuntimePushEnabledFlip_TrueToFalse`. No other test in the file
was modified.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — added the two `[Fact]` test methods described above.

## Tests

- `Handle_DedupesProductsCaseInsensitively_WhenDuplicateCodesDifferOnlyByCase` (new)
- `Handle_PersistsBothFolderLinks_WhenSameFolderKeyButDifferentFolderType` (new)

## How to verify

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateMarketingActionHandlerTests" --no-build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests" --no-build
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
```

Results:
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`: `Build succeeded`, 0 errors.
- `CreateMarketingActionHandlerTests` filter: 14 passed, 0 failed.
- `UpdateMarketingActionHandlerTests` filter: 13 passed, 0 failed.
- `dotnet format` (scoped to `CreateMarketingActionHandler.cs` and `CreateMarketingActionHandlerTests.cs`): no changes (already conformant).
- `dotnet build Anela.Heblo.sln`: `Build succeeded`, 0 errors.
- `dotnet test Anela.Heblo.sln` (full suite, `--no-build`): 7151+34+28+85+270+6+16+14 = passed, 110 failed, 4/6 skipped.
  All 110 failures are pre-existing, environment-only failures unrelated to
  this change or to the Marketing module — confirmed by inspecting every
  failure's error message:
  - `Anela.Heblo.Tests.dll`: all failures are integration tests requiring a
    live Postgres via Testcontainers (`Docker is either not running or
    misconfigured`) — no Docker daemon is available in this sandbox.
  - `Anela.Heblo.Adapters.Shoptet.Tests.dll`: integration tests gated on
    `Shoptet:IsTestEnvironment=true` / live credentials
    (`Integration test must not run against live environment`).
  - `Anela.Heblo.Adapters.Flexi.Tests.dll`: integration tests missing a
    `FlexiIntegrationTestFixture` (live API credentials not configured in
    this sandbox).
  No failure touches `Marketing`, `CreateMarketingActionHandler`, or
  `UpdateMarketingActionHandler` — grepped the full failure list for
  "marketing" (case-insensitive): zero matches.

## Notes

No deviations from the task context. No new `using` directives or
dependencies were required — `System.Linq.Single()` was already reachable
the same way the existing test uses it.

## PR Summary
Added the two handler-level regression tests arch-review's Specification
Amendments called for, locking in the case-insensitive product dedup and
same-key-different-type folder-link dedup behavior introduced by the prior
task's switch to `ReplaceProductAssociations`/`ReplaceFolderLinks`. Ran the
full backend validation gate (build, targeted test filters, `dotnet format`,
full-solution build, full test suite) — all Marketing-related and previously
passing tests remain green; the only failures are pre-existing
environment-only integration tests (no Docker / no live credentials in this
sandbox), unrelated to this change.

### Changes
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — added two regression tests for the dedup behavior changes.

## Status
DONE
