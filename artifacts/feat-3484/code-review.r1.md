## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

Notes: Verified against the actual worktree (not just the pasted diff text, whose final hunk in the prompt appeared corrupted — `Times.Once):` with a stray colon and a missing class-closing brace). The real file at `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` is syntactically correct; `dotnet build Anela.Heblo.sln` succeeds with 0 errors, and `dotnet test --filter FullyQualifiedName~FileStorage` passes 115/115. The relocation is a faithful, behavior-preserving move: `AzureBlobStorageService` and its `BlobServiceClient`/`IBlobStorageService` registrations move from `FileStorageModule` (Application) to `AzureAdapterModule.AddAzureBlobStorageService` (Azure adapter), mirroring the existing `AddAzurePrintQueueSink` pattern. `Anela.Heblo.Application.csproj` no longer references `Azure.Storage.Blobs`, no remaining `BlobServiceClient`/`Azure.Storage` references exist in the Application project, registration order in `Program.cs` is safe (options validation is deferred to first resolution, not registration time), and there is no double-registration of `IBlobStorageService`. Tests were correctly migrated: the Singleton-lifetime and Development-fallback-warning tests moved from `FileStorageModuleTests` to the new `AzureAdapterModuleTests`, with equivalent coverage.
