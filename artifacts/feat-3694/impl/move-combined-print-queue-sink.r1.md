# Implementation: move-combined-print-queue-sink

## What was implemented
Relocated `CombinedPrintQueueSink` out of the API host project and into `Anela.Heblo.Adapters.Azure`, matching the placement of the other `IPrintQueueSink` implementations (`AzureBlobPrintQueueSink`, `CupsPrintQueueSink`, `FileSystemPrintQueueSink`). This is a pure move: namespace changed from `Anela.Heblo.API.Features.ExpeditionList` to `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`, and visibility changed from `internal sealed` to `public sealed` (required since `ServiceCollectionExtensions` in the API assembly constructs it directly across the assembly boundary). Constructor, fields, and `SendAsync` body are byte-for-byte unchanged. Updated the one call site and the two existing test files that referenced the old namespace. No `.csproj` changes were needed — both projects already referenced what they needed.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs` — new file (moved from API project), namespace `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`, class now `public sealed`.
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — deleted; the now-empty `ExpeditionList` directory under `API/Features/` was also removed.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed the dead `using Anela.Heblo.API.Features.ExpeditionList;` (verified it was the only reason for that using — no other symbol from that namespace was referenced in the file); changed the `"Combined"` case construction from the fully-qualified `Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(...)` to plain `CombinedPrintQueueSink(...)`, resolved via the pre-existing `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;`. No other line changed.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — updated the `using` from the old API namespace to `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`. No other change.
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — removed the now-redundant `using Anela.Heblo.API.Features.ExpeditionList;` (the type resolves via the existing `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` already present for `AzureBlobPrintQueueSink`). No other change.

## Tests
- Repo-wide grep confirms zero remaining references to `Anela.Heblo.API.Features.ExpeditionList` and that `CombinedPrintQueueSink` now only appears in the new adapter source file, the updated call site, and the two updated test files.
- `dotnet build Anela.Heblo.sln` — succeeds, 0 errors. One pre-existing, unrelated warning (MSB3073 from the `Anela.Heblo.AccessMatrixGen` tool exiting non-zero during the API project's post-build step) was present before this change too and is not caused by this move.
- `dotnet format --verify-no-changes` — passes clean (exit 0), no formatting diffs introduced.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSink"` — 9/9 passed (0 failed), covering both `CombinedPrintQueueSinkTests` and `CombinedPrintQueueSinkRegistrationTests`, including the `FileSystem_ResolvesFileSystemPrintQueueSink` regression guard.
- Full `Anela.Heblo.Tests` suite: 5895 passed, 76 failed, 4 skipped, 5975 total. All 76 failures are pre-existing Postgres-Testcontainers integration tests failing with `System.ArgumentException: Docker is either not running or misconfigured` — this sandbox has no Docker daemon available. None of the failing test classes (`ArticleRepositoryFeedbackProjectionSqlTests`, `BankStatementImportRepositoryIntegrationTests`, `GetStockUpOperationsSummaryIntegrationTests`, `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests`, `LeafletRepositoryIntegrationTests`, `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests`, `MeetingTranscriptRepositorySearchIntegrationTests`, `PhotobankRepositoryGetTagsSqlShapeTests`, `PurchaseOrderRepositoryHistorySqlShapeTests`, `KnowledgeBaseRepositoryIntegrationTests`, `GridLayoutRepositoryUpsertIntegrationTests`, `SmartsuppPresenceRepositoryIntegrationTests`, `SmartsuppRepositoryUpsertIntegrationTests`) is related to ExpeditionList or print queue sinks — unrelated infrastructure/environment limitation, not a regression from this change.

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet format --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSink"
grep -rn "Anela.Heblo.API.Features.ExpeditionList" backend/   # expect no output
grep -rn "CombinedPrintQueueSink" backend/                    # expect only the new source file, the call site, and the two test files
```

## Notes
- The old `backend/src/Anela.Heblo.API/Features/ExpeditionList/` directory contained only this one file and was removed entirely, per the task's Definition of Done.
- No `.csproj` changes were required; the build succeeded without them, confirming the architecture review's assumption that both projects already had the necessary references.
- No behavior change: constructor signature, field names, and `SendAsync` implementation are identical to the original.

## PR Summary
### Changes
- Moved `CombinedPrintQueueSink` from `Anela.Heblo.API/Features/ExpeditionList/` to `Anela.Heblo.Adapters.Azure/Features/ExpeditionList/`, aligning it with the other `IPrintQueueSink` implementations.
- Changed its namespace to `Anela.Heblo.Adapters.Azure.Features.ExpeditionList` and visibility from `internal sealed` to `public sealed` (required for cross-assembly construction from `ServiceCollectionExtensions`).
- Updated `ServiceCollectionExtensions.AddPrintQueueSink` to construct it via the existing Azure-namespace `using`, and removed the now-dead `using Anela.Heblo.API.Features.ExpeditionList;`.
- Updated the two existing test files (`CombinedPrintQueueSinkTests.cs`, `CombinedPrintQueueSinkRegistrationTests.cs`) to reference the new namespace; no test logic changed.
- Removed the now-empty `Anela.Heblo.API/Features/ExpeditionList/` directory.

## Status
DONE
