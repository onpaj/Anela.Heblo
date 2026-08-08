# Photobank DateTime Kind=Unspecified — Diagnostic & Boundary Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the 4th blind guess at issue #3757 by shipping a diagnostic that names the exact failing SQL parameter, lock in the model invariant that already rules out the theory the previous three fixes were built on, and harden the one untrusted `DateTime` entry point in the Photobank index path.

**Architecture:** Three independent, additive changes. (1) A `DbCommandInterceptor` registered next to the existing `PostgresExceptionLoggingInterceptor` that logs every parameter's store type, CLR type and `DateTime.Kind` when a command fails — this is what turns the next nightly run into a definitive answer instead of another hypothesis. (2) A model-wide xUnit invariant test generalising the existing `PhotoSchemaTests` from 6 hand-listed properties to *every* `DateTime` property in the model. (3) A UTC normalisation at the Microsoft Graph JSON boundary in `PhotobankGraphService`, the only externally-sourced `DateTime` reaching the failing `SaveChangesAsync`.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, xUnit + FluentAssertions + Moq, `System.Text.Json`.

---

## Background — why the previous three fixes could not have worked

Read this before writing code. It is the reason this plan does not contain a 4th column-mapping fix.

The exception is:

```
System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type
'timestamp with time zone', only UTC is supported.
```

