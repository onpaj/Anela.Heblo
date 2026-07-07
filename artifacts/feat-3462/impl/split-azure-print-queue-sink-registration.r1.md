# Implementation: split-azure-print-queue-sink-registration

## What was implemented
Split `AzureAdapterModule.AddAzurePrintQueueSink` into two methods: a new
`AddAzurePrintQueueSinkInfrastructure` that registers only the Azure Blob
infrastructure (`BlobContainerClient` factory + `AzureBlobPrintQueueSink` as a
concrete singleton), and `AddAzurePrintQueueSink` (unchanged public contract),
which now calls the infrastructure method and additionally binds the
non-keyed `IPrintQueueSink -> AzureBlobPrintQueueSink` singleton via a factory
that resolves the shared concrete instance.

The `"Combined"` case in `ServiceCollectionExtensions.AddPrintQueueSink` now
calls `AddAzurePrintQueueSinkInfrastructure` (no non-keyed `IPrintQueueSink`
side effect) instead of `AddAzurePrintQueueSink`, removing the phantom
singleton. Its keyed `"azure"` slot is now a singleton factory that resolves
the same shared `AzureBlobPrintQueueSink` instance registered by the
infrastructure method (previously `AddKeyedScoped<IPrintQueueSink,
AzureBlobPrintQueueSink>("azure")`, which constructed a second instance). The
stale workaround comment on the `"Combined"` case was removed since the
underlying bug it referenced no longer exists.

A pre-existing, unrelated compile break in
`GetConfigurationHandlerTests.cs` (a stale `ConfigurationConstants.APP_VERSION`
reference left over from PR #3435, which moved that constant to
`InfrastructureConfigurationKeys.APP_VERSION`) was also fixed as a separate
commit, since it prevented the test project from building at all and blocked
verification of this task.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — split into `AddAzurePrintQueueSinkInfrastructure` + `AddAzurePrintQueueSink`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `"Combined"` case now calls the infrastructure-only method and uses a keyed singleton factory for the `"azure"` slot
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — added regression test
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — unrelated pre-existing compile-break fix (stale constant reference)

## Tests
- `CombinedPrintQueueSinkRegistrationTests.Combined_NonKeyedIPrintQueueSink_HasExactlyOneRegistration_AndItIsCombined` (new) — asserts `GetServices<IPrintQueueSink>()` in `"Combined"` mode returns exactly one item (`CombinedPrintQueueSink`), proving the phantom `AzureBlobPrintQueueSink` singleton no longer leaks into the enumerable resolution. Confirmed this test fails against the pre-fix code (`Assert.Single` throws because 2 items are present) and passes after the fix.
- All 5 tests in `CombinedPrintQueueSinkRegistrationTests` pass after the fix.

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSinkRegistrationTests"
```
Expected: build succeeds, 5/5 tests pass.

Full suite: `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 5415 passed, 64 failed (all pre-existing `Docker is either not running or misconfigured` Testcontainers integration-test failures, unrelated to this change; none touch `PrintQueueSink`, `ExpeditionList`, `AzureAdapterModule`, or `Configuration`), 4 skipped.

## Notes
- `dotnet format --verify-no-changes` (scoped to the changed files) reported no diffs.
- The `"AzureBlob"`, `"Cups"`, and `default` cases in `ServiceCollectionExtensions.AddPrintQueueSink` were left untouched, per the task plan.
- The unrelated `GetConfigurationHandlerTests.cs` fix is out of scope for issue #3462 but was necessary to unblock any test run in this worktree — flagged clearly in its own commit message.

## PR Summary
Fixes a DI registration bug where `AzureAdapterModule.AddAzurePrintQueueSink` bundled two concerns (Azure Blob infrastructure setup and the non-keyed `IPrintQueueSink` binding) into one method. The `"Combined"` print-sink mode called this method purely for infrastructure setup but inherited an unwanted non-keyed singleton registration for `AzureBlobPrintQueueSink` alongside its own `CombinedPrintQueueSink` registration — a latent bug for any `IEnumerable<IPrintQueueSink>` / `GetServices<IPrintQueueSink>()` consumer, which would see both sinks instead of one.

The fix splits the method into `AddAzurePrintQueueSinkInfrastructure` (Blob client + concrete `AzureBlobPrintQueueSink` singleton, no `IPrintQueueSink` binding) and `AddAzurePrintQueueSink` (infrastructure + the non-keyed binding, used only by the `"AzureBlob"` mode). The `"Combined"` case now calls only the infrastructure method and registers its own keyed singleton binding that shares the same underlying instance. A regression test proves `GetServices<IPrintQueueSink>()` now returns exactly one item in `"Combined"` mode.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — split `AddAzurePrintQueueSink` into infrastructure + full registration
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `"Combined"` case uses infrastructure-only registration + keyed singleton factory
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — new regression test
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — unrelated pre-existing compile-break fix (stale `ConfigurationConstants.APP_VERSION` reference)

## Status
DONE
