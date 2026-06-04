# Specification: Relocate CombinedPrintQueueSink to Application Layer

## Summary
`CombinedPrintQueueSink` is an `IPrintQueueSink` composite adapter that currently lives in the API project, violating Clean Architecture by placing Application-layer logic in the HTTP shell. This change relocates the class to the Application project alongside its peer sink implementations (`FileSystemPrintQueueSink`) with no behavioral changes — only the file location, namespace, and `using` statements move.

## Background
The repo follows Clean Architecture: the API project (`Anela.Heblo.API`) is a thin HTTP shell — controllers, middleware, auth filters — while business-logic services and adapters live in `Anela.Heblo.Application` or dedicated adapter projects. `docs/architecture/development_guidelines.md` codifies this rule.

`CombinedPrintQueueSink` is a 25-line internal sealed class implementing `IPrintQueueSink` (defined in `Anela.Heblo.Application.Shared.Printing`). It wraps two keyed `IPrintQueueSink` services (`"azure"` and `"cups"`) and forwards `SendAsync` to both. It has no HTTP, controller, MVC, or middleware responsibility. It is registered in the API's `ServiceCollectionExtensions.AddPrintQueueSink` when `ExpeditionList:PrintSink = "Combined"`.

Every other `IPrintQueueSink` implementation already lives in the appropriate layer:
- `FileSystemPrintQueueSink` → `Anela.Heblo.Application/Features/ExpeditionList/Services/`
- `AzureBlobPrintQueueSink` → `Anela.Heblo.Adapters.Azure/Features/ExpeditionList/`
- `CupsPrintQueueSink` → `Anela.Heblo.Adapters.Cups/Features/ExpeditionList/`

`CombinedPrintQueueSink` is the only outlier sitting in the API project. Filed by the daily arch-review routine on 2026-06-03.

## Functional Requirements

### FR-1: Move file to Application project
Move `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` to `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs`. The new location mirrors the placement of `FileSystemPrintQueueSink.cs`.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs`.
- File no longer exists under `backend/src/Anela.Heblo.API/Features/ExpeditionList/`.
- The `Anela.Heblo.API/Features/ExpeditionList/` directory is removed if it becomes empty.

### FR-2: Update namespace
Change the namespace from `Anela.Heblo.API.Features.ExpeditionList` to `Anela.Heblo.Application.Features.ExpeditionList.Services`, matching `FileSystemPrintQueueSink`.

**Acceptance criteria:**
- File declares `namespace Anela.Heblo.Application.Features.ExpeditionList.Services;`.
- Class remains `internal sealed class CombinedPrintQueueSink : IPrintQueueSink` (no visibility change).
- Constructor, fields, and `SendAsync` implementation are byte-for-byte equivalent to the current code.
- `using Anela.Heblo.Application.Shared.Printing;` and `using Microsoft.Extensions.DependencyInjection;` are preserved (the latter is required for `[FromKeyedServices]`).

### FR-3: Update DI registration call site
The current registration in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs::AddPrintQueueSink` references `CombinedPrintQueueSink` via `using Anela.Heblo.API.Features.ExpeditionList;`. Update the `using` to point to the new namespace. The registration line itself (`services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>();`) does not change.

**Acceptance criteria:**
- `ServiceCollectionExtensions.cs` imports `Anela.Heblo.Application.Features.ExpeditionList.Services` (either explicitly or via the existing `Anela.Heblo.Application.Features.ExpeditionList.Services` using already present for `FileSystemPrintQueueSink`).
- The `using Anela.Heblo.API.Features.ExpeditionList;` line is removed if no other type from that namespace is referenced.
- The `"Combined"` switch branch in `AddPrintQueueSink` continues to register `IPrintQueueSink` → `CombinedPrintQueueSink` with the same scoped lifetime and same keyed-service dependencies.
- DI resolution at runtime returns a `CombinedPrintQueueSink` instance when `ExpeditionList:PrintSink = "Combined"`.

### FR-4: Update test file
`backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` currently imports the type via `using Anela.Heblo.API.Features.ExpeditionList;`. Update this `using` to the new namespace. No test logic changes.

**Acceptance criteria:**
- Test file imports `Anela.Heblo.Application.Features.ExpeditionList.Services`.
- All four existing test cases (`SendAsync_BothSucceed_*`, `SendAsync_AzureThrows_*`, `SendAsync_AzureSucceedsCupsThrows_*`, `SendAsync_SinglePassEnumerable_*`) pass without modification to their bodies.
- The `internal sealed` class remains test-accessible via the existing `InternalsVisibleTo("Anela.Heblo.Tests")` declaration in `Anela.Heblo.Application/AssemblyInfo.cs` and `Anela.Heblo.Application.csproj`.

