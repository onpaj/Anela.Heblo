# Code Review: relocate-service-and-rewire-di (feat-3484)

## Summary
The implementation matches the task spec precisely: `AzureBlobStorageService` was moved with `git mv`
(confirmed as a 99%-similarity rename in the commit, preserving history) into
`Anela.Heblo.Adapters.Azure.Features.FileStorage`, only the namespace and one `using` line changed in
the moved file, and the new `AddAzureBlobStorageService(IServiceCollection, IHostEnvironment)`
extension on `AzureAdapterModule` reproduces the `BlobServiceClient` factory and `IBlobStorageService`
binding verbatim. `FileStorageModule` now matches the target content in the task context byte-for-byte.
I independently ran the production build and it succeeds with 0 errors.

## Review Result: PASS

### task: relocate-service-and-rewire-di
**Status:** PASS

Verification performed directly against the worktree (not just the developer's summary):
- Moved file exists at `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` with namespace `Anela.Heblo.Adapters.Azure.Features.FileStorage`; old path no longer exists.
- `git log --follow` / `git show -M --summary` on the implementation commit confirm a detected rename (99% similarity), preserving history as required — only 3 lines changed (namespace + new `using Anela.Heblo.Application.Features.FileStorage;`), no method bodies touched.
- `AzureAdapterModule.cs` contains the new `AddAzureBlobStorageService` extension with the `BlobServiceClient` factory moved verbatim (including the exact fallback-warning message) and the `IBlobStorageService` singleton binding, matching the task's target code exactly.
- `FileStorageModule.cs` matches the task's target content exactly: Azure `using`s and registrations removed, `Microsoft.Extensions.Options`/`Microsoft.Extensions.Logging` retained as instructed.
- `Program.cs` has exactly one `AddAzureBlobStorageService(builder.Environment)` call, immediately after `AddApplicationServices` and before the `ISmartsuppWebhookMetrics` registration — correctly placed outside the print-sink switch and outside `ApplicationModule`.
- `Anela.Heblo.Application.csproj` no longer references `Azure.Storage.Blobs`; `Anela.Heblo.Adapters.Azure.csproj` still does (one match, version 12.25.0).
- `grep -rn "Azure.Storage\|BlobServiceClient\|BlobContainerClient" backend/src/Anela.Heblo.Application` returns empty — no stray Azure SDK references remain in the Application layer.
- Ran `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` myself: **Build succeeded, 0 Error(s)** (160 pre-existing warnings unrelated to this change, plus the known non-fatal `AccessMatrixGen` post-build tool exit code 134, both pre-existing and orthogonal to this task).
- As instructed, the test project's expected non-compilation (due to the old namespace reference, to be fixed by task 2) was not flagged as an issue here — out of scope for this task.

No functional, architectural, or correctness issues found.

## Docs to Update
None.

## Overall Notes
Clean, surgical move with correct DI wiring and zero behavior change. The developer's implementation
summary was accurate in every claim checked against the actual file contents and a live build.
