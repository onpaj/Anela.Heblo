# Specification: Remove non-zero `LastModified` defaults on Dashboard domain entities

## Summary
`UserDashboardTile` and `UserDashboardSettings` default their `LastModified` property to `DateTime.UtcNow` via a C# property initializer, which captures real wall-clock construction time instead of the intended write time. This is inconsistent with the module's `TimeProvider`-based time discipline and is a silent-failure risk if a future handler forgets to explicitly set `LastModified` before persisting. This spec covers removing the non-zero defaults so the properties default to `DateTime.MinValue` (the implicit `DateTime` default), making an omitted assignment immediately and obviously wrong rather than plausible.

## Background
Per issue #4316 (filed by the daily arch-review routine), both domain entities in `backend/src/Anela.Heblo.Domain/Features/Dashboard/`:

- `UserDashboardTile.cs` line 11: `public DateTime LastModified { get; set; } = DateTime.UtcNow;`
- `UserDashboardSettings.cs` line 8: `public DateTime LastModified { get; set; } = DateTime.UtcNow;`

use a property initializer that evaluates `DateTime.UtcNow` at object-construction time, not at the time the entity is actually persisted. Every current handler (`SaveUserSettingsHandler`, `GetUserSettingsHandler`, `UserDashboardSettingsMutator`) already overrides this value using the injected `TimeProvider.GetUtcNow()` before writing, so there is no known production bug today. The risk is latent: nothing prevents a future handler from omitting the explicit assignment, in which case the entity would silently persist the construction-time value instead of the intended write-time value, and unit tests using `FakeTimeProvider` would not actually control the timestamp for an entity constructed directly in the test body.

## Functional Requirements

### FR-1: Remove non-zero default from `UserDashboardTile.LastModified`
Change `UserDashboardTile.cs` line 11 from
```csharp
public DateTime LastModified { get; set; } = DateTime.UtcNow;
```
to
```csharp
public DateTime LastModified { get; set; }
```
**Acceptance criteria:**
- The property has no initializer; it defaults to `default(DateTime)` (`DateTime.MinValue`, i.e. `0001-01-01T00:00:00`).
- No other property or behavior of `UserDashboardTile` changes.

### FR-2: Remove non-zero default from `UserDashboardSettings.LastModified`
Change `UserDashboardSettings.cs` line 8 from
```csharp
public DateTime LastModified { get; set; } = DateTime.UtcNow;
```
to
```csharp
public DateTime LastModified { get; set; }
```
**Acceptance criteria:**
- The property has no initializer; it defaults to `default(DateTime)`.
- No other property or behavior of `UserDashboardSettings` changes.

### FR-3: Preserve existing handler behavior
No handler, mutator, or other call site changes. `SaveUserSettingsHandler`, `GetUserSettingsHandler`, and `UserDashboardSettingsMutator` already assign `LastModified` explicitly via `TimeProvider.GetUtcNow()` before persistence, so their observable behavior (and the values written to the database) is unchanged by this fix.
**Acceptance criteria:**
- Existing unit/integration tests covering these handlers continue to pass unmodified, or are updated only if they happened to assert on the pre-fix default value (see Open Questions).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a property-initializer removal with no runtime cost implications.

### NFR-2: Security
Not applicable — no auth or data-sensitivity change.

### NFR-3: Testability
The change strengthens testability: any test that constructs a `UserDashboardTile` or `UserDashboardSettings` directly (without an explicit `LastModified` assignment) will now observe `DateTime.MinValue`, making an accidentally-omitted assignment in test setup or in a new handler immediately visible rather than passing silently with a plausible-looking real timestamp.

## Data Model
No schema change. `LastModified` remains a `DateTime` column on both entities; only its CLR default value changes (construction-time default, not persisted default — EF Core migrations are unaffected since the column type and nullability are unchanged). If a row is ever persisted without `LastModified` being explicitly set, it will now store `0001-01-01` instead of the construction timestamp, which is intentional per the "obviously wrong, immediately caught" design in the issue's suggested fix.

## API / Interface Design
No API surface change. No controller, MediatR request/response, or DTO is affected. `LastModified` continues to be set explicitly by:
- `SaveUserSettingsHandler` (line 57)
- `GetUserSettingsHandler` (line 40)
- `UserDashboardSettingsMutator` (line 49)

## Dependencies
None. This is a self-contained two-line change within `Anela.Heblo.Domain`. No dependency on `TimeProvider` registration or DI changes — those already exist and are unaffected.

## Out of Scope
- Auditing or changing any other domain entity's `LastModified`-style defaults outside the Dashboard module (not raised by this issue).
- Adding compile-time enforcement (e.g., a required constructor parameter, an analyzer rule) that `LastModified` must be set before persistence — the issue's suggested fix is the minimal, low-risk change; a stronger enforcement mechanism is a separate, larger design decision not requested here.
- Backfilling or migrating any existing persisted rows that currently hold a `DateTime.UtcNow`-at-construction value — this change only affects the in-memory default for newly constructed, not-yet-persisted objects going forward.

## Open Questions
None.

## Status: COMPLETE
