# Implementation: extend-temporary-file-accessor

## What was implemented
Extended `ITemporaryFileAccessor` with a new `CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default)` method, implemented it in `FileSystemTemporaryFileAccessor` (writes the stream to a GUID-named temp file, deleting any partial file and rethrowing on failure), and resolved `ITemporaryFileAccessor` in the `ExpeditionListArchiveModule` DI factory (not yet passed into the handler constructor — that lands in `task: refactor-reprint-handler-and-tests`, which changes the constructor signature). Added adapter-level tests for the new method.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` — added `CreateFromStreamAsync` to the interface
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` — implemented `CreateFromStreamAsync`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — resolves `ITemporaryFileAccessor` from the provider in the handler factory (constructor wiring completed by task 2)
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs` — 3 new tests: valid stream round-trips content and extension, extension is honored, a throwing source stream leaves no partial file and propagates the exception

## Tests
`FileSystemTemporaryFileAccessorTests.cs` — 7/7 pass (4 pre-existing + 3 new), verified with:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileSystemTemporaryFileAccessorTests"
```
Full backend solution builds cleanly (`dotnet build`, 0 errors). Full test suite run: 5641 passed / 65 failed (pre-existing, all Docker/Testcontainers-dependent integration tests unrelated to this change — this sandbox has no Docker daemon) / 4 skipped.

## How to verify
1. `dotnet build Anela.Heblo.sln` from repo root — succeeds.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileSystemTemporaryFileAccessorTests"` — 7 pass.
3. Confirm `ReprintExpeditionListHandler` and its tests are untouched by this task (verified by `task: refactor-reprint-handler-and-tests`).

## Notes
Per the task's sequencing note, `ExpeditionListArchiveModule.cs`'s factory resolves `ITemporaryFileAccessor` but does not yet pass it to `new ReprintExpeditionListHandler(...)` (still a 3-arg call), since the handler's constructor still has the old 3-arg signature. Task 2 adds the 4th constructor parameter and updates this call site to pass the resolved accessor — left an inline comment pointing at that task so the intent is clear. `dotnet format` produced no diff.

## Status
DONE
