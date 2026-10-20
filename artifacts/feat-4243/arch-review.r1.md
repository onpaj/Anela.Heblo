# Architecture Review: Use ConfigurationConstants defaults in ApplicationConfiguration.CreateWithDefaults

## Skip Design: true

## Architectural Fit Assessment
This is a pure intra-class DRY cleanup inside the Domain layer, with no architectural surface at all. `ApplicationConfiguration` and `ConfigurationConstants` already live in the same file directory and the same namespace (`Anela.Heblo.Domain.Features.Configuration`), so this isn't even a cross-module or cross-layer dependency question — it's a reference from one static member to another within a module that already exists for exactly this purpose (`ConfigurationConstants` is documented as "Configuration constants and keys"). The single call site, `GetConfigurationHandler.cs` in the Application layer, is unaffected: it calls `CreateWithDefaults(version, environment, useMockAuth)` and receives identical output before and after. No new integration points, no new dependencies, no layering decision to make.

## Proposed Architecture

### Component Overview
No component-level change. Existing shape, unchanged:

```
Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
        │  calls ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth)
        ▼
Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs   (factory method — fix here)
        │  references
        ▼
Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs     (already defines DEFAULT_VERSION / DEFAULT_ENVIRONMENT)
```

### Key Design Decisions

#### Decision 1: Reference the existing constant vs. any alternative
**Options considered:**
- Leave the literals as-is (rejected — that's the bug being fixed).
- Introduce a new configuration-defaults abstraction, options pattern, or `IConfiguration`-bound default (rejected — wildly disproportionate; no such need exists, and the spec explicitly scopes this out).
- Reference `ConfigurationConstants.DEFAULT_VERSION` / `ConfigurationConstants.DEFAULT_ENVIRONMENT` directly (chosen).

**Chosen approach:** Swap the two string literals in `CreateWithDefaults` for the two existing constants. Nothing else changes.

**Rationale:** The constants class already exists in the same namespace for exactly this purpose and already holds the identical values. This is the smallest possible change that removes the duplication; anything larger would be scope creep on a one-line-per-fallback fix.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Edit only:
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs` (lines 24–27)

### Interfaces and Contracts
No interface, signature, or contract changes. `CreateWithDefaults(string? version, string? environment, bool useMockAuth)` keeps its exact signature and return type. The one and only call site (`GetConfigurationHandler.cs`) requires no changes.

```csharp
public static ApplicationConfiguration CreateWithDefaults(string? version, string? environment, bool useMockAuth)
{
    return new ApplicationConfiguration(
        version ?? ConfigurationConstants.DEFAULT_VERSION,
        environment ?? ConfigurationConstants.DEFAULT_ENVIRONMENT,
        useMockAuth
    );
}
```

### Data Flow
Unchanged. `GetConfigurationHandler` still resolves `version`/`environment` from its existing sources, passes them into `CreateWithDefaults`, and gets back an `ApplicationConfiguration` with the same fallback values as before (`"1.0.0"` / `"Production"`), now sourced from the shared constant instead of a duplicated literal.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| None identified — same namespace, no new dependency, values unchanged, single call site unaffected | N/A | N/A |

## Specification Amendments
None. The spec (`spec.r1.md`) is accurate, correctly scoped, and requires no architectural changes or additions.

## Prerequisites
None. No migrations, config, or infrastructure needed — this is a same-file-directory, same-namespace literal-to-constant swap that can be implemented immediately.
