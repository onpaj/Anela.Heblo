# Architecture Review: Move `InfrastructureConfigurationKeys` out of Domain layer

## Skip Design: true

## Architectural Fit Assessment

This is a textbook Clean Architecture layering fix, and it aligns cleanly with existing conventions in this codebase — no new pattern is introduced.

`Anela.Heblo.Application/Shared/` already exists and already hosts exactly this kind of cross-cutting, non-domain type: `ErrorCodes.cs`, `BaseResponse.cs`, `ListResponse.cs`, `HttpStatusCodeAttribute.cs`, plus subfolders (`Rag/`, `Http/`, `Json/`, `Printing/`, `Users/`, `WebSearch/`). `docs/architecture/filesystem.md` explicitly documents the distinction between `Domain/Shared/Rag/` (cross-module **domain** types) and `Application/Shared/Rag/` (cross-module **application/infrastructure** types — "options base classes, helpers, shared services"). `InfrastructureConfigurationKeys` is squarely the latter category: it is a lookup table of environment-variable/config-key *names*, not a domain concept, entity, or value object. Its current home in `Domain/Shared` is the violation the issue correctly identifies, and `Application/Shared` is exactly where its siblings already live.

Project-reference direction was independently confirmed by inspection, not just asserted in the spec:
- `Anela.Heblo.Application.csproj` references `Anela.Heblo.Domain.csproj` (unaffected).
- `Anela.Heblo.API.csproj` references `Anela.Heblo.Application.csproj` (present).
- `Anela.Heblo.Adapters.Microsoft365.csproj` references `Anela.Heblo.Application.csproj` (present).

No project needs a new `<ProjectReference>`. A full-tree grep for `InfrastructureConfigurationKeys` across `backend/src` and `backend/test` returns exactly 11 hits: the definition file plus the 10 consumer files enumerated in the spec — no 11th consumer was missed. This confirms the spec's exhaustiveness claim rather than merely trusting it.

There is no UI/UX surface whatsoever: this is a namespace relocation of a `const string` holder class and a `using`-directive edit in 10 files. `Skip Design: true` is unambiguous.

## Proposed Architecture

### Component Overview

```
Before:
  Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs   (namespace Anela.Heblo.Domain.Shared)
                │
                ├── referenced by Anela.Heblo.Application (Application → Domain, already allowed)
                └── referenced by Anela.Heblo.API and Anela.Heblo.Adapters.Microsoft365
                    (API/Adapters → Domain, an *extra*, unnecessary edge — these layers
                     only needed this one infra-metadata class, not domain knowledge)

After:
  Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs   (namespace Anela.Heblo.Application.Shared)
                │
                ├── referenced by Anela.Heblo.Application features (same layer, sibling of ErrorCodes.cs etc.)
                ├── referenced by Anela.Heblo.API           (API → Application, already-existing edge)
                └── referenced by Anela.Heblo.Adapters.Microsoft365 (Adapter → Application, already-existing edge)

  Anela.Heblo.Domain/Shared/  no longer contains any infrastructure/config-key metadata —
  only Cooling.cs, CurrencyCode.cs, Result.cs, Rag/ (true domain-shared types) remain.
```

The net effect: the dependency edge from API/Adapters onto `Domain.Shared` for this symbol collapses into an edge that already existed (API/Adapters → Application). No new edges are introduced anywhere in the graph — the move is a pure removal of an unnecessary Domain dependency.

### Key Design Decisions

#### Decision 1: Single relocation target vs. per-consumer duplication

**Options considered:**
1. Move the whole class as-is to `Application/Shared/` (the brief's directive).
2. Split it: keep nothing in Domain, but let API-layer consumers define their own local `const string` duplicates in `API/Infrastructure/` instead of depending on Application for two constants (an alternative the original arch-review finding floated).

**Chosen approach:** Option 1 — single class, single location, `Application/Shared/InfrastructureConfigurationKeys.cs`, namespace `Anela.Heblo.Application.Shared`.

**Rationale:** The spec (status: COMPLETE) already made this call and it is correct — API already depends on Application (ADR-003, standard Controller→MediatR wiring), so referencing `Application.Shared` from API introduces no new coupling. Option 2 would create two sources of truth for the same three string constants (`UseMockAuth` appears in `AuthenticationExtensions`, both Hangfire filters, and three `*Module.cs` files — a classic single-definition case), trading a nonexistent problem (there is no forbidden dependency direction from API to Application) for a real one (drift risk between duplicated constants). Reject option 2.

#### Decision 2: Where exactly under `Application/Shared/`

**Options considered:**
1. Flat file: `Application/Shared/InfrastructureConfigurationKeys.cs` (mirrors `ErrorCodes.cs`, `BaseResponse.cs` placement — flat siblings, no subfolder).
2. A new subfolder, e.g. `Application/Shared/Infrastructure/InfrastructureConfigurationKeys.cs`.

**Chosen approach:** Option 1 — flat, at the same level as `ErrorCodes.cs`.

**Rationale:** `Application/Shared/` already has subfolders (`Rag/`, `Http/`, `Json/`, `Printing/`, `Users/`, `WebSearch/`) for *categories* with multiple related files. A single 6-line class with no siblings of its own does not warrant a new subfolder — that would be over-structuring a two-constant lookup table. Flat placement matches `ErrorCodes.cs` (also a small static constants holder) exactly.

## Implementation Guidance

### Directory / Module Structure

- **Delete**: `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`
- **Create**: `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs`, with only the namespace line changed (`Anela.Heblo.Domain.Shared` → `Anela.Heblo.Application.Shared`); the three `const string` members and their values are byte-for-byte unchanged.
- No other file in `Domain/Shared/` (`Cooling.cs`, `CurrencyCode.cs`, `Result.cs`, `Rag/`) is touched — those remain genuine domain-shared types per `filesystem.md`'s documented distinction.
- No `.csproj` changes anywhere.

### Interfaces and Contracts

No interface or contract changes — this is a `static class` of `const string` fields, not a service with a DI-registered interface. The public shape (`InfrastructureConfigurationKeys.APP_VERSION`, `.USE_MOCK_AUTH`, `.BYPASS_JWT_VALIDATION`) is preserved exactly; only the namespace consumers must `using` changes.

Update `using Anela.Heblo.Domain.Shared;` → `using Anela.Heblo.Application.Shared;` in exactly these 10 files (verified by grep against the full `backend/src` + `backend/test` tree — no additional consumers exist):

1. `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
2. `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
3. `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs`
4. `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`
5. `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
6. `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
7. `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
8. `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`
9. `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`
10. `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

Each file has exactly one `using Anela.Heblo.Domain.Shared;` line used solely for this symbol — confirmed no other `Domain.Shared` type (`Cooling`, `CurrencyCode`, `Result`) is referenced in any of the 10, so the straight substitution is safe everywhere. If `dotnet format`'s `using`-ordering rules reshuffle the block as a side effect of the substitution, that is acceptable; no other manual reordering or unrelated edits should be made.

### Data Flow

No data flow changes. The three constants are read the same way at the same call sites (env var lookup, `IConfiguration` reads for `UseMockAuth`/`BypassJwtValidation`, CI/CD-set `APP_VERSION`). This is purely a compile-time symbol-resolution change — nothing crosses a runtime boundary differently before vs. after.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A missed consumer outside the 10 listed files causes a build break | Low | Full-tree grep for `InfrastructureConfigurationKeys` (not just `using Anela.Heblo.Domain.Shared;`) after the edit must return zero hits referencing the old namespace; `dotnet build` on the whole solution is the authoritative check. |
| One of the 10 files also uses another `Domain.Shared` symbol, so blindly replacing the `using` line breaks that reference | Low | Already checked during this review — none of the 10 files reference `Cooling`, `CurrencyCode`, or `Result`. Still, re-verify per-file if `dotnet build` reports an unresolved symbol in any of them; the fix is to *add back* `using Anela.Heblo.Domain.Shared;` alongside the new one, not to revert the move. |
| Architecture-boundary test suite (`ModuleBoundariesTests.cs` or similar reflection-based tests) has a rule pinned to `Domain.Shared` types that inadvertently also asserts something about this class | Low | Run the full `Anela.Heblo.Tests` suite, specifically anything under `Architecture/`, after the move — not just `GetConfigurationHandlerTests`. |
| `dotnet format` reorders `using` blocks beyond the single line, producing a noisy diff | Cosmetic | Acceptable per spec — run `dotnet format` and accept its output; do not hand-tune beyond that. |

No risk is rated above Low: this is a mechanical, compiler-verifiable refactor with a closed, confirmed consumer set.

## Specification Amendments

None. The spec (`spec.r1.md`, Status: COMPLETE) is accurate and sufficiently detailed — its consumer list, project-reference analysis, and acceptance criteria were independently re-verified during this review (grep for all references, inspection of the three `.csproj` files, inspection of the current `Domain/Shared/InfrastructureConfigurationKeys.cs` contents) and found correct with no discrepancies. Proceed with the spec as written.

## Prerequisites

None. No migrations, config, or infrastructure setup is needed — this is a same-commit source move with no external dependencies. The only "prerequisite" is running `dotnet build` (and ideally `dotnet format`) immediately after the file move + `using` edits to catch any missed reference before committing.
