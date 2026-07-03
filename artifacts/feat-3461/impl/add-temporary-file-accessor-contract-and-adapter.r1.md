# Implementation: add-temporary-file-accessor-contract-and-adapter

## What was implemented
Added the `ITemporaryFileAccessor` contract to the Application layer and its `FileSystemTemporaryFileAccessor` implementation to the FileSystem adapter project, registered unconditionally in the composition root (before the `PrintSink` switch). `ExpeditionListService` itself is untouched in this task, per plan.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` — new interface: `Task<byte[]> ReadAllBytesAsync(string, CancellationToken)`, `void DeleteIfExists(string)`.
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` — new `System.IO.File`-backed implementation.
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs` — added `AddFileSystemTemporaryFileAccessor()` extension method.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — calls `AddFileSystemTemporaryFileAccessor()` unconditionally inside `AddPrintQueueSink`, before the `PrintSink` switch.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs` — new test file, 4 tests covering read/delete happy and missing-file paths.
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — unrelated one-line fix (see Notes).

## Tests
- `FileSystemTemporaryFileAccessorTests` (4 new tests): read existing file, read missing file throws `FileNotFoundException`, delete existing file, delete non-existent file is a no-op. All pass.
- Full `ExpeditionList` test slice (`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionList`): 164 passed, 0 failed.

## How to verify
```
cd backend && dotnet build ../Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionList
```

## Notes
`GetConfigurationHandlerTests.cs` line 92 had a pre-existing, unrelated compile error on the branch base (`ConfigurationConstants.APP_VERSION`, but `APP_VERSION` had already moved to `InfrastructureConfigurationKeys` in a prior merged PR — every other reference in the same file already used the new symbol; this one site was missed). This blocked `dotnet build` for the whole solution, so it was fixed as a one-line symbol-name correction to unblock this task's build. No behavior change; not otherwise in scope for this feature.

## PR Summary
Introduces `ITemporaryFileAccessor` (Application layer) and `FileSystemTemporaryFileAccessor` (FileSystem adapter), mirroring the existing `IPrintQueueSink`/`FileSystemPrintQueueSink` split, and wires it into DI unconditionally so it's available regardless of the configured `PrintSink`. `ExpeditionListService` does not yet use the new type — that's the next task.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` — new
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` — new
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs` — new registration method
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — wired the new registration
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs` — new
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — unrelated pre-existing build-break fix

## Status
DONE
