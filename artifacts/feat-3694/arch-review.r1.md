# Architecture Review: Move `CombinedPrintQueueSink` into the Azure Adapter Project

## Skip Design: true

## Architectural Fit Assessment

Verified against `docs/architecture/filesystem.md` ("Concrete `IPrintQueueSink` implementations... live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`") and the actual layout:

- `FileSystemPrintQueueSink` → `Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/`
- `AzureBlobPrintQueueSink` → `Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/` (`public class`)
- `CupsPrintQueueSink` → `Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/`
- `CombinedPrintQueueSink` → `Anela.Heblo.API/Features/ExpeditionList/` (`internal sealed class`) — the sole violator.

The spec's factual claims all check out against the code:
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/` contains exactly one file, `CombinedPrintQueueSink.cs` — nothing else is left behind after the move, and the directory can disappear entirely.
- `ServiceCollectionExtensions.cs` line 26 (`using Anela.Heblo.API.Features.ExpeditionList;`) and line 441 (fully-qualified construction) are the only two references to that namespace anywhere in `backend/src` — confirmed via repo-wide grep. Removing the `using` is safe.
- Line 19 already has `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` (for `AzureBlobPrintQueueSink`), so the fully-qualified `Anela.Heblo.Adapters.Azure.Features.ExpeditionList.CombinedPrintQueueSink` in the spec's FR-2 can just as well collapse to the bare `CombinedPrintQueueSink` — either compiles identically, matching the spec's own note.
- `Anela.Heblo.API.csproj` already has `<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Azure\Anela.Heblo.Adapters.Azure.csproj" />`. `Anela.Heblo.Adapters.Azure.csproj` already references `Anela.Heblo.Application` (where `IPrintQueueSink` lives). No `.csproj` edits are needed — FR-3 is correct.
- `CombinedPrintQueueSink` is currently `internal sealed`; it must become `public` (matching `AzureBlobPrintQueueSink`'s modifier) for `ServiceCollectionExtensions` in the API assembly to construct it directly — this is a compile-time requirement of the move, not a design decision.

This is a pure file relocation + namespace/visibility fix. There is no new abstraction, no behavior change, and no ambiguity in the destination. Design stage is unnecessary.

## Proposed Architecture

### Component Overview
No new components. One existing class moves from the API host project to the Azure adapter project it already logically depends on (it wraps `AzureBlobPrintQueueSink` as its primary sink).

### Key Design Decisions

#### Decision 1: Destination project
**Options considered:** (a) new `Anela.Heblo.Adapters.Combined` project, (b) `Anela.Heblo.Adapters.Azure` (existing), (c) `Anela.Heblo.Adapters.Cups` (existing).
**Chosen approach:** (b) `Anela.Heblo.Adapters.Azure`.
**Rationale:** Both a new project and Cups would work, but a new project is unjustified ceremony for one class, and placing it in Azure is more natural since Azure is the sink `CombinedPrintQueueSink` is keyed/constructed alongside (`"azure"` keyed singleton resolved to `AzureBlobPrintQueueSink`) and the class already sits next to `AzureBlobPrintQueueSink` conceptually. No new project reference is required either way since API already references both Azure and Cups adapters — but Azure is the better-fitting home.

## Implementation Guidance

### Directory / Module Structure
- Delete: `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` (and the now-empty `Features/ExpeditionList/` folder under API, if nothing else references it).
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs`, namespace `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`, class changed from `internal sealed` to `public sealed` (matching `AzureBlobPrintQueueSink`'s `public` in the same folder — `sealed` may be kept or dropped, developer's call, since `AzureBlobPrintQueueSink` itself isn't sealed; either is fine, not worth specifying further).
- Edit: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — line 441 drop the `Anela.Heblo.API.Features.ExpeditionList.` qualifier (relies on existing line-19 `using`), and remove the now-dead `using Anela.Heblo.API.Features.ExpeditionList;` at line 26.

### Interfaces and Contracts
Unchanged. `IPrintQueueSink` contract, constructor signature `(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)`, and `SendAsync` body are untouched.

### Data Flow
Unchanged. DI wiring in `AddPrintQueueSink`'s `"Combined"` case still resolves the same two keyed `IPrintQueueSink` instances (`"azure"`, `"cups"`) and constructs the same combining wrapper; only its assembly/namespace of origin changes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgetting to flip `internal` → `public` causes a compile error in the API project | Low | Immediately caught by `dotnet build`; trivial fix. |
| Leaving a dangling empty `Features/ExpeditionList/` folder under API | Low | Cosmetic only; delete the folder if empty, but not build-breaking either way. |

## Specification Amendments
None. The spec (`spec.r1.md`) is accurate, fully verified against the code, and requires no changes.

## Prerequisites
None.
