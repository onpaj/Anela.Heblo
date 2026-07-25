### task: move-combined-print-queue-sink

## Goal
Relocate the `CombinedPrintQueueSink` class from the API host project into `Anela.Heblo.Adapters.Azure`, matching the pattern used by every other `IPrintQueueSink` implementation (`FileSystemPrintQueueSink`, `AzureBlobPrintQueueSink`, `CupsPrintQueueSink`), and update the one call site plus the two existing test files that reference it. This is a pure relocation — no behavior change, no new abstractions, no test additions beyond namespace fixes.

## Background
- Current (wrong) location: `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`, namespace `Anela.Heblo.API.Features.ExpeditionList`, class declared `internal sealed class CombinedPrintQueueSink : IPrintQueueSink`.
- Target location: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs`, namespace `Anela.Heblo.Adapters.Azure.Features.ExpeditionList` (same folder as the sibling `AzureBlobPrintQueueSink`).
- The class must become `public` (visibility only — `sealed` may be kept or dropped, doesn't matter) because `ServiceCollectionExtensions` in the API assembly constructs it directly across assembly boundaries; `internal` would not compile.
- No `.csproj` changes are needed: `Anela.Heblo.API.csproj` already references `Anela.Heblo.Adapters.Azure`, and `Anela.Heblo.Adapters.Azure.csproj` already references `Anela.Heblo.Application` (where `IPrintQueueSink` lives).
- Confirmed via repo-wide grep: exactly 4 files in the repo reference `CombinedPrintQueueSink` — the source file itself, its call site in `ServiceCollectionExtensions.cs`, and two test files. All four are covered by the steps below.

## Steps

1. **Read the current source file** at:
   `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`

   Its exact current content is:
   ```csharp
   using Anela.Heblo.Application.Shared.Printing;

   namespace Anela.Heblo.API.Features.ExpeditionList;

   internal sealed class CombinedPrintQueueSink : IPrintQueueSink
   {
       private readonly IPrintQueueSink _azureSink;
       private readonly IPrintQueueSink _cupsSink;

       public CombinedPrintQueueSink(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)
       {
           _azureSink = azureSink;
           _cupsSink = cupsSink;
       }

       public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
       {
           var paths = filePaths.ToList();
           await _azureSink.SendAsync(paths, cancellationToken);
           await _cupsSink.SendAsync(paths, cancellationToken);
       }
   }
   ```

2. **Create the new file** at:
   `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs`

   with this exact content (only the namespace and access modifier differ from the original — constructor, fields, and `SendAsync` body are byte-for-byte unchanged):
   ```csharp
   using Anela.Heblo.Application.Shared.Printing;

   namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

   public sealed class CombinedPrintQueueSink : IPrintQueueSink
   {
       private readonly IPrintQueueSink _azureSink;
       private readonly IPrintQueueSink _cupsSink;

       public CombinedPrintQueueSink(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)
       {
           _azureSink = azureSink;
           _cupsSink = cupsSink;
       }

       public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
       {
           var paths = filePaths.ToList();
           await _azureSink.SendAsync(paths, cancellationToken);
           await _cupsSink.SendAsync(paths, cancellationToken);
       }
   }
   ```
   (This new file lands in the same directory as `AzureBlobPrintQueueSink.cs`, which is `public class` — matching visibility.)

3. **Delete the old file**:
   `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`

4. **Check whether the old directory is now empty** and remove it if so:
   `backend/src/Anela.Heblo.API/Features/ExpeditionList/`
   (As of the architecture review, this file was the only thing in that folder. Confirm with `ls` before deleting the directory — if anything else is present, leave the directory in place and just leave a note.)

5. **Update the call site** in:
   `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

   a. Around line 441, inside `AddPrintQueueSink`, the `"Combined"` case currently has:
      ```csharp
                  return new Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(azure, cups);
      ```
      Change it to (relying on the existing `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` already present at line 19, which is used for `AzureBlobPrintQueueSink`):
      ```csharp
                  return new CombinedPrintQueueSink(azure, cups);
      ```

   b. Around line 26, remove the now-unused using directive:
      ```csharp
      using Anela.Heblo.API.Features.ExpeditionList;
      ```
      Before removing it, grep the whole file for any other symbol from the `Anela.Heblo.API.Features.ExpeditionList` namespace to confirm `CombinedPrintQueueSink` was the only reason for the `using`. (The architecture review already confirmed this via repo-wide grep, but re-verify locally since you're the one editing.)

   Do not change any other line in this file.

6. **Update the two existing test files that reference `CombinedPrintQueueSink`** — these are not new tests, just namespace fixes so the existing suite keeps compiling and passing:

   a. `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs`
      Line 1 currently reads:
      ```csharp
      using Anela.Heblo.API.Features.ExpeditionList;
      ```
      Change it to:
      ```csharp
      using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
      ```
      No other line in this file needs to change (the test's own namespace, `Anela.Heblo.Tests.Features.ExpeditionList`, stays as-is — only the `using` for the type under test changes).

   b. `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`
      This file already has `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` at line 2 (used for `AzureBlobPrintQueueSink`). It also has, at line 6:
      ```csharp
      using Anela.Heblo.API.Features.ExpeditionList;
      ```
      Remove line 6 entirely — `CombinedPrintQueueSink` will now resolve via the existing line-2 `using`, and no other symbol from `Anela.Heblo.API.Features.ExpeditionList` is used in this file. Do not change anything else in this file.

7. **Search for any other references** you may have missed, to be safe:
   ```bash
   grep -rn "CombinedPrintQueueSink" backend/
   grep -rn "Anela.Heblo.API.Features.ExpeditionList" backend/
   ```
   Confirm the only remaining hits are: the new adapter source file, the updated call site, and the two updated test files (referencing the class name itself, not the old namespace). There should be zero remaining references to the `Anela.Heblo.API.Features.ExpeditionList` namespace anywhere.

8. **Build and test**:
   ```bash
   cd backend && dotnet build && dotnet format --verify-no-changes
   dotnet test --filter "FullyQualifiedName~CombinedPrintQueueSink"
   ```
   Then run the full backend test suite (or at least the `Anela.Heblo.Tests` project) to confirm nothing else regressed.

## Definition of Done
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` no longer exists.
- The empty `backend/src/Anela.Heblo.API/Features/ExpeditionList/` directory no longer exists (unless something unexpected was found in it, in which case that deviation is noted).
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs` exists with namespace `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`, class is `public sealed`, constructor/fields/`SendAsync` body byte-for-byte identical to the original.
- `ServiceCollectionExtensions.cs`: the `"Combined"` case constructs `CombinedPrintQueueSink` via the existing Azure-namespace `using`; the dead `using Anela.Heblo.API.Features.ExpeditionList;` is removed; no other line in the file changed.
- Both test files (`CombinedPrintQueueSinkTests.cs`, `CombinedPrintQueueSinkRegistrationTests.cs`) compile against the new namespace with no other logic changes.
- No `.csproj` file changes (confirm build succeeds without them; if it doesn't, the missing reference is a real deviation to flag, not something to silently work around).
- `dotnet build` succeeds with no new warnings/errors.
- `dotnet format --verify-no-changes` passes (or `dotnet format` was run and the diff is limited to the files touched above).
- All existing tests pass, in particular every test in `CombinedPrintQueueSinkTests.cs` and `CombinedPrintQueueSinkRegistrationTests.cs`, and the `FileSystem_ResolvesFileSystemPrintQueueSink` regression guard in the latter.
- `grep -rn "Anela.Heblo.API.Features.ExpeditionList" backend/` returns no results.
