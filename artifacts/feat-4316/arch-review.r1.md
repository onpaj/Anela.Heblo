# Architecture Review: Remove non-zero `LastModified` defaults on Dashboard domain entities

## Skip Design: true

This is a backend-only, two-line property-initializer removal in the Domain layer. There is no new or changed UI/UX surface, no new endpoint, and no visual component involved. Design phase should be skipped.

## Architectural Fit Assessment

The proposed change aligns exactly with an existing, documented project convention rather than introducing a new one. `docs/architecture/Dev_Guidelines_time.md` ("Database Entity Guidelines") shows the canonical pattern for entity timestamp properties:

```csharp
public class MyEntity
{
    // ✅ CORRECT - Stored as UTC, converted for display as needed
    public DateTime CreatedAt { get; set; }

    // When setting values, always use UTC:
    // entity.CreatedAt = _timeProvider.GetUtcNow().DateTime;
}
```

i.e. **no property initializer**, with the value always assigned explicitly via the injected `TimeProvider` at the call site. `UserDashboardTile.LastModified` and `UserDashboardSettings.LastModified` are the only two properties in the Dashboard module (confirmed by a full-module `LastModified` grep) that deviate from this documented pattern by defaulting to `DateTime.UtcNow` at construction. The fix brings both entities into conformance with the module's own written guideline — it is a conformance fix, not a new architectural decision.

The only integration points are:
- EF Core mapping (`UserDashboardTileConfiguration`, `UserDashboardSettingsConfiguration`) — both already call `.IsRequired()` with no `.HasDefaultValue(...)` at the database level, so the column's DB-level behavior is untouched; only the CLR default for a not-yet-persisted, not-yet-assigned instance changes.
- The three call sites that assign `LastModified` explicitly today (`SaveUserSettingsHandler:57/67/75`, `GetUserSettingsHandler:52/59/89/94`, `UserDashboardSettingsMutator:55/56/81/83`) — all already use `_timeProvider.GetUtcNow().DateTime` / a `now` variable sourced from `TimeProvider`, so none of them observe the property-initializer default today and none require modification.

## Proposed Architecture

### Component Overview

No component, layer, or module boundary changes. The two affected types remain exactly where they are:

```
Anela.Heblo.Domain
 └─ Features/Dashboard/
     ├─ UserDashboardTile.cs        (LastModified initializer removed)
     └─ UserDashboardSettings.cs    (LastModified initializer removed)

Anela.Heblo.Application (unchanged)
 └─ Features/Dashboard/
     ├─ UseCases/SaveUserSettings/SaveUserSettingsHandler.cs   (already sets LastModified via TimeProvider)
     ├─ UseCases/GetUserSettings/GetUserSettingsHandler.cs     (already sets LastModified via TimeProvider)
     └─ Infrastructure/UserDashboardSettingsMutator.cs         (already sets LastModified via TimeProvider)

Anela.Heblo.Persistence (unchanged)
 └─ Dashboard/
     ├─ UserDashboardTileConfiguration.cs      (IsRequired(), no DB default — unaffected)
     └─ UserDashboardSettingsConfiguration.cs   (IsRequired(), no DB default — unaffected)
```

### Key Design Decisions

#### Decision 1: Default to `DateTime.MinValue` (remove initializer) rather than making the property non-nullable-but-required via another mechanism
**Options considered:**
1. Remove the `= DateTime.UtcNow` initializer, letting the property fall back to `default(DateTime)` (`DateTime.MinValue`).
2. Make `LastModified` a required constructor parameter so it cannot be omitted at all.
3. Add a nullable `DateTime?` with no default, forcing every read site to handle `null`.

**Chosen approach:** Option 1, exactly as specified in the issue and in `spec.r1.md` FR-1/FR-2.

**Rationale:** Option 1 is the minimal change that fixes the actual defect (a plausible-looking but wrong default) without touching any other code. Option 2 would require changing every construction site (`new UserDashboardTile { ... }`, `new UserDashboardSettings { ... }`) across handlers and tests, which is out of proportion to the issue and risks regressions in an otherwise-working area. Option 3 would ripple `DateTime?`-handling into the DTO (`UserDashboardSettingsDto.LastModified`, currently non-nullable `DateTime`), the EF Core mapping (`.IsRequired()` already enforces non-null at the DB layer, making `DateTime?` redundant), and every comparison/formatting call site, for no behavioral benefit over Option 1. `DateTime.MinValue` is a well-established "obviously uninitialized" sentinel in .NET and is exactly what the issue asks for.

## Implementation Guidance

### Directory / Module Structure
No new files. Edit in place:
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs`
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs`

### Interfaces and Contracts
No interface or contract changes. `IUserDashboardSettingsMutator` (which explicitly documents in its XML doc comments that it "owns" `LastModified` stamping) is unaffected — it already sets the value itself and never relies on the constructor default.

### Data Flow
Unchanged. Every write path already flows: handler/mutator resolves `_timeProvider.GetUtcNow().DateTime` → assigns to `entity.LastModified` → `UpdateAsync`/`AddAsync` persists it. The only behavioral difference after this change is for a *hypothetical* future or existing code path that constructs one of these entities and persists it **without** assigning `LastModified` — that path will now write `0001-01-01` (obviously wrong, fails review/tests) instead of silently writing a plausible real-looking timestamp. No such path exists today (verified below).

## Verification performed during this review
Grepped the full Dashboard module (`Anela.Heblo.Domain`, `Anela.Heblo.Application`, `Anela.Heblo.Persistence`) and its test suite (`Anela.Heblo.Tests/Features/Dashboard/*`) for every `LastModified` reference:
- All four production call sites that assign `LastModified` already source the value from `TimeProvider`, not from the constructor default — confirmed by direct file reads of `SaveUserSettingsHandler.cs`, `GetUserSettingsHandler.cs`, and `UserDashboardSettingsMutator.cs`.
- Every test construction of `UserDashboardTile`/`UserDashboardSettings` in `GetUserSettingsHandlerTests.cs`, `EnableTileHandlerTests.cs`, `DisableTileHandlerTests.cs`, and `SaveUserSettingsHandlerTests.cs` explicitly sets `LastModified` (to `FixedUtcNow`, `DateTime.UtcNow`, or an offset thereof) — none relies on the property-initializer default, so no test needs to change.
- Both EF Core `IEntityTypeConfiguration` classes call `.IsRequired()` with no `.HasDefaultValue(...)`, confirming no database-level default value or migration is implicated.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A currently-unknown code path constructs one of these entities and persists it without setting `LastModified`, and would now silently write `0001-01-01` instead of a real-looking (but still wrong) timestamp | Low | Grep above found no such path today. The issue's own premise is that this failure mode should be *loud* rather than silent — `0001-01-01` in a database row or test assertion is far more visible and will fail review/tests immediately, which is the intended and desired outcome, not a regression. |
| None — no DB schema, migration, API contract, or DI change is involved | N/A | N/A |

## Specification Amendments
None. `spec.r1.md` is accurate and complete as written; the exploration above confirms every claim in it (all handlers already override the default; no test asserts on the old default; no DB-level default value exists).

## Prerequisites
None. No migration, config, or infrastructure change is required before implementation can start — this is a two-line, self-contained code change ready to implement immediately.