### FR-5: Preserve behavior end-to-end
No functional behavior changes. The sink's contract — sequential `await azureSink.SendAsync(paths)` then `await cupsSink.SendAsync(paths)`, with `paths` materialized once via `.ToList()` for single-pass enumerables — must be byte-identical.

**Acceptance criteria:**
- Manual diff of pre-move vs post-move class body shows zero changes to logic.
- All existing `CombinedPrintQueueSinkTests` pass.
- Integration: with `ExpeditionList:PrintSink = "Combined"` configured, the application starts and `IPrintQueueSink` resolves to `CombinedPrintQueueSink` with both keyed sinks injected.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance change is expected. This is a compile-time relocation; the IL produced for `CombinedPrintQueueSink.SendAsync` and its DI resolution path are unaffected.

### NFR-2: Security
No security impact. The class does not handle authentication, authorization, user input, secrets, or external network calls directly — it merely fans out to two already-registered sinks.

### NFR-3: Architecture compliance
After the change, the `Anela.Heblo.API` project must contain no `IPrintQueueSink` implementations. The API project's role as a thin HTTP shell is restored for this slice.

**Acceptance criteria:**
- `grep -r "IPrintQueueSink" backend/src/Anela.Heblo.API/` returns only references in DI wiring (`ServiceCollectionExtensions.cs`), not concrete implementations.

### NFR-4: Build & test gates
The standard project gates must pass after the change:
- `dotnet build` succeeds for the solution.
- `dotnet format` reports no changes needed (or all changes applied).
- `dotnet test` passes for `Anela.Heblo.Tests` — specifically the `CombinedPrintQueueSinkTests` class and any test that exercises the print sink registration (`ExpeditionListServicePrintSinkTests`).

## Data Model
Not applicable. No domain entities, DTOs, persistence schemas, or migrations are touched.

## API / Interface Design
Not applicable. No public HTTP API, MediatR request, or external contract is added, removed, or modified. The class's public surface (`SendAsync(IEnumerable<string>, CancellationToken)`) is unchanged.

The `IPrintQueueSink` interface itself remains in `Anela.Heblo.Application.Shared.Printing` and is untouched.

## Dependencies
- **Project references:** No project reference changes required. `Anela.Heblo.Application` already references `Microsoft.Extensions.DependencyInjection.Abstractions` (transitively or directly — verified by `FileSystemPrintQueueSink` using `IOptions<>` from the same family). The `[FromKeyedServices]` attribute lives in `Microsoft.Extensions.DependencyInjection.Abstractions` and is available in .NET 8.
- **InternalsVisibleTo:** Already configured for `Anela.Heblo.Application` → `Anela.Heblo.Tests` (`Anela.Heblo.Application/AssemblyInfo.cs:3`, `Anela.Heblo.Application.csproj:46`). No additional config needed.
- **Configuration:** The `ExpeditionList:PrintSink = "Combined"` configuration value continues to control activation. No config schema changes.
- **Related specs (historical):** `docs/superpowers/specs/2026-03-25-combined-print-queue-sink-design.md` and `docs/superpowers/plans/2026-03-25-combined-print-queue-sink.md` describe the original feature; both predate this relocation and do not need updating beyond optional reference notes.

## Out of Scope
- **Behavior changes:** No change to how the combined sink dispatches to its child sinks. Sequential `await` (Azure first, then CUPS) is preserved exactly. If Azure throws, CUPS is still skipped.
- **Parallel dispatch:** Switching to `Task.WhenAll` for parallel Azure+CUPS calls is **not** part of this work.
- **Error-isolation policy:** Changing the fail-fast semantics (e.g., attempting CUPS even if Azure fails) is **not** part of this work.
- **Refactoring `AddPrintQueueSink`:** The note in `ServiceCollectionExtensions.cs` about `AddAzurePrintQueueSink` registering a non-keyed sink as an unused side effect is **not** addressed here.
- **Renaming the class, the keyed names (`"azure"`, `"cups"`), or the configuration key (`ExpeditionList:PrintSink`):** all stay as-is.
- **Visibility broadening:** The class stays `internal sealed`. No `public` exposure.
- **Tests beyond the import statement:** Test logic, assertions, and mocking setup are unchanged.
- **Other arch-review findings:** This brief covers only `CombinedPrintQueueSink`. The broader pattern of locating `IPrintQueueSink` implementations is incidentally reinforced but no audit of related code is performed.

## Open Questions
None.

## Status: COMPLETE