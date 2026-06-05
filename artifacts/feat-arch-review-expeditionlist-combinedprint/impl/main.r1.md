Committed. Here is the output summary:

---

# Implementation: Relocate CombinedPrintQueueSink to Application Layer

## What was implemented

`CombinedPrintQueueSink` was moved from the API project (`Anela.Heblo.API/Features/ExpeditionList/`) to the Application project (`Anela.Heblo.Application/Features/ExpeditionList/Services/`), restoring Clean Architecture compliance. No logic was changed — only the file location, namespace, and `using` statements moved.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` — new file, updated namespace to `Anela.Heblo.Application.Features.ExpeditionList.Services`
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — deleted (directory also removed)
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed dead `using Anela.Heblo.API.Features.ExpeditionList;`
- `backend/src/Anela.Heblo.Application/AssemblyInfo.cs` — added `[assembly: InternalsVisibleTo("Anela.Heblo.API")]`
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — added `<InternalsVisibleTo Include="Anela.Heblo.API" />`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — updated `using` to new namespace only

## Tests

`CombinedPrintQueueSinkTests` — all 4 test cases pass:
- `SendAsync_BothSucceed_CallsBothSinksWithSamePaths`
- `SendAsync_AzureThrows_CupsNeverCalledAndExceptionPropagates`
- `SendAsync_AzureSucceedsCupsThrows_ExceptionPropagates`
- `SendAsync_SinglePassEnumerable_BothSinksReceiveAllPaths`

## How to verify

```bash
dotnet build Anela.Heblo.sln   # 0 errors
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSink"   # 4/4 passed
grep -r "IPrintQueueSink" backend/src/Anela.Heblo.API/   # only ServiceCollectionExtensions.cs
```

## Notes

The arch-review amendment (FR-6) was required: `internal sealed` in Application is invisible to the API project without `InternalsVisibleTo`. Both `AssemblyInfo.cs` and `.csproj` were updated to mirror the existing pattern for `Anela.Heblo.Tests`.

## PR Summary

Moves `CombinedPrintQueueSink` from the API project into `Anela.Heblo.Application/Features/ExpeditionList/Services/` — the correct layer for a pure Application-level composite adapter. The API project previously held the only `IPrintQueueSink` implementation not in its proper layer; after this change, the API project contains no sink implementations, only DI wiring.

The only non-obvious prerequisite: the class stays `internal sealed`, so `InternalsVisibleTo("Anela.Heblo.API")` was added to `AssemblyInfo.cs` and `Anela.Heblo.Application.csproj` to let the API's composition root register the type without widening visibility.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` — relocated file, namespace updated
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — deleted
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed dead `using`
- `backend/src/Anela.Heblo.Application/AssemblyInfo.cs` — added `InternalsVisibleTo("Anela.Heblo.API")`
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — added `<InternalsVisibleTo Include="Anela.Heblo.API" />`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — updated `using` to new namespace

## Status
DONE