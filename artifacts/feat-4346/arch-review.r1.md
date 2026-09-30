# Architecture Review: Fix CS1503 build break in RecurringJobSeeder

## Skip Design: true
Backend-only, one-token compile fix; no UI.

## Architectural Fit Assessment
Verified in code: `existingByName.TryGetValue(config.JobName, out var existingConfig)` yields the per-job entity, and `HasSeededFieldsChanged(RecurringJobConfiguration existing, RecurringJobConfiguration config)` is a private static comparer of DisplayName/Description/TimeZoneId. The call site simply uses the wrong variable. No architectural impact.

## Proposed Architecture

### Component Overview
Unchanged: `RecurringJobSeeder` -> `IRecurringJobConfigurationRepository` (batch `GetAllAsync`, `AddAsync`, `UpdateAsync`).

### Key Design Decisions

#### Decision 1: Minimal call-site fix
**Options considered:** (a) pass `existingConfig`; (b) change the helper signature; (c) restructure the loop.
**Chosen approach:** (a).
**Rationale:** `existingConfig` is definitely assigned in the `else` branch of the `TryGetValue` negation, has the right type, and matches the helper's intent. Other options touch more than required.

## Implementation Guidance

### Directory / Module Structure
Edit only `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` line 51.

### Interfaces and Contracts
None changed.

### Data Flow
GetAllAsync -> dictionary by JobName -> per default config: missing => AddAsync; present and seeded fields differ => UpdateConfiguration + UpdateAsync; else untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Other latent compile errors on main hidden by this one | Low | Run full `dotnet build` and `dotnet format` after the fix |
| Behaviour change | Low | Run `RecurringJobSeederTests` |

## Specification Amendments
None.

## Prerequisites
None.
