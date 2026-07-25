# Design: Fix residual `PhotobankRepository.SaveChangesAsync` DateTime Kind=Unspecified exception

No UI is involved — this is a persistence-layer configuration fix plus one EF Core migration and a
regression test. The UX/UI section is omitted.

## Component design

Three components are touched; none change their public interface or callers' behavior.

### 1. `PhotobankIndexRootConfiguration` (EF entity type configuration)

`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankIndexRootConfiguration.cs`

Responsibility: declare the PostgreSQL column mapping for `PhotobankIndexRoot`. Currently every
`DateTime`/`DateTime?` property except `LastIndexedAt` is explicitly opted into `timestamp` (without
time zone) via `AsUtcTimestamp()` (`DateTimeConfigurationExtensions.cs`). `LastIndexedAt` was left on
Npgsql's implicit default (`timestamp with time zone`), which conflicts with the DbContext-wide
`ValueConverter` that forces every `DateTime` to `Kind=Unspecified` before write
(`ApplicationDbContext.cs:186-208`). Npgsql rejects `Kind=Unspecified` writes to `timestamptz` columns —
this is the exact exception in the signal.

Change:

```csharp
// before
builder.Property(x => x.LastIndexedAt);

// after
builder.Property(x => x.LastIndexedAt).AsUtcTimestamp();
```

This is a one-line change to an existing fluent chain — no new methods, no new abstractions. The fix
mirrors the pattern already applied to `Photo.TakenAt` / `Photo.LastAutoTaggedAt` in PR #3330 and to
`PhotobankIndexRoot.CreatedAt` in the same file.

Interface: `IEntityTypeConfiguration<PhotobankIndexRoot>.Configure(EntityTypeBuilder<PhotobankIndexRoot>)`
— unchanged signature, called automatically by EF's `ApplyConfigurationsFromAssembly` during model
building. No caller-visible change.

### 2. EF Core migration

New file `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AlignPhotobankIndexRootTimestampWithoutTimeZone.cs`
(+ matching `.Designer.cs` and `ApplicationDbContextModelSnapshot.cs` update), generated via
`dotnet ef migrations add`.

Responsibility: alter the physical `PhotobankIndexRoots."LastIndexedAt"` column from
`timestamp with time zone` to `timestamp`, without shifting the stored instant. Follows the exact
pattern of `20260624115315_AlignPhotoTimestampsWithoutTimeZone.cs` (PR #3330) — raw SQL via
`migrationBuilder.Sql(...)` rather than the EF-generated `AlterColumn<>` call, because the `USING ...
AT TIME ZONE 'UTC'` cast is required to reinterpret existing `timestamptz` values as UTC-naive instead
of converting them to the DB session's local timezone.

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        "ALTER TABLE public.\"PhotobankIndexRoots\" " +
        "ALTER COLUMN \"LastIndexedAt\" TYPE timestamp USING \"LastIndexedAt\" AT TIME ZONE 'UTC';");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        "ALTER TABLE public.\"PhotobankIndexRoots\" " +
        "ALTER COLUMN \"LastIndexedAt\" TYPE timestamp with time zone USING \"LastIndexedAt\" AT TIME ZONE 'UTC';");
}
```

`LastIndexedAt` is nullable (`DateTime?`) — `ALTER COLUMN ... TYPE ... USING` preserves `NULL` rows
unchanged under this cast, so no special-casing is needed (same nullability shape as `Photo.TakenAt`,
which the reference migration already handles).

Not run automatically at deploy (project fact — migrations are manual); must be applied against
staging/production by hand after merge, called out in the PR description.

### 3. Schema regression test

`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs`

Responsibility: fail fast (unit test, no DB connection needed) if any Photobank `DateTime` column is
ever added or left without `AsUtcTimestamp()`, instead of surfacing only as a nightly-job production
exception. The existing test class already does this for `Photo`; extend coverage to
`PhotobankIndexRoot` using the same `NewNpgsqlContext()` helper and assertion shape (no new helper
needed — reuse in place).

```csharp
[Theory]
[InlineData(nameof(PhotobankIndexRoot.CreatedAt))]
[InlineData(nameof(PhotobankIndexRoot.LastIndexedAt))]
public void PhotobankIndexRoot_DateTimeColumns_AreTimestampWithoutTimeZone(string propertyName)
{
    using var db = NewNpgsqlContext();

    var property = db.Model
        .FindEntityType(typeof(PhotobankIndexRoot))!
        .FindProperty(propertyName)!;

    property.GetColumnType().Should().Be(
        "timestamp",
        $"{propertyName} stores UTC and must map to 'timestamp without time zone' to match the " +
        "global UTC->Unspecified converter; 'timestamp with time zone' rejects Unspecified writes");
}
```

Placed as a second `[Theory]` in the same file, per the plan's default (lower churn than a sibling
file; the class comment header can stay as-is since it already documents the general failure mode, not
just `Photo` specifically).

## Data schema

Single column type change on one existing table. No new tables, no DTO/API/event payload changes —
`LastIndexedAt` is not exposed through `PhotoLocator` or any other API-facing contract.

| Entity | Column | Nullable | Before | After |
|---|---|---|---|---|
| `PhotobankIndexRoot` | `LastIndexedAt` | yes (`DateTime?`) | `timestamp with time zone` (implicit default) | `timestamp` (explicit, UTC-naive) |

CLR-level behavior is unchanged: `PhotobankIndexJob.IndexRootAsync` continues to set
`root.LastIndexedAt = DateTime.UtcNow` and the global `ValueConverter` continues to strip/restore
`DateTimeKind` on write/read — only the physical column type changes, aligning it with the convention
the converter assumes.

## Verification

- New test fails against current code (missing `AsUtcTimestamp()` on `LastIndexedAt`), passes after the
  config change — confirms the test actually guards the regression.
- `dotnet build` + `dotnet format`; full Photobank test suite green (including `PhotobankIndexJob`
  batching tests from #3692/#3697).
- Post-deploy: re-run the signal's App Insights query for several nightly runs to confirm the exception
  count reaches and stays at zero.
