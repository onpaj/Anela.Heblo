# Design: Remove non-zero `LastModified` defaults on Dashboard domain entities

This is a backend-only change with no user-facing component (confirmed by the architecture review: `Skip Design: true`). No UX/UI section applies.

## Component Design

No new components. Two existing domain entity types in `Anela.Heblo.Domain.Features.Dashboard` change their construction-time default for one property each:

- **`UserDashboardTile`** — its `LastModified` property (`DateTime`) loses the `= DateTime.UtcNow` initializer and defaults to `default(DateTime)`.
- **`UserDashboardSettings`** — its `LastModified` property (`DateTime`) loses the `= DateTime.UtcNow` initializer and defaults to `default(DateTime)`.

Responsibilities of both types are otherwise unchanged: they remain plain EF Core-mapped entities, and the responsibility for stamping a correct `LastModified` value at write time remains entirely with the call sites that already own it —

- `SaveUserSettingsHandler` (assigns via `_timeProvider.GetUtcNow().DateTime`)
- `GetUserSettingsHandler` (assigns via a `TimeProvider`-derived `now`)
- `UserDashboardSettingsMutator` (documented owner of `LastModified` stamping per its XML doc comments; assigns via a `TimeProvider`-derived `now`)

No component's interface or contract changes: `IUserDashboardSettingsMutator`, the MediatR handlers' request/response shapes, and the EF Core `IEntityTypeConfiguration<T>` classes are untouched.

## Data Schemas

No schema change. `LastModified` remains a non-nullable `DateTime`, mapped via `.AsUtcTimestamp().IsRequired()` in both `UserDashboardTileConfiguration` and `UserDashboardSettingsConfiguration`, with no database-level default value in either configuration — so no EF Core migration is required.

The only observable difference is the **in-memory default of a newly constructed, not-yet-assigned instance**:

| Type | Property | Old default (construction time) | New default (construction time) |
|---|---|---|---|
| `UserDashboardTile` | `LastModified` | `DateTime.UtcNow` (real wall-clock time) | `DateTime.MinValue` (`0001-01-01T00:00:00`) |
| `UserDashboardSettings` | `LastModified` | `DateTime.UtcNow` (real wall-clock time) | `DateTime.MinValue` (`0001-01-01T00:00:00`) |

`UserDashboardSettingsDto.LastModified` (the API-facing shape) is unaffected — it is a plain DTO property populated explicitly by handlers from the entity's actual, already-stamped value; it has no default of its own tied to this change.
