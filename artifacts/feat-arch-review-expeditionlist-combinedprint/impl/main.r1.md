Now writing the output artifact.

---

# Implementation: Decouple CombinedPrintQueueSink from DI Keying Conventions

## What was implemented

Moved `CombinedPrintQueueSink` from `Anela.Heblo.Application` to `Anela.Heblo.API`, stripped its `[FromKeyedServices]` attributes, and replaced the `"Combined"` DI registration with a factory delegate that resolves the two keyed sinks explicitly. This is a pure layering correction — the Application layer now has zero `Microsoft.Extensions.DependencyInjection` coupling; runtime behavior is identical.

## Files created/modified

- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — **new**: `internal sealed` class, namespace `Anela.Heblo.API.Features.ExpeditionList`, plain constructor (no DI attributes)
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — **modified**: added `using Anela.Heblo.API.Features.ExpeditionList;`; replaced `"Combined"` arm's `AddScoped<T,T>` with inline factory lambda using `GetRequiredKeyedService`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` — **deleted**
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — **modified**: swapped one `using` from Application namespace to API namespace (constructor call unchanged)
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — **new**: 4 DI-resolution tests (FR-5)

## Tests

- `CombinedPrintQueueSinkTests` (4 tests) — unit tests on the relocated class; all pass unchanged
- `CombinedPrintQueueSinkRegistrationTests` (4 tests, new) — DI-resolution: verifies `Combined` resolves `CombinedPrintQueueSink`, keyed `"azure"`→`AzureBlobPrintQueueSink`, keyed `"cups"`→`CupsPrintQueueSink`, `FileSystem` still resolves `FileSystemPrintQueueSink`
- `ExpeditionListServicePrintSinkTests` (2 tests) — regression guard; unchanged, pass

**All 10 tests pass.**

## How to verify

```bash
# Architectural invariants
grep -rn 'FromKeyedServices' backend/src/Anela.Heblo.Application/  # must return 0 matches

# Targeted tests (4+4+2 = 10)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CombinedPrintQueueSink|FullyQualifiedName~ExpeditionListServicePrintSink"

# Full build
dotnet build backend/Anela.Heblo.sln
```

## Notes

- The Task 4 implementer added `services.AddSingleton(TimeProvider.System)` to `BuildProvider` (not in the original template) because `AzureBlobPrintQueueSink` requires it at construction time — this was a correct adjustment.
- The 38 failures in the full test suite are pre-existing Testcontainers/Docker failures (PostgreSQL integration tests) unrelated to this change.
- `dotnet format --verify-no-changes` passed with zero diff.

## PR Summary

Moves `CombinedPrintQueueSink` from `Anela.Heblo.Application` to `Anela.Heblo.API` and replaces the `[FromKeyedServices]`-decorated constructor with a factory delegate at the composition root — correcting a Clean Architecture layering violation where Application code carried knowledge of DI key strings defined in the API's `ServiceCollectionExtensions`.

The factory (`AddScoped<IPrintQueueSink>(provider => { ... })`) resolves the two keyed sinks (`"azure"`, `"cups"`) explicitly and constructs the composite; the Application layer no longer references `Microsoft.Extensions.DependencyInjection` for this class. Runtime behavior is identical — same sequential dispatch, same `.ToList()` materialization, same fail-fast semantics.

Four commits: add API-layer class → wire factory → delete Application-layer copy → add DI-resolution regression test (FR-5).

### Changes
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — new home for the composite, no DI attributes
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — "Combined" arm rewritten to factory delegate
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` — deleted
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — using updated to new namespace
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — new DI-resolution regression test (4 assertions)

## Status

DONE