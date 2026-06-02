# Specification: Move `AnalyticsRepository` to Persistence Layer

## Summary
The Application layer currently carries a direct project reference to the Persistence layer because `AnalyticsRepository` is implemented inside `Anela.Heblo.Application` and injects `ApplicationDbContext` directly. This refactor relocates the implementation to `Anela.Heblo.Persistence`, keeps the `IAnalyticsRepository` abstraction in the Application layer, and removes the outward-pointing project reference — restoring Clean Architecture dependency flow.

## Background
Per Clean Architecture (and `docs/architecture/development_guidelines.md`), dependencies must point inward: Persistence → Application → Domain. The current state inverts this for the Analytics module:

- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` line 41 declares `<ProjectReference Include="../Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj" />`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` (lines 5–19) imports `Anela.Heblo.Persistence` and injects `ApplicationDbContext` directly into its constructor.
- The methods `GetInvoiceImportStatisticsAsync` (lines 115–185) and `GetBankStatementImportStatisticsAsync` (lines 213–283) build raw EF Core queries against `_dbContext`, bypassing any repository abstraction.

This leakage:
1. Allows any feature module under `Application` to reach `ApplicationDbContext` directly, eroding module isolation.
2. Couples the Application layer's build graph to EF Core and the entire Persistence assembly.
3. Blocks any future module split or microservice extraction.
4. Contradicts the "Phase 1" persistence rule that repository implementations live in the Persistence layer while only abstractions sit in Application.

The fix is mechanical but must be done carefully: move the implementation class, re-wire DI registration, and prove that no other code in `Anela.Heblo.Application` still depends on types from `Anela.Heblo.Persistence` before removing the project reference.

## Functional Requirements

### FR-1: Relocate `AnalyticsRepository` implementation to the Persistence layer
Move the file `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` to `backend/src/Anela.Heblo.Persistence/Analytics/AnalyticsRepository.cs`. Update the namespace to match the Persistence layer's conventions (mirroring other repository implementations already living there). The class body — including the EF Core queries in `GetInvoiceImportStatisticsAsync` and `GetBankStatementImportStatisticsAsync` — moves verbatim; no behavior change.

**Acceptance criteria:**
- A file exists at `backend/src/Anela.Heblo.Persistence/Analytics/AnalyticsRepository.cs` containing the `AnalyticsRepository` class.
- The previous file under `Anela.Heblo.Application/Features/Analytics/Infrastructure/` no longer exists.
- The namespace of the moved class follows the same pattern as other repository implementations in `Anela.Heblo.Persistence`.
- The class still implements `IAnalyticsRepository`.
- The class signature, constructor parameters, and public method signatures are unchanged.

### FR-2: Keep `IAnalyticsRepository` abstraction in the Application layer
The interface `IAnalyticsRepository` must remain in `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/` (its current location) so that Application-layer consumers can depend on the abstraction without referencing Persistence.

**Acceptance criteria:**
- `IAnalyticsRepository.cs` still exists in `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/`.
- Its namespace, methods, and signatures are unchanged.
- All existing consumers in the Application layer continue to depend only on the interface, not the concrete class.

### FR-3: Move DI registration to `PersistenceModule`
The binding `services.AddScoped<IAnalyticsRepository, AnalyticsRepository>()` (or equivalent) must be registered in `PersistenceModule.cs` inside `Anela.Heblo.Persistence`. If the binding currently lives in an Application-layer module/extension method, remove it from there.

**Acceptance criteria:**
- `PersistenceModule.cs` (or the equivalent Persistence-layer DI registration entry point) registers `AnalyticsRepository` against `IAnalyticsRepository`.
- No Application-layer DI module registers `AnalyticsRepository` as a concrete type.
- The application starts successfully and resolves `IAnalyticsRepository` for any consumer that requested it before the refactor.

### FR-4: Remove the `ProjectReference` from `Anela.Heblo.Application.csproj`
Once FR-1 through FR-3 are complete and no remaining code in `Anela.Heblo.Application` references types from `Anela.Heblo.Persistence`, delete line 41 (`<ProjectReference Include="../Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj" />`) from `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`.

