# Specification: Move `CombinedPrintQueueSink` into the Azure Adapter Project

## Summary
`CombinedPrintQueueSink` currently lives in the API host project (`Anela.Heblo.API/Features/ExpeditionList/`), violating the Adapters pattern documented in `filesystem.md`, which requires all `IPrintQueueSink` implementations to live in adapter projects. This is a pure relocation: move the class into `Anela.Heblo.Adapters.Azure`, update its namespace, and repoint the one call site that constructs it. No behavior changes.

## Background
Every other `IPrintQueueSink` implementation (`FileSystemPrintQueueSink`, `AzureBlobPrintQueueSink`, `CupsPrintQueueSink`) lives under `backend/src/Adapters/<AdapterProject>/Features/ExpeditionList/`, per the architecture rule in `docs/architecture/filesystem.md`. `CombinedPrintQueueSink` is the sole exception, sitting in the API host project instead. This was flagged by the daily arch-review routine (2026-07-19) as an architectural inconsistency: it couples the API host to a composition-only detail, makes the class untestable without pulling in the API project, and would mislead anyone searching adapter projects for all sink implementations. `Anela.Heblo.Adapters.Azure` is the correct new home since it already contains `AzureBlobPrintQueueSink`, the primary sink `CombinedPrintQueueSink` wraps, and the API project already references `Anela.Heblo.Adapters.Azure` (it's already used for `AzureBlobPrintQueueSink` and `AddAzurePrintQueueSinkInfrastructure`), so no new project reference is required.

## Functional Requirements

### FR-1: Relocate `CombinedPrintQueueSink` to the Azure adapter project
Move the file `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` to `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs`, updating only its namespace declaration (and, if required, its access modifier — see acceptance criteria). Class body, constructor, and `SendAsync` implementation are otherwise byte-for-byte unchanged.

**Acceptance criteria:**
- File no longer exists at `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`.
- File exists at `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs`.
- Namespace changes from `Anela.Heblo.API.Features.ExpeditionList` to `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`, matching the sibling `AzureBlobPrintQueueSink` in the same folder.
- Class access modifier changes from `internal sealed` to `public` (or `public sealed`, matching `AzureBlobPrintQueueSink`'s existing `public` modifier in the same file/folder). This is required because the class is constructed from the API project via `ServiceCollectionExtensions.AddPrintQueueSink` — a cross-assembly reference cannot resolve an `internal` type. This is a compile-time necessity, not a behavior change: the type remains registered only behind `IPrintQueueSink` and is not otherwise exposed publicly.
- `SendAsync` method body, constructor signature, and field names are unchanged.
- No other file changes accompany the move (no renaming of members, no reformatting beyond what the namespace edit requires).

### FR-2: Update the DI registration call site
Update `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, `AddPrintQueueSink` method, `"Combined"` case (currently line 441), which currently does:
```csharp
return new Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(azure, cups);
```
Update the fully-qualified reference to `Anela.Heblo.Adapters.Azure.Features.ExpeditionList.CombinedPrintQueueSink`. Since the file already has `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` at line 19 (used for `AzureBlobPrintQueueSink`), the fully-qualified reference can be simplified to just `CombinedPrintQueueSink` if that reads more consistently with surrounding code — either form (fully-qualified or relying on the existing `using`) is acceptable, since both compile identically.

**Acceptance criteria:**
- The `"Combined"` case in `AddPrintQueueSink` compiles and resolves `CombinedPrintQueueSink` from the `Anela.Heblo.Adapters.Azure.Features.ExpeditionList` namespace.
- The now-unused `using Anela.Heblo.API.Features.ExpeditionList;` (line 26) is removed if `CombinedPrintQueueSink` was the only symbol in that namespace referenced from this file. Verify no other symbol in that namespace is used elsewhere in the file before removing.
- No other lines in `AddPrintQueueSink` (or elsewhere in `ServiceCollectionExtensions.cs`) change.

### FR-3: No project reference changes
Confirm `Anela.Heblo.API.csproj` already references `Anela.Heblo.Adapters.Azure` (it does, evidenced by existing usage of `AzureBlobPrintQueueSink` and `AddAzurePrintQueueSinkInfrastructure` in the same file) and that `Anela.Heblo.Adapters.Azure.csproj` already references `Anela.Heblo.Application` (for `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink`, evidenced by `AzureBlobPrintQueueSink`'s existing `using`). No `.csproj` edits are expected.

**Acceptance criteria:**
- `dotnet build` succeeds with no project-reference changes.
- If either reference is in fact missing (contradicting the assumption above), add the minimal project reference needed and note it in the PR description as a deviation from this spec.

## Non-Functional Requirements
N/A — this is a structural/namespace-only change with no runtime, performance, or security surface. Existing behavior, DI lifetimes (`Scoped`/`Singleton`/keyed registrations), and the `SendAsync` combining logic are preserved exactly.

## Data Model
N/A — no data model involved.

## API / Interface Design
N/A — no public API, controller, or UI surface changes. The `IPrintQueueSink` interface contract is untouched; only the concrete implementation's assembly location and namespace change.

## Dependencies
- `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink` (interface implemented — unchanged).
- `Anela.Heblo.Adapters.Azure` project (new home for the class; project already exists and is already referenced by the API project).
- No new NuGet packages or external services.

## Out of Scope
- Any change to `FileSystemPrintQueueSink` or `CupsPrintQueueSink` locations (already compliant).
- Any change to the combining logic, sink ordering, or error-handling behavior in `SendAsync`.
- Any change to configuration keys (`ExpeditionList:PrintSink`) or the set of valid values (`FileSystem`, `AzureBlob`, `Cups`, `Combined`).
- Any change to `AzureBlobPrintQueueSink`, `AddAzurePrintQueueSinkInfrastructure`, or other existing Azure adapter registrations.
- Renaming the class, its namespace segments beyond the adapter-root swap, or its file name.

## Open Questions
None.

## Status: COMPLETE
