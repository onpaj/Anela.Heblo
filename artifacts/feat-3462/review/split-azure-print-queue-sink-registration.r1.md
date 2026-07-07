# Code Review: Split AddAzurePrintQueueSink Phantom Singleton Registration

## Summary
The actual code changes (`AzureAdapterModule.cs` split into `AddAzurePrintQueueSinkInfrastructure` + `AddAzurePrintQueueSink`, and the `"Combined"` case in `ServiceCollectionExtensions.cs`) match the spec and arch-review's prescribed final shape line-for-line, and the new regression test's content correctly proves the fix. However, the regression test file was never committed — it exists only as an uncommitted working-tree modification — so the delivered commit history is missing a required test (FR-4) and does not match the task's own step-by-step commit plan.

## Review Result: REVISION_NEEDED

### task: split-azure-print-queue-sink-registration
**Status:** REVISION_NEEDED
**Issues:**
- The new regression test `Combined_NonKeyedIPrintQueueSink_HasExactlyOneRegistration_AndItIsCombined` in `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` (task step 1, spec FR-4) is **not committed** to git. `git status` in the worktree shows it as an uncommitted modification (`M backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`), and `git log --oneline -6` shows only two task-relevant commits: `0e50b75` (the unrelated pre-existing test-project build fix) and `39720bb` ("fix: split AddAzurePrintQueueSink..."), the latter touching only `AzureAdapterModule.cs` and `ServiceCollectionExtensions.cs` (`git show --stat 39720bb`). The task plan's steps 3 ("commit the failing test on its own"), 12 (commit the implementation), and 13 ("confirm the working tree is clean") were not followed — the test change was left uncommitted. If this branch is merged/PR'd from its commit history, the regression test proving the phantom-singleton bug is fixed will not ship at all, silently reverting FR-4.

## Docs to Update
(none)

## Overall Notes
- Verified by direct file read: the committed diff of `AzureAdapterModule.cs` (`git show 39720bb`) matches the spec's and arch-review's prescribed code exactly — `AddAzurePrintQueueSinkInfrastructure` registers `BlobContainerClient` + `AzureBlobPrintQueueSink` as a concrete singleton with no `IPrintQueueSink` binding, and `AddAzurePrintQueueSink` layers the non-keyed singleton factory on top, preserving the public contract (NFR-3).
- The `"Combined"` case in `ServiceCollectionExtensions.cs` now calls `AddAzurePrintQueueSinkInfrastructure`, uses `AddKeyedSingleton<IPrintQueueSink>("azure", (provider, _) => provider.GetRequiredService<AzureBlobPrintQueueSink>())` to share the one instance, and the stale workaround comment was removed — matching the arch-review's Decision 1 and the spec's final code sketch verbatim. The `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` needed for the unqualified `AzureBlobPrintQueueSink` reference was already present.
- `AzureBlobPrintQueueSink`'s constructor dependencies (`BlobContainerClient`, `TimeProvider`, `ILogger<AzureBlobPrintQueueSink>`) are all singleton-safe, confirming the singleton promotion decision was sound.
- The `"AzureBlob"`, `"Cups"`, and `default`/`"FileSystem"` cases were left untouched, as required.
- The regression test's own content (read from the working tree) is correct and matches the spec's prescribed test verbatim: it asserts `GetServices<IPrintQueueSink>()` in `"Combined"` mode yields exactly one `CombinedPrintQueueSink` instance.
- Only two files show as modified/uncommitted in the worktree: `artifacts/feat-3462/state.json` (pipeline bookkeeping, still shows `"developing"`/task status as `"in_progress"` rather than done — consistent with the missing final commit) and the test file noted above.
- Per the task's own instructions, the unrelated pre-existing fix to `GetConfigurationHandlerTests.cs` (stale `ConfigurationConstants.APP_VERSION` reference, committed separately as `0e50b75`) is out of scope and not penalized — it was necessary to unblock the test project's build.
- Fix is mechanical: `git add backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs && git commit` (ideally split into the pre-fix failing-test commit and confirming it against the already-applied implementation commit, per the original task plan, though a single follow-up commit adding the test is also acceptable since the implementation commit already exists).
