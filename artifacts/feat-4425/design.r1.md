# Design: JournalEntry clock injection

## Component Design
- `JournalEntry` (Domain): `Update` and `SoftDelete` gain a required trailing `DateTime now`. `Update` sets `ModifiedAt = now`. `SoftDelete` sets `DeletedAt = now` and `ModifiedAt = now`. Other behaviour (trim, `EntryDate.Date`, audit user fields, `IsDeleted = true`) is unchanged.
- `UpdateJournalEntryHandler` and `DeleteJournalEntryHandler` (Application): declare `var now = DateTime.UtcNow;` before the entity call and pass it, mirroring `CreateJournalEntryHandler`.
- Tests: entity tests pass a fixed `now` (for example `new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc)`) and assert exact equality. Handler tests keep their before/after window, because the handler still reads the system clock.

## Data Schemas
No schema, DTO, or API change. No migration and no OpenAPI regeneration.