Three fixes have targeted entity column mappings (#3330, #3444/PR #3743). PR #3743's entire
backend change was one migration altering `PhotobankIndexRoots."LastIndexedAt"` from
`timestamptz` to `timestamp`. The daily rate did not move: 3/day before, 3/day after.

Verified against the current `main`:

1. **No `DateTime` property anywhere in the EF model maps to `timestamp with time zone`.**
   Counting every `b.Property<DateTime>` / `b.Property<DateTime?>` in
   `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`:
   66 map to `timestamp without time zone`, 54 to `timestamp`, 1 to `date`, **0 to
   `timestamp with time zone`**. All 15 `timestamptz` columns in the snapshot belong to
   `DateTimeOffset` properties, which cannot raise this exception.
2. **A global convention already forces `Kind=Unspecified` on every mapped `DateTime`.**
   `ApplicationDbContext.OnModelCreating` (`backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:186-210`)
   loops every entity property and installs a `ValueConverter` writing
   `DateTime.SpecifyKind(v, DateTimeKind.Unspecified)`.
3. Therefore the value in the exception **cannot be an entity column write**. A parameter whose
   store type EF declares as `timestamp` never resolves to `timestamptz` inside Npgsql's
   `DateTimeConverterResolver`.

The remaining explanation is a parameter Npgsql typed **by its own default** because EF supplied
no store type — Npgsql 6+ maps a bare CLR `DateTime` to `timestamptz`, and rejects any
`Kind != Utc`. Task 1 exists to identify which parameter, in which command, without guessing.

Two further facts the implementer needs:

- **The telemetry fingerprint is stale.** `PhotobankRepository` no longer exists — commit
  `17a275f7` split it into six repositories under
  `backend/src/Anela.Heblo.Persistence/Photobank/`. The issue's `problemId`
  (`PhotobankRepository.SaveChangesAsync`) will not match new occurrences; the same signal now
  surfaces under `PhotobankPhotoRepository.SaveChangesAsync` /
  `PhotobankRootRepository.SaveChangesAsync`. Any App Insights query must account for both.
- **Related open signal:** #3592 is the same `DateTimeConverterResolver` exception family at
  `SmartsuppRepository.UpsertContactAsync`. If Task 1's diagnostic identifies a shared,
  non-entity-column mechanism, it likely explains #3592 too. Do not attempt to fix #3592 here.

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Persistence/Infrastructure/DateTimeParameterDiagnosticsInterceptor.cs` (create) | On command failure, log every parameter's name, DB type, CLR type, `DateTime.Kind` and value. Sibling of the existing `PostgresExceptionLoggingInterceptor`. |
| `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` (modify, ~85-120) | Register and attach the new interceptor alongside the two existing ones. |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs` (modify) | Add the model-wide invariant assertion next to the existing per-property theories. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs` (modify, ~231-243) | Normalise the Graph-sourced `lastModifiedDateTime` to `Kind=Utc` when mapping to `GraphPhotoItem`. |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceDeltaDateTimeTests.cs` (create) | Cover the normalisation for `Z`, offset, naive and null inputs. |

Tasks 1+2 (diagnostic) and Task 3 (invariant) and Task 4 (boundary) are independent — they may be
implemented in any order, but commit each separately.

---

### Task 1: Failing-command parameter diagnostics interceptor

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/DateTimeParameterDiagnosticsInterceptor.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/DateTimeParameterDiagnosticsInterceptorTests.cs`

`ArgumentException` from `DateTimeConverterResolver` is raised while Npgsql writes parameters, so
`DbCommandInterceptor.CommandFailed`/`CommandFailedAsync` fires with the live `DbCommand` still
carrying its parameter collection. That is the only place the offending parameter is observable.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/DateTimeParameterDiagnosticsInterceptorTests.cs`:

```csharp
using System.Data;
using System.Data.Common;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class DateTimeParameterDiagnosticsInterceptorTests
{
    private static NpgsqlCommand CommandWith(params (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand("UPDATE \"Photos\" SET \"ModifiedAt\" = $1 WHERE \"Id\" = $2");
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
        }
        return command;
    }

    [Fact]
    public void Describe_ReportsKindForEveryDateTimeParameter()
    {
        using var command = CommandWith(
            ("p0", new DateTime(2026, 7, 27, 1, 40, 13, DateTimeKind.Unspecified)),
            ("p1", 42));

        var description = DateTimeParameterDiagnosticsInterceptor.Describe(command);

        description.Should().Contain("p0");
        description.Should().Contain("Kind=Unspecified");
        description.Should().Contain("2026-07-27T01:40:13");
    }

    [Fact]
    public void Describe_MarksNonDateTimeParametersWithoutKind()
    {
        using var command = CommandWith(("p1", 42));

        var description = DateTimeParameterDiagnosticsInterceptor.Describe(command);

        description.Should().Contain("p1");
        description.Should().NotContain("Kind=");
    }

    [Fact]
    public void Describe_HandlesNullAndDbNullValues()
    {
        using var command = CommandWith(("p0", null));

        var description = DateTimeParameterDiagnosticsInterceptor.Describe(command);

        description.Should().Contain("p0");
        description.Should().Contain("null");
    }

    [Fact]
    public void CommandFailed_LogsOnlyForDateTimeKindFailures()
    {
        var logger = new Mock<ILogger<DateTimeParameterDiagnosticsInterceptor>>();
        var interceptor = new DateTimeParameterDiagnosticsInterceptor(logger.Object);

        interceptor.ShouldLog(new ArgumentException(
            "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported"))
            .Should().BeTrue();

        interceptor.ShouldLog(new InvalidOperationException("connection closed"))
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DateTimeParameterDiagnosticsInterceptorTests`
Expected: FAIL — `DateTimeParameterDiagnosticsInterceptor` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/DateTimeParameterDiagnosticsInterceptor.cs`:

```csharp
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Infrastructure;

/// <summary>
/// Logs the full parameter set of a failing DbCommand when Npgsql rejects a DateTime because of its
/// Kind. Three successive fixes for this signal (#3330, #3743) targeted entity column mappings and
/// moved the daily rate by zero, because no DateTime property in the model maps to
/// 'timestamp with time zone' — the offending parameter is one Npgsql typed by its own default.
/// This interceptor names that parameter instead of requiring another hypothesis.
///
/// Distinct from <see cref="PostgresExceptionLoggingInterceptor"/>: that one enriches
/// SaveChanges failures with the Postgres SqlState; this one runs at the command layer, which is
/// where parameter-writing exceptions are raised before any SQL reaches the server.
/// </summary>
public class DateTimeParameterDiagnosticsInterceptor : DbCommandInterceptor
{
    private readonly ILogger<DateTimeParameterDiagnosticsInterceptor> _logger;

    public DateTimeParameterDiagnosticsInterceptor(ILogger<DateTimeParameterDiagnosticsInterceptor> logger)
    {
        _logger = logger;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        LogIfDateTimeKindFailure(command, eventData.Exception);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogIfDateTimeKindFailure(command, eventData.Exception);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    internal bool ShouldLog(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Cannot write DateTime with Kind", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void LogIfDateTimeKindFailure(DbCommand command, Exception? exception)
    {
        if (!ShouldLog(exception))
            return;

        _logger.LogError(
            exception,
            "DateTime Kind rejected by Npgsql. CommandText={CommandText} Parameters={Parameters}",
            command.CommandText,
            Describe(command));
    }

    /// <summary>
    /// Renders every parameter as name/DbType/CLR type/value, annotating DateTime values with their
    /// Kind. Values are timestamps and identifiers only — no free-text business data is emitted.
    /// </summary>
    internal static string Describe(DbCommand command)
    {
        var builder = new StringBuilder();

        foreach (DbParameter parameter in command.Parameters)
        {
            if (builder.Length > 0)
                builder.Append("; ");

            builder.Append(parameter.ParameterName)
                .Append(" DbType=").Append(parameter.DbType);

            var value = parameter.Value;
            if (value is null || value == DBNull.Value)
            {
                builder.Append(" Value=null");
                continue;
            }

            builder.Append(" ClrType=").Append(value.GetType().Name);

            if (value is DateTime dateTime)
            {
                builder.Append(" Kind=").Append(dateTime.Kind)
                    .Append(" Value=").Append(dateTime.ToString("O", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(" Value=").Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        return builder.Length == 0 ? "(none)" : builder.ToString();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DateTimeParameterDiagnosticsInterceptorTests`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/DateTimeParameterDiagnosticsInterceptor.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/DateTimeParameterDiagnosticsInterceptorTests.cs
git commit -m "feat(persistence): log failing command parameters on DateTime Kind rejection"
```

---

### Task 2: Register the interceptor

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:85-120`

The two existing interceptors are registered as scoped services and attached in the
`AddDbContext<ApplicationDbContext>` callback. Follow that exact shape — do not restructure it.

- [ ] **Step 1: Read the current registration**

Run: `sed -n '80,125p' backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`
Expected: `services.AddScoped<PostgresExceptionLoggingInterceptor>();`,
`services.AddScoped<NpgsqlConnectionInterceptor>();` and an `options.AddInterceptors(...)` call
resolving both from `sp`.

- [ ] **Step 2: Add the service registration**

Immediately after the `NpgsqlConnectionInterceptor` registration line, add:

```csharp
        services.AddScoped<DateTimeParameterDiagnosticsInterceptor>();
```

- [ ] **Step 3: Attach it to the DbContext**

Extend the existing `options.AddInterceptors(...)` call with a third argument:

```csharp
                options.AddInterceptors(
                    sp.GetRequiredService<PostgresExceptionLoggingInterceptor>(),
                    sp.GetRequiredService<NpgsqlConnectionInterceptor>(),
                    sp.GetRequiredService<DateTimeParameterDiagnosticsInterceptor>());
```

- [ ] **Step 4: Build and run the full backend suite**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: Build succeeded, 0 errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — no regressions. Adding a `DbCommandInterceptor` changes no query behaviour;
any failure here means the registration was placed in the wrong lambda.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat(persistence): register DateTimeParameterDiagnosticsInterceptor"
```

---

### Task 3: Model-wide `timestamptz` invariant test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs`

`PhotoSchemaTests` currently pins 6 hand-listed properties. The invariant that actually matters —
and that this investigation established — is model-wide: because the global converter in
`ApplicationDbContext.OnModelCreating` writes every `DateTime` as `Kind=Unspecified`, *any*
`DateTime` property mapped to `timestamp with time zone` is an unconditional runtime failure.
Assert it once for the whole model so no future entity can reintroduce the shape the last three
fixes were chasing.

- [ ] **Step 1: Write the failing test**

Append this method inside the existing `PhotoSchemaTests` class (keep the existing theories as-is;
`NewNpgsqlContext()` is already defined in the file):

```csharp
    // Model-wide invariant. The global converter in ApplicationDbContext.OnModelCreating writes
    // every DateTime as Kind=Unspecified, which Npgsql rejects for 'timestamp with time zone'.
    // A single offending property is therefore a guaranteed runtime failure for that entity —
    // catch it at build time instead of in a nightly Hangfire job.
    [Fact]
    public void NoDateTimeProperty_IsMappedToTimestampWithTimeZone()
    {
        using var db = NewNpgsqlContext();

        var offenders = db.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
                .Where(p => (p.GetColumnType() ?? string.Empty).Contains("with time zone"))
                .Select(p => $"{entityType.ClrType.Name}.{p.Name} -> {p.GetColumnType()}"))
            .OrderBy(x => x)
            .ToList();

        offenders.Should().BeEmpty(
            "DateTime values are written with Kind=Unspecified by the global converter, which " +
            "'timestamp with time zone' rejects; use AsUtcTimestamp() on these properties");
    }
```

Add `using System;`, `using System.Linq;` to the file's using block if the project does not have
implicit usings enabled for the test assembly.

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~PhotoSchemaTests`
Expected: **PASS immediately** — the model currently has zero offenders. This test is a guard, not
a bug reproduction, so a green first run is the correct outcome here.

- [ ] **Step 3: Verify the guard actually bites**

Temporarily change `builder.Property(x => x.IndexedAt).IsRequired().AsUtcTimestamp();` to
`builder.Property(x => x.IndexedAt).IsRequired().HasColumnType("timestamp with time zone");`
in `backend/src/Anela.Heblo.Persistence/Photobank/PhotoConfiguration.cs:21`.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~PhotoSchemaTests`
Expected: FAIL listing `Photo.IndexedAt -> timestamp with time zone`.

Then revert the edit:

```bash
git checkout -- backend/src/Anela.Heblo.Persistence/Photobank/PhotoConfiguration.cs
```

Re-run the filter. Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs
git commit -m "test(persistence): assert no DateTime column maps to timestamptz model-wide"
```

---

### Task 4: Normalise the Microsoft Graph `lastModifiedDateTime` boundary

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs:231-243`
- Create: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceDeltaDateTimeTests.cs`

`photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;`
(`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:181`)
is the only value in the failing batch not sourced from `DateTime.UtcNow` or a DB round-trip. It
originates at `PhotobankGraphService.cs:239`, deserialised from the raw Graph response by
`System.Text.Json` with no `DateTime` converter (`JsonOptions`, line 29-32). `System.Text.Json`
yields `Kind=Utc` only for a trailing `Z`; an offset yields `Kind=Local` and a naive string yields
`Kind=Unspecified`.

This does **not** on its own explain the exception (the `ModifiedAt` column is `timestamp`, and the
global converter overwrites the Kind anyway) — so do not describe it as the fix in the PR. It is
worth doing because it makes the adapter's contract honest: `GraphPhotoItem.LastModifiedAt` is
documented as an instant, and today it can carry a local or naive value into domain code.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceDeltaDateTimeTests.cs`:

```csharp
using Anela.Heblo.Adapters.Microsoft365.Photobank;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankGraphServiceDeltaDateTimeTests
{
    [Fact]
    public void ToUtc_KeepsUtcInstantsUnchanged()
    {
        var utc = new DateTime(2026, 7, 27, 1, 40, 13, DateTimeKind.Utc);

        var result = PhotobankGraphService.ToUtc(utc);

        result.Should().Be(utc);
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_ConvertsLocalInstantsToUtc()
    {
        var local = new DateTime(2026, 7, 27, 3, 40, 13, DateTimeKind.Local);

        var result = PhotobankGraphService.ToUtc(local);

        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void ToUtc_TreatsUnspecifiedAsAlreadyUtc()
    {
        var naive = new DateTime(2026, 7, 27, 1, 40, 13, DateTimeKind.Unspecified);

        var result = PhotobankGraphService.ToUtc(naive);

        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(DateTime.SpecifyKind(naive, DateTimeKind.Utc));
    }

    [Fact]
    public void ToUtc_PassesNullThrough()
    {
        PhotobankGraphService.ToUtc(null).Should().BeNull();
    }
}
```

The `Unspecified` case asserts "already UTC", not a local-timezone conversion: Graph documents
`lastModifiedDateTime` as UTC, so a missing designator means a missing suffix, not local time.
This deliberately differs from `Anela.Heblo.Adapters.Flexi.Common.UnspecifiedDateTimeConverter`,
which converts from local because FlexiBee genuinely returns local time.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~PhotobankGraphServiceDeltaDateTimeTests`
Expected: FAIL — `PhotobankGraphService` does not contain a definition for `ToUtc` (CS0117).

- [ ] **Step 3: Write the implementation**

In `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs`,
add this method next to the other private static helpers (immediately above `CreateRequest`,
around line 245):

```csharp
    /// <summary>
    /// Normalises a Graph-sourced timestamp to Kind=Utc. System.Text.Json yields Kind=Utc only for
    /// a trailing 'Z'; an explicit offset yields Local and a naive string yields Unspecified.
    /// Graph documents lastModifiedDateTime as UTC, so an absent designator means an absent suffix
    /// — stamp it rather than shifting it by the machine's timezone.
    /// </summary>
    internal static DateTime? ToUtc(DateTime? value) => value?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };
```

Then change the mapping at line 239 from:

```csharp
            LastModifiedAt = item.LastModifiedDateTime,
```

to:

```csharp
            LastModifiedAt = ToUtc(item.LastModifiedDateTime),
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~PhotobankGraphServiceDeltaDateTimeTests`
Expected: PASS — 4 tests.

No `InternalsVisibleTo` plumbing is needed: both
`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj:20`
and `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj:33` already declare
`<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, which is what makes the `internal ToUtc`
(Task 4) and `internal Describe`/`ShouldLog` (Task 1) members reachable from the tests.

- [ ] **Step 5: Run the full backend suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS. `PhotobankIndexJobTests` exercises the mapping path — if it asserts a specific
`LastModifiedAt` Kind, update the expectation to `DateTimeKind.Utc`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceDeltaDateTimeTests.cs
git commit -m "fix(photobank): normalise Graph lastModifiedDateTime to UTC at the adapter boundary"
```

---

### Task 5: Record the findings on the issue

**Files:** none — this is a GitHub comment, not a code change.

- [ ] **Step 1: Post a comment on issue #3757**

State, with the file/line evidence from the Background section above:
- No `DateTime` property in the model maps to `timestamp with time zone` (0 of 121), so the
  exception is not an entity column write and the three prior fixes could not have moved it.
- The global `Kind=Unspecified` converter at `ApplicationDbContext.cs:186-210` and the
  `timestamp` column mappings are mutually consistent and correct.
- The telemetry `problemId` is stale: `PhotobankRepository` was split by commit `17a275f7`;
  new occurrences fingerprint under `PhotobankPhotoRepository` / `PhotobankRootRepository`.
- The new interceptor will name the exact parameter on the next nightly run — hold the 4th fix
  until that log lands.

Do **not** close the issue. It stays open until a diagnostic log identifies the parameter.

- [ ] **Step 2: Query App Insights after the next nightly run**

Run, once the deploy is live and one `PhotobankIndexJob` cycle has passed:

```bash
./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P2D \
  'traces | where message has "DateTime Kind rejected by Npgsql" | project timestamp, message | order by timestamp desc'
```

Expected: one row per occurrence, containing `CommandText` and the `Parameters` string with the
offending `Kind=Unspecified` parameter. That row determines the actual fix — which is a **separate
follow-up issue**, not part of this plan.

---

## Verification

- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — all green
- [ ] `dotnet build Anela.Heblo.sln` — 0 errors
- [ ] Diff touches only the 5 files in the File Structure table
- [ ] No migration added — this plan deliberately changes no column type

## Out of Scope

- A 4th mapping fix. The evidence rules out the mechanism all three prior attempts assumed;
  guessing again without the Task 1 log repeats the loop.
- #3592 (`SmartsuppRepository.UpsertContactAsync`). Same exception family; the interceptor from
  Task 1 is global and will diagnose it too, but do not change Smartsupp code here.
- Refreshing the telemetry routine's `problemId` fingerprints after the `17a275f7` repository
  split. Real, and worth its own issue.