**Acceptance criteria:**
- The `ProjectReference` to `Anela.Heblo.Persistence` is absent from `Anela.Heblo.Application.csproj`.
- `dotnet build` on `Anela.Heblo.Application.csproj` succeeds without that reference.
- `dotnet build` on the full solution succeeds.
- A search across `Anela.Heblo.Application` for `using Anela.Heblo.Persistence` returns zero matches.
- A search across `Anela.Heblo.Application` for `ApplicationDbContext` (or any other concrete type from `Anela.Heblo.Persistence`) returns zero matches.

### FR-5: Preserve existing Analytics behavior
The two methods that currently query `ApplicationDbContext` directly — `GetInvoiceImportStatisticsAsync` (lines 115–185) and `GetBankStatementImportStatisticsAsync` (lines 213–283) — must continue to produce identical results after the move. The refactor is structural, not behavioral.

**Acceptance criteria:**
- All existing unit and integration tests that exercise `AnalyticsRepository` pass without modification of their assertions.
- Any test that referenced the old namespace updates its `using` directive only — no logic changes.
- No SQL query or LINQ expression in the moved methods is altered.

### FR-6: Update test project references if affected
If the test project that covers `AnalyticsRepository` previously resolved the class through `Anela.Heblo.Application`, it must continue to compile after the move — either by adding a reference to `Anela.Heblo.Persistence` (preferred for integration tests) or by depending only on the `IAnalyticsRepository` abstraction (preferred for unit tests with mocks).

**Acceptance criteria:**
- The test project compiles.
- All Analytics-related tests pass.
- No test references the concrete `AnalyticsRepository` via a path that would force `Anela.Heblo.Application` to depend on `Anela.Heblo.Persistence`.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance change is expected or permitted. EF Core query shapes, materialization, and execution remain identical.

### NFR-2: Security
No change to authentication, authorization, or data exposure. The repository remains internal and is consumed only via DI.

### NFR-3: Architectural Compliance
After the change, the Application layer must contain **no** project reference to Persistence and **no** `using Anela.Heblo.Persistence` statements. Verified by grep and by inspecting `.csproj`.

### NFR-4: Backward Compatibility
No public API change. All callers of `IAnalyticsRepository` continue to work without recompilation of their source — only the assembly providing the implementation changes.

## Data Model
No data model changes. No schema migrations. No EF Core entity changes.

## API / Interface Design
No HTTP API, MediatR contract, or DTO changes. The only "interface" affected is the C# `IAnalyticsRepository` abstraction, whose signature is preserved verbatim.

Affected files (final state):
- `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/IAnalyticsRepository.cs` — unchanged.
- `backend/src/Anela.Heblo.Persistence/Analytics/AnalyticsRepository.cs` — new location of the implementation.
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — adds DI registration.
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — `ProjectReference` to Persistence removed.
- Any DI registration module under `Anela.Heblo.Application` that previously bound `IAnalyticsRepository` → updated/removed.

## Dependencies
- `Anela.Heblo.Persistence` must already reference `Anela.Heblo.Application` (so the implementation in Persistence can see the abstraction). Verify this reference exists before starting — it almost certainly does, since other repository implementations there already depend on Application-layer abstractions.
- `PersistenceModule.cs` is the established Persistence-layer DI entry point; this work relies on its presence.
- EF Core packages must be available to `Anela.Heblo.Persistence` (they already are).
- No external service or package dependency changes.

## Out of Scope
- **Behavior or query optimization** in `GetInvoiceImportStatisticsAsync` and `GetBankStatementImportStatisticsAsync`. The queries are moved verbatim even if they could be improved.
- **Other modules** in `Anela.Heblo.Application` that may have similar Persistence leakage. This spec addresses Analytics only; a broader sweep is a separate effort.
- **Renaming or restructuring** `IAnalyticsRepository` methods.
- **Introducing a generic repository base** beyond what already exists in Xcc.
- **Removing or modifying** the `Features/Analytics/Infrastructure/` folder in the Application layer — it remains as the home of the abstraction.
- **Database migrations** — none required.
- **Documentation updates** to architecture docs — the docs already prescribe this layout; this change brings code into compliance with existing docs.

## Open Questions
None.

## Status: COMPLETE