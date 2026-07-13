# Architecture Review: Rename `LogisticsModule.AddTransportModule()` to `AddLogisticsModule()`

## Skip Design: true

No UI/UX surface. This is a backend identifier rename confined to DI composition wiring and two documentation code samples.

## Architectural Fit Assessment

Verified directly against the working tree — all claims in brief and spec hold exactly as stated:

- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs:17` declares `public static IServiceCollection AddTransportModule(this IServiceCollection services)` inside `public static class LogisticsModule`.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs:92` is the single call site: `services.AddTransportModule();`.
- `docs/architecture/development_guidelines.md:158` and `docs/architecture/infrastructure.md:143` both show `.AddTransportModule()` inside their respective "API Composition" fluent-chain examples.
- A survey of every `Add{Feature}Module` extension method under `Features/` (29+ modules: Catalog, Purchase, Manufacture, Packaging, Journal, Bank, etc.) confirms `LogisticsModule` is the sole outlier — every other module follows `{Feature}Module.Add{Feature}Module()`. The spec's claim of a codebase-wide convention is accurate, not asserted.
- A repo-wide grep for `AddTransportModule` turns up exactly the four in-scope locations (method decl, call site, two docs) plus out-of-scope historical references correctly excluded by the spec: `docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md` (a dated completed-plan record) and unrelated prior arch-review artifacts under `artifacts/` (not living documentation, not part of this change's blast radius).

This is a same-file, same-line, compiler-checked rename with no behavioral, data, or contract surface. It fits the codebase's existing convention rather than introducing one — there is no architectural decision to make here beyond confirming the rename is total (no dangling reference left behind).

## Proposed Architecture

### Component Overview

No new components. Four textual edits across three files:

1. `LogisticsModule.cs` — rename the method declaration.
2. `ApplicationModule.cs` — rename the call site.
3. `docs/architecture/development_guidelines.md` — rename in the example code block.
4. `docs/architecture/infrastructure.md` — rename in the example code block.

### Key Design Decisions

#### Decision 1: No compatibility shim
**Options considered:** (a) rename outright, (b) keep `AddTransportModule()` as a deprecated pass-through wrapper calling the new `AddLogisticsModule()`.
**Chosen approach:** (a) rename outright, no shim.
**Rationale:** This is an internal DI composition method with exactly one call site in the same solution, not a published API or NuGet contract (confirmed by NFR-3 in the spec and by the grep above). A compat shim would add permanent dead code for a rename that a full solution build catches immediately if missed. Matches the "surgical changes" principle in `CLAUDE.md` — don't add structure the task doesn't require.

## Implementation Guidance

### Directory / Module Structure

No structural change. Same files, same locations:
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- `docs/architecture/development_guidelines.md`
- `docs/architecture/infrastructure.md`

### Interfaces and Contracts

Single signature change, identifier only:

```csharp
// Before
public static IServiceCollection AddTransportModule(this IServiceCollection services)

// After
public static IServiceCollection AddLogisticsModule(this IServiceCollection services)
```

Method body, parameters, and return type are unchanged. No DTOs, no OpenAPI surface, no MediatR handlers involved — the "DTOs are classes not records" rule and API-client-generation concerns from `docs/architecture/development_guidelines.md` do not apply here.

### Data Flow

Unchanged. This method only wires DI registrations (repositories, adapters, dashboard tiles, background refresh task) at startup; none of those registrations change, only the name developers use to invoke them.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed reference causes build failure | Low | `dotnet build` fails immediately on any stale `AddTransportModule()` reference (it's a compiled extension method, not a string) — the repo-wide grep above found only the four in-scope files plus explicitly out-of-scope historical docs, so a clean build is sufficient verification. |
| Stale mentions in non-living docs (`docs/superpowers/plans/2026-06-01-...md`, prior arch-review artifacts under `artifacts/`) | Negligible | Correctly out of scope per spec — these are dated historical records, not templates developers copy from. No action needed. |

## Specification Amendments

None. The spec's file paths, line numbers, and scope boundaries were all verified against the working tree and are accurate.

## Prerequisites

None. No sequencing dependency on other in-flight work.
