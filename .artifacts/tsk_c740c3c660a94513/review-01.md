# Review — Flexi: `ILotsClient` registered twice with conflicting lifetimes

## Verdict: done

## What was checked

**Diff scope.** `git show a27fd9d4` touches exactly two files:
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — one line deleted (`services.AddScoped<ILotsClient, FlexiLotsClient>();`, formerly line 86). Confirmed via `grep` that the Singleton registration on line 73 (`services.AddSingleton<Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient, FlexiLotsClient>();`) is now the only `ILotsClient` registration in the file. No other lines moved or touched.
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Lots/FlexiAdapterLotsClientRegistrationTests.cs` (new) — a descriptor-only DI test.

**Conformance to plan/architecture.** Matches `plan-01.md`/`architecture-01.md` exactly: FR-1 (exactly one Singleton descriptor for `ILotsClient → FlexiLotsClient`) is satisfied by the deletion; FR-2 (no consumer changes) is satisfied — no other file was touched. The implementer correctly left the fully-qualified `ILotsClient` name on line 73 as instructed, avoiding a drive-by simplification.

**Test quality.** `FlexiAdapterLotsClientRegistrationTests.AddFlexiAdapter_RegistersLotsClientExactlyOnce_AsSingleton` calls the real `AddFlexiAdapter` extension with a minimal in-memory configuration, then asserts on `ServiceDescriptor` metadata only (no `BuildServiceProvider()`), mirroring the existing `PersistenceModuleTests` precedent cited in the design. It would have failed pre-fix (2 descriptors instead of 1) and correctly asserts `Lifetime == Singleton` and `ImplementationType == FlexiLotsClient`.

**Independent verification performed this step** (not just trusting development-01.md's claims):
- `dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` → succeeded, 0 errors, only pre-existing warnings (none introduced by this change).
- `dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~Lots"` → 13/13 passed (12 pre-existing `FlexiLotsClientTests` + the new registration test).
- `dotnet format Anela.Heblo.sln --include <the two changed files> --verify-no-changes` → exit code 0, clean.

## Assessment

Correctness bug fixed exactly as scoped, no unrelated changes, regression test in place and passing, build and format clean. No functional requirement is unmet, no architecture conflict, no missing required test, no correctness issue found.

```json
{"outcome": "done", "summary": "Verified: the duplicate Scoped ILotsClient registration was removed, leaving the single intended Singleton binding; new regression test (13/13 Lots tests pass) guards against recurrence. Build succeeds with only pre-existing warnings, dotnet format is clean, and the diff is minimal and matches the approved plan/architecture exactly."}
```
