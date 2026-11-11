# Architecture Review: Dashboard settings provisioning separation

## Skip Design: true
Backend-only refactor, no UI.

## Architectural Fit Assessment
Fits the vertical-slice layout (`Features/Dashboard/{UseCases,Infrastructure}`) and existing precedent: `UserDashboardSettingsMutator` is already an internal infrastructure service registered in `DashboardModule`. Verified in code: only `GetTileDataHandler` and `UserDashboardSettingsMutator` (two methods) dispatch `GetUserSettingsRequest`; Enable/Disable/Save handlers go through the mutator.

## Proposed Architecture

### Component Overview
```
GET settings -> GetUserSettingsHandler -> repo.GetByUserIdAsync -> DashboardDefaults.Project(settings?, registry) -> DTO   (no writes, no lock)
GetTileDataHandler -> same read path (no IMediator)
Enable/Disable/Save -> Mutator -> Provisioner.EnsureProvisionedAsync (lock inside, released) -> Mutator lock -> load -> mutate -> UpdateAsync
```

### Key Design Decisions

#### Decision 1: Pure-read handler with in-memory default projection
**Options considered:** (a) handler delegates to provisioner (still writes on GET); (b) handler is pure read, projecting defaults in memory; provisioning only on write paths.
**Chosen approach:** (b).
**Rationale:** (a) leaves the read-with-write-side-effect and the read-only-replica risk intact, which is the finding's core. With (b), a user with no row sees the same default tiles; the row is persisted on first mutation, which is when it is needed. Deviation from the issue's suggestion is deliberate and noted in the spec's Out of Scope.

#### Decision 2: One shared default-tile computation
Create an internal static helper (e.g. `UserDashboardSettingsDefaults`) in `Features/Dashboard/Infrastructure` exposing (1) `BuildDefaultSettings(userId, tiles, now)` and (2) `GetMissingAutoShowTiles(settings, tiles, now)` logic, used by the provisioner (persisting) and a read projection (non-persisting), to avoid drift.

#### Decision 3: Lock ownership
Provisioner acquires the lock internally and releases before returning; mutator calls it before taking its own lock (same invariant as today, now documented on the provisioner). Read path takes no lock.

#### Decision 4: Read service for GetTileData
Add `IUserDashboardSettingsReader.GetEffectiveSettingsAsync(userId, ct)` (internal) returning the projected `UserDashboardSettingsDto`, used by both `GetUserSettingsHandler` and `GetTileDataHandler`. Avoids handler-to-handler chaining.

## Implementation Guidance

### Directory / Module Structure
- New: `Infrastructure/IUserDashboardSettingsProvisioner.cs`, `UserDashboardSettingsProvisioner.cs`, `IUserDashboardSettingsReader.cs`, `UserDashboardSettingsReader.cs`, `UserDashboardSettingsDefaults.cs`.
- Modify: `GetUserSettingsHandler`, `GetTileDataHandler`, `UserDashboardSettingsMutator` (+ interface remarks), `DashboardModule`.
- Tests in `backend/test/Anela.Heblo.Tests/Features/Dashboard/`.

### Interfaces and Contracts
- `Task<UserDashboardSettings> EnsureProvisionedAsync(string userId, CancellationToken ct)`
- `Task<UserDashboardSettingsDto> GetEffectiveSettingsAsync(string userId, CancellationToken ct)` (userId normalisation to "anonymous" stays with callers/handler as today).
- Internal visibility consistent with the mutator (`InternalsVisibleTo` for tests already used by mutator tests; verify).

### Data Flow
See component overview. Behaviour for existing, provisioned users is byte-identical.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| GET no longer persists default row; other code assuming a row exists | Medium | grep shows only mutator/GetTileData consumers; mutator provisions first |
| Projection drifts from persisted logic | Low | Single shared helper + tests asserting equivalence |
| Lock deadlock if provisioner called inside lock | Medium | Document invariant; mutator order unchanged; test |
| Existing tests mocking IMediator break | Low | Update in same change |

## Specification Amendments
Spec FR-2 adopts the in-memory projection (Decision 1); FR-3 uses the reader (Decision 4).

## Prerequisites
None (no migration). Per CLAUDE.md, run `dotnet build` and `dotnet format`; no process doc change expected unless `docs/processes/` owns the Dashboard settings code (check `scripts/process-docs/check.py`).
