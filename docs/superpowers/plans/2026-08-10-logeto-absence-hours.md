# Logeto Absence Hours Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill each opted-in worker's net daily contracted hours into Logeto absence records (Dovolená, Nemoc, …) that were entered with no time at all, for past days only.

**Architecture:** A new Hangfire recurring job (`AbsenceHoursJob`) drives a new `AbsenceHoursService`, which walks a rolling window of past days per opted-in person and `PUT`s `Hours` onto absence records that have neither a `From`/`To` window nor an `Hours` duration. The hours come from the person's Logeto `Note` field, which is extended from `integration` to `integration 6,4` — Logeto's public API does not expose úvazek anywhere (proven three ways in the spec). Parsing that note moves into a shared `IntegrationNote` domain type that `BreakInsertionService` must also adopt, or its exact-match person filter silently stops matching.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq, Hangfire, `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md`

## Global Constraints

- **DTOs are classes, never C# records** — OpenAPI client generators mishandle record parameter order. (`docs/architecture/development_guidelines.md`)
- **Times are Prague wall-clock, never UTC** — the Logeto API is a pure pass-through of local time. Use `LogetoTimeConverter.PragueTimeZone` for "today". (`docs/superpowers/specs/2026-08-05-logeto-spike-results.md`, Finding 3)
- **Never stamp an `ExternalKey` on a Logeto record we write.** A record carrying one throws `ExternalKeyUniqueViolation` when later split by a `merge=true` insert. (Same spike doc, Finding 2)
- **Hours format is `HH:mm:00`** — the API requires seconds present and zero.
- **Net hours, not gross.** `80% úvazek bez pauz (6,4hod/den)` → `6,4`. A vacation day carries no unpaid break.
- **Decimal parsing must be culture-invariant** and accept both `,` and `.`, so behaviour does not depend on server locale.
- **New recurring jobs ship disabled:** `DefaultIsEnabled = false`.
- Validation before completion: `dotnet build` + `dotnet format`, and all touched tests pass.

## Build & Test Commands

Run the build once before testing, then test with `--no-build`. Concurrent `dotnet test` across worktrees hangs at 0% CPU, hence `-p:UseSharedCompilation=false`:

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~<TestClass>"
dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests/Anela.Heblo.Adapters.Logeto.Tests.csproj --no-build
```

An `AccessMatrixGen` crash during the build is pre-existing noise, not a failure.

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs` | **new** — parse a person's `Note` into enrollment + net daily hours |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs` | add the `Absence` constant |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntry.cs` | add `Billable`, `Contract`, `Subcontract` so a PUT can round-trip them |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntryRequest.cs` | **renamed** from `LogetoCreateTimeEntryRequest.cs`; add `Hours`, `Contract`, `Subcontract`. Logeto uses one `TimeTrackingRequest` body for POST and PUT |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/ILogetoClient.cs` | add `UpdateTimeEntryAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs` | implement `PUT /api/v2/TimeTracking/{guid}?merge=false` |
| `backend/src/Anela.Heblo.Application/Features/Attendance/AbsenceHoursOptions.cs` | **new** — window and marker config |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs` | **new** — the walk, the guards, the fill |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs` | **new** — recurring job wrapper |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs` | person filter switches to `IntegrationNote` |
| `backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs` | register options + service |
| `backend/src/Anela.Heblo.API/appsettings.json` | `Logeto:AbsenceHours` section |
| `backend/test/Anela.Heblo.Tests/Features/Attendance/IntegrationNoteTests.cs` | **new** |
| `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursServiceTests.cs` | **new** |
| `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursJobTests.cs` | **new** |
| `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs` | regression test for the note change |
| `backend/test/Anela.Heblo.Adapters.Logeto.Tests/LogetoClientTests.cs` | PUT URL + body test |

---

### Task 1: `IntegrationNote` — parse enrollment and hours from the Note

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/IntegrationNoteTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IntegrationNote.Parse(string? note, string marker) → IntegrationNote` with `bool IsEnrolled` and `TimeSpan? DailyHours`. Also `LogetoActivityTypes.Absence` (value `"Absence"`). Tasks 2, 5 and 6 depend on both.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Attendance/IntegrationNoteTests.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Attendance;

public class IntegrationNoteTests
{
    private const string Marker = "integration";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("somebody else")]
    [InlineData("integrationX")]      // marker must be followed by whitespace or nothing
    [InlineData("not integration")]   // marker must start the note
    public void Parse_NotEnrolled_WhenMarkerIsAbsent(string? note)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeFalse();
        result.DailyHours.Should().BeNull();
    }

    [Theory]
    [InlineData("integration")]
    [InlineData("  integration  ")]
    [InlineData("INTEGRATION")]
    public void Parse_EnrolledWithoutHours_WhenNoteIsMarkerOnly(string note)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().BeNull();
    }

    [Theory]
    [InlineData("integration 6,4", 6, 24)]
    [InlineData("integration 6.4", 6, 24)]
    [InlineData("integration 8", 8, 0)]
    [InlineData("integration   7,5", 7, 30)]
    [InlineData("Integration 6,4", 6, 24)]
    public void Parse_ReadsDailyHours(string note, int expectedHours, int expectedMinutes)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().Be(new TimeSpan(expectedHours, expectedMinutes, 0));
    }

    [Theory]
    [InlineData("integration abc")]
    [InlineData("integration 0")]
    [InlineData("integration -3")]
    [InlineData("integration 25")]
    [InlineData("integration 6,4 extra")]
    public void Parse_EnrolledWithoutHours_WhenHoursAreUnusable(string note)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().BeNull();
    }

    [Fact]
    public void Parse_IsCultureInvariant()
    {
        // A culture whose decimal separator is "," must not change how "6.4" parses,
        // and vice versa. Without invariant parsing this test flips on a Czech server.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            IntegrationNote.Parse("integration 6.4", Marker).DailyHours
                .Should().Be(new TimeSpan(6, 24, 0));
            IntegrationNote.Parse("integration 6,4", Marker).DailyHours
                .Should().Be(new TimeSpan(6, 24, 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
```

Expected: FAIL to compile — `The name 'IntegrationNote' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs`:

```csharp
using System.Globalization;

namespace Anela.Heblo.Domain.Features.Attendance;

/// <summary>
/// A Logeto person's Note field carries both the integration opt-in marker and that
/// person's net daily contracted hours (úvazek): "integration 6,4". Logeto's public API
/// exposes úvazek nowhere — the Úvazky pracovníků screen is web-app-only — so the Note is
/// the single place this lives. See
/// docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md.
/// </summary>
public class IntegrationNote
{
    private const double MinDailyHours = 0;
    private const double MaxDailyHours = 24;

    private static readonly IntegrationNote NotEnrolledNote = new() { IsEnrolled = false };

    public bool IsEnrolled { get; private init; }

    /// <summary>Net daily hours, or null when the note carries no usable number.</summary>
    public TimeSpan? DailyHours { get; private init; }

    public static IntegrationNote Parse(string? note, string marker)
    {
        var trimmed = note?.Trim();

        if (string.IsNullOrEmpty(trimmed)
            || !trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return NotEnrolledNote;
        }

        var remainder = trimmed[marker.Length..];

        // "integrationX" is a different note, not an enrolled person with a typo.
        if (remainder.Length > 0 && !char.IsWhiteSpace(remainder[0]))
        {
            return NotEnrolledNote;
        }

        return new IntegrationNote
        {
            IsEnrolled = true,
            DailyHours = ParseDailyHours(remainder.Trim())
        };
    }

    private static TimeSpan? ParseDailyHours(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        // Czech notes use a decimal comma; accept both separators regardless of server locale.
        var normalized = text.Replace(',', '.');

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            || hours <= MinDailyHours
            || hours > MaxDailyHours)
        {
            return null;
        }

        // Round to whole minutes: the API rejects non-zero seconds, and 6.4 is not exactly
        // representable in binary floating point.
        return TimeSpan.FromMinutes(Math.Round(hours * 60));
    }
}
```

Modify `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs` to add the third type:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public static class LogetoActivityTypes
{
    public const string Work = "Work";
    public const string Break = "Break";
    public const string Absence = "Absence";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~IntegrationNoteTests"
```

Expected: PASS, 20 tests.

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Domain backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs \
        backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/IntegrationNoteTests.cs
git commit -m "feat: parse integration opt-in and daily hours from Logeto person note"
```

---

### Task 2: Break insertion adopts the shared note parser

Without this, appending hours to a Note silently drops that person out of break insertion.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs:54-57`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs`

**Interfaces:**
- Consumes: `IntegrationNote.Parse` from Task 1.
- Produces: nothing new.

- [ ] **Step 1: Write the failing regression test**

Append to `BreakInsertionServiceTests`, after the existing `InsertsBreak_ForEightHourDayWithoutBreak` test:

```csharp
    [Fact]
    public async Task SelectsPerson_WhenNoteCarriesDailyHours()
    {
        // The absence-hours feature extends the note to "integration 6,4". Break insertion
        // used to match the note with exact equality, which would silently drop this person.
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = BreakActivity, Name = "Oběd", Type = LogetoActivityTypes.Break }
            });

        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "integration 6,4", Inactive = false }
            });

        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoTimeEntry> { WorkEntry(8, 0, 16, 30) });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~BreakInsertionServiceTests.SelectsPerson_WhenNoteCarriesDailyHours"
```

Expected: FAIL — `summary.BreaksInserted` is `0`, because the note is not exactly `"integration"`.

- [ ] **Step 3: Switch the person filter**

In `BreakInsertionService.RunAsync`, replace:

```csharp
        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Where(p => !p.Inactive
                && string.Equals(p.Note?.Trim(), options.NoteMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();
```

with:

```csharp
        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Where(p => !p.Inactive
                && IntegrationNote.Parse(p.Note, options.NoteMarker).IsEnrolled)
            .ToList();
```

`IntegrationNote` is in `Anela.Heblo.Domain.Features.Attendance`, already imported at the top of the file.

- [ ] **Step 4: Run the full break-insertion suite**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~BreakInsertion"
```

Expected: PASS, including every pre-existing test — plain `"integration"` still enrolls.

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs
git commit -m "refactor: select break-insertion people via shared IntegrationNote parser"
```

---

### Task 3: Domain types gain the fields a PUT must round-trip

`PUT /api/v2/TimeTracking/{guid}` replaces the whole record, so every field we do not resend is lost. This task widens the read model and the request model; no behaviour changes yet.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntry.cs`
- Rename: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoCreateTimeEntryRequest.cs` → `LogetoTimeEntryRequest.cs`
- Modify (rename callers): `ILogetoClient.cs`, `LogetoClient.cs`, `BreakInsertionService.cs`, `LogetoClientTests.cs`, `BreakInsertionServiceTests.cs`, `BreakInsertionJobTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LogetoTimeEntryRequest` with `Person`, `Activity`, `Date`, `From`, `To`, `Hours`, `Billable`, `Description`, `ExternalKey`, `Contract`, `Subcontract`. `LogetoTimeEntry` additionally exposes `Billable`, `Contract`, `Subcontract`. Tasks 4, 5 and 6 depend on both.

- [ ] **Step 1: Rename the request type**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoCreateTimeEntryRequest.cs \
       backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntryRequest.cs
```

Replace its contents entirely — one type now serves both POST and PUT, because Logeto uses a single `TimeTrackingRequest` body for each:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

/// <summary>
/// Body for both POST (create) and PUT (update) on /api/v2/TimeTracking — Logeto uses the
/// same TimeTrackingRequest schema for each. A PUT is a full replacement, so callers must
/// resend every field they want preserved.
/// </summary>
public class LogetoTimeEntryRequest
{
    public required Guid Person { get; init; }
    public required Guid Activity { get; init; }
    public required DateOnly Date { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }

    /// <summary>Duration for records with no clock window, e.g. "06:24:00". Seconds must be zero.</summary>
    public string? Hours { get; init; }

    public required bool Billable { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }
    public Guid? Contract { get; init; }
    public Guid? Subcontract { get; init; }
}
```

- [ ] **Step 2: Update every reference to the old name**

Six files reference `LogetoCreateTimeEntryRequest`. Rename the type in each — the member names are unchanged, so this is a pure identifier swap:

```bash
grep -rl --include='*.cs' LogetoCreateTimeEntryRequest backend | grep -v '/obj/'
```

Expected list: `ILogetoClient.cs`, `LogetoClient.cs`, `BreakInsertionService.cs`, `LogetoClientTests.cs`, `BreakInsertionServiceTests.cs`, `BreakInsertionJobTests.cs`.

```bash
grep -rl --include='*.cs' LogetoCreateTimeEntryRequest backend | grep -v '/obj/' | \
  xargs sed -i 's/LogetoCreateTimeEntryRequest/LogetoTimeEntryRequest/g'
```

- [ ] **Step 3: Widen the read model**

Replace `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntry.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoTimeEntry
{
    public Guid Guid { get; init; }
    public Guid Person { get; init; }
    public DateOnly Date { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    /// <summary>Duration for records entered without a clock window, e.g. "08:04:00".</summary>
    public string? Hours { get; init; }

    public Guid Activity { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }

    /// <summary>Resent verbatim when updating a record — a PUT replaces the whole entry.</summary>
    public bool Billable { get; init; }

    public Guid? Contract { get; init; }
    public Guid? Subcontract { get; init; }
}
```

- [ ] **Step 4: Build and run the whole suite to prove nothing broke**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests/Anela.Heblo.Adapters.Logeto.Tests.csproj --no-build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Attendance"
```

Expected: PASS. A rename plus three additive properties changes no behaviour.

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln
git add -A backend/src backend/test
git commit -m "refactor: reuse one Logeto time-entry request type for create and update"
```

---

### Task 4: `UpdateTimeEntryAsync` on the Logeto client

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Attendance/ILogetoClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Logeto.Tests/LogetoClientTests.cs`

**Interfaces:**
- Consumes: `LogetoTimeEntryRequest` from Task 3.
- Produces: `Task UpdateTimeEntryAsync(Guid guid, LogetoTimeEntryRequest request, CancellationToken cancellationToken)` on `ILogetoClient`. Tasks 5 and 6 call it.

- [ ] **Step 1: Write the failing tests**

Append to `LogetoClientTests`:

```csharp
    [Fact]
    public async Task UpdateTimeEntryAsync_PutsToRecordUrlWithMergeDisabled()
    {
        var handler = new StubHandler(Json("""{"Guid":"11111111-1111-1111-1111-111111111111"}"""));
        var client = CreateClient(handler);
        var guid = Guid.Parse("18af1a88-d9aa-4de7-b6a4-129898c32012");

        await client.UpdateTimeEntryAsync(guid, new LogetoTimeEntryRequest
        {
            Person = Guid.Parse("92d12de7-ca9d-4211-9eb9-9f4f294cb205"),
            Activity = Guid.Parse("b569483e-f36b-1410-80ad-00e813da89b0"),
            Date = new DateOnly(2026, 7, 20),
            Hours = "06:24:00",
            Billable = false,
            // Deliberately ASCII: this test pins the wire format, and System.Text.Json's
            // default encoder escapes non-ASCII, which would make the assertion about the
            // encoder rather than about the round-trip. Diacritics are covered by
            // AbsenceHoursServiceTests.PreservesFieldsThatAPutWouldOtherwiseDrop.
            Description = "puvodni popis",
            ExternalKey = null
        }, CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].RequestUri!.PathAndQuery
            .Should().Be($"/api/v2/TimeTracking/{guid}?merge=false");

        var body = handler.RequestBodies[0];
        body.Should().Contain("\"Hours\":\"06:24:00\"");
        body.Should().Contain("\"Date\":\"2026-07-20\"");
        body.Should().Contain("\"Description\":\"puvodni popis\"");
        body.Should().Contain("\"Billable\":false");
        // DefaultIgnoreCondition.WhenWritingNull keeps null fields out of the payload.
        body.Should().NotContain("ExternalKey");
    }

    [Fact]
    public async Task UpdateTimeEntryAsync_ThrowsLogetoApiException_OnErrorResponse()
    {
        var handler = new StubHandler(Json(
            """{"Error":{"Code":"ExternalKeyUniqueViolation","Message":"ExternalKey must be unique."}}""",
            HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var act = async () => await client.UpdateTimeEntryAsync(
            Guid.NewGuid(),
            new LogetoTimeEntryRequest
            {
                Person = Guid.NewGuid(),
                Activity = Guid.NewGuid(),
                Date = new DateOnly(2026, 7, 20),
                Billable = false
            },
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<LogetoApiException>();
        exception.Which.Code.Should().Be("ExternalKeyUniqueViolation");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
```

Expected: FAIL to compile — `'ILogetoClient' does not contain a definition for 'UpdateTimeEntryAsync'`.

- [ ] **Step 3: Write the implementation**

Add to `ILogetoClient`, below `CreateTimeEntryAsync`:

```csharp
    /// <summary>
    /// Replaces a time entry. Logeto's PUT is a full replacement, so the request must carry
    /// every field that should survive. merge is disabled — updating an entry that has no
    /// clock window has nothing to merge against.
    /// </summary>
    Task UpdateTimeEntryAsync(Guid guid, LogetoTimeEntryRequest request, CancellationToken cancellationToken);
```

Add to `LogetoClient`, below `CreateTimeEntryAsync`:

```csharp
    public async Task UpdateTimeEntryAsync(
        Guid guid, LogetoTimeEntryRequest request, CancellationToken cancellationToken)
    {
        var url = $"/api/v2/TimeTracking/{guid}?merge=false";
        var response = await _httpClient.PutAsJsonAsync(url, request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }
```

`PutAsJsonAsync` comes from `System.Net.Http.Json`, already imported at the top of the file.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests/Anela.Heblo.Adapters.Logeto.Tests.csproj --no-build
```

Expected: PASS.

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln
git add backend/src/Anela.Heblo.Domain/Features/Attendance/ILogetoClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs \
        backend/test/Anela.Heblo.Adapters.Logeto.Tests/LogetoClientTests.cs
git commit -m "feat: add Logeto time-entry update via PUT"
```

---

### Task 5: `AbsenceHoursService` — window, selection, and the fill

Guards come in Task 6. This task delivers the happy path and the window boundaries.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/AbsenceHoursOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursServiceTests.cs`

**Interfaces:**
- Consumes: `IntegrationNote.Parse`, `LogetoActivityTypes.Absence` (Task 1); `LogetoTimeEntry.Billable/Contract/Subcontract`, `LogetoTimeEntryRequest` (Task 3); `ILogetoClient.UpdateTimeEntryAsync` (Task 4); `LogetoTimeConverter.PragueTimeZone` (existing).
- Produces: `AbsenceHoursService.RunAsync(CancellationToken) → Task<AbsenceHoursSummary>`, where `AbsenceHoursSummary` has `RecordsScanned`, `HoursFilled`, `SkippedNoHours`, `SkippedAmbiguous`, `SkippedMixedDay`, `Failed`. `AbsenceHoursOptions` has `StartDate`, `LookbackDays`, `NoteMarker` and `ConfigKey`. Tasks 6 and 7 depend on all of this.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class AbsenceHoursServiceTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VacationActivity = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid SickActivity = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid Worker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // Fixed "now": 2026-08-04 08:00 Prague. today = 2026-08-04, so the window is
    // 2026-08-01 (StartDate) through 2026-08-03.
    private static readonly DateOnly Today = new(2026, 8, 4);
    private static readonly DateOnly PastDay = new(2026, 8, 3);

    private readonly Mock<ILogetoClient> _client = new();

    private AbsenceHoursService CreateService(string note = "integration 6,4")
    {
        var options = new AbsenceHoursOptions { StartDate = new DateOnly(2026, 8, 1) };

        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero));

        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = VacationActivity, Name = "Dovolená", Type = LogetoActivityTypes.Absence },
                new() { Guid = SickActivity, Name = "Nemoc", Type = LogetoActivityTypes.Absence }
            });

        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = note, Inactive = false },
                new() { Guid = Guid.NewGuid(), Note = "somebody else", Inactive = false }
            });

        return new AbsenceHoursService(
            _client.Object,
            Options.Create(options),
            timeProvider.Object,
            NullLogger<AbsenceHoursService>.Instance);
    }

    private void SetupEntries(params LogetoTimeEntry[] entries) =>
        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());

    private static LogetoTimeEntry EmptyAbsence(DateOnly date, Guid? activity = null) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = date,
        Activity = activity ?? VacationActivity,
        From = null,
        To = null,
        Hours = null
    };

    [Fact]
    public async Task FillsHours_ForLoneEmptyAbsenceOnPastDay()
    {
        var record = EmptyAbsence(PastDay);
        SetupEntries(record);

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(1);
        summary.RecordsScanned.Should().Be(1);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            record.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == VacationActivity
                && r.Date == PastDay
                && r.Hours == "06:24:00"
                && r.From == null
                && r.To == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FillsHours_ForAnyAbsenceActivity()
    {
        SetupEntries(EmptyAbsence(PastDay, SickActivity));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(1);
    }

    [Fact]
    public async Task PreservesFieldsThatAPutWouldOtherwiseDrop()
    {
        var record = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = PastDay,
            Activity = VacationActivity,
            Description = "rodinná dovolená",
            ExternalKey = "legacy-key",
            Billable = true,
            Contract = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Subcontract = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        SetupEntries(record);

        await CreateService().RunAsync(CancellationToken.None);

        _client.Verify(c => c.UpdateTimeEntryAsync(
            record.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Description == "rodinná dovolená"
                && r.ExternalKey == "legacy-key"
                && r.Billable == true
                && r.Contract == record.Contract
                && r.Subcontract == record.Subcontract),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IgnoresToday()
    {
        SetupEntries(EmptyAbsence(Today));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(0);
        summary.RecordsScanned.Should().Be(0);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IgnoresRecordThatAlreadyHasHours()
    {
        SetupEntries(new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = PastDay,
            Activity = VacationActivity,
            Hours = "06:24:00"
        });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(0);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IgnoresRecordThatHasAClockWindow()
    {
        SetupEntries(new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = PastDay,
            Activity = VacationActivity,
            From = new DateTimeOffset(2026, 8, 3, 7, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 14, 0, 0, TimeSpan.Zero)
        });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(0);
    }

    [Fact]
    public async Task IgnoresNonAbsenceActivities()
    {
        SetupEntries(new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = PastDay,
            Activity = WorkActivity
        });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(0);
    }

    [Fact]
    public async Task IgnoresPeopleWhoAreNotEnrolled()
    {
        SetupEntries(new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Guid.NewGuid(), // nobody enrolled
            Date = PastDay,
            Activity = VacationActivity
        });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.HoursFilled.Should().Be(0);
    }

    [Fact]
    public async Task FormatsWholeHoursWithLeadingZero()
    {
        SetupEntries(EmptyAbsence(PastDay));

        await CreateService(note: "integration 8").RunAsync(CancellationToken.None);

        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(),
            It.Is<LogetoTimeEntryRequest>(r => r.Hours == "08:00:00"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
```

Expected: FAIL to compile — `AbsenceHoursService` and `AbsenceHoursOptions` do not exist.

- [ ] **Step 3: Write the options**

Create `backend/src/Anela.Heblo.Application/Features/Attendance/AbsenceHoursOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance;

public class AbsenceHoursOptions
{
    public const string ConfigKey = "Logeto:AbsenceHours";

    /// <summary>First day of the walk. Idempotent skipping keeps re-runs cheap.</summary>
    public DateOnly StartDate { get; set; } = new(2026, 8, 1);

    /// <summary>Days of history scanned before today. The walk covers
    /// [max(StartDate, today - LookbackDays), today - 1]; today is excluded because a
    /// same-day absence may still be edited by the worker.</summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>People whose Note starts with this marker are processed. The rest of the
    /// note carries their net daily hours, e.g. "integration 6,4".</summary>
    public string NoteMarker { get; set; } = "integration";
}
```

- [ ] **Step 4: Write the service**

Create `backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>
/// Fills each opted-in worker's net daily contracted hours into Logeto absence records that
/// were entered with no time at all. Past days only — a same-day absence may still be edited.
/// See docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md.
/// </summary>
public class AbsenceHoursService
{
    private readonly ILogetoClient _client;
    private readonly IOptions<AbsenceHoursOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AbsenceHoursService> _logger;

    public AbsenceHoursService(
        ILogetoClient client,
        IOptions<AbsenceHoursOptions> options,
        TimeProvider timeProvider,
        ILogger<AbsenceHoursService> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AbsenceHoursSummary> RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var summary = new AbsenceHoursSummary();

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var today = DateOnly.FromDateTime(pragueNow.Date);
        var lookbackDays = Math.Max(options.LookbackDays, 0);
        var windowStart = today.AddDays(-lookbackDays);
        var from = windowStart < options.StartDate ? options.StartDate : windowStart;
        var to = today.AddDays(-1);

        if (from > to)
        {
            _logger.LogWarning(
                "Absence hours window is empty: computed from {From} (StartDate {StartDate}) is after {To}. Nothing to do.",
                from, options.StartDate, to);
            return summary;
        }

        var activities = await _client.GetActivitiesAsync(cancellationToken);
        var absenceActivities = activities
            .Where(a => a.Type == LogetoActivityTypes.Absence)
            .Select(a => a.Guid)
            .ToHashSet();

        if (absenceActivities.Count == 0)
        {
            _logger.LogWarning("No Absence-type activities found in Logeto. Nothing to do.");
            return summary;
        }

        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Select(person => (Person: person, Note: IntegrationNote.Parse(person.Note, options.NoteMarker)))
            .Where(candidate => !candidate.Person.Inactive && candidate.Note.IsEnrolled)
            .ToList();

        if (people.Count == 0)
        {
            _logger.LogWarning("No active Logeto workers found with note marker '{NoteMarker}'", options.NoteMarker);
            return summary;
        }

        var entries = await _client.GetTimeTrackingAsync(from, to, cancellationToken);

        foreach (var (person, note) in people)
        {
            var days = entries
                .Where(e => e.Person == person.Guid && e.Date >= from && e.Date <= to)
                .GroupBy(e => e.Date)
                .OrderBy(g => g.Key);

            foreach (var day in days)
            {
                await ProcessDayAsync(
                    person, note, day.Key, day.ToList(), absenceActivities, summary, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Absence hours finished: {Scanned} timeless absence records scanned, {Filled} filled, " +
            "{NoHours} without configured hours, {Ambiguous} ambiguous, {MixedDay} on mixed days, {Failed} failed",
            summary.RecordsScanned, summary.HoursFilled, summary.SkippedNoHours,
            summary.SkippedAmbiguous, summary.SkippedMixedDay, summary.Failed);

        return summary;
    }

    private async Task ProcessDayAsync(
        LogetoPerson person,
        IntegrationNote note,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlySet<Guid> absenceActivities,
        AbsenceHoursSummary summary,
        CancellationToken cancellationToken)
    {
        var timeless = dayEntries
            .Where(e => absenceActivities.Contains(e.Activity)
                && !e.From.HasValue
                && !e.To.HasValue
                && string.IsNullOrWhiteSpace(e.Hours))
            .ToList();

        if (timeless.Count == 0)
        {
            return;
        }

        summary.RecordsScanned += timeless.Count;

        if (note.DailyHours is null)
        {
            summary.SkippedNoHours++;
            _logger.LogWarning(
                "Skipping {Date} for person {PersonGuid}: their Logeto Note carries no usable daily hours. " +
                "Set it to e.g. '{Marker} 6,4' to enable absence filling.",
                date, person.Guid, _options.Value.NoteMarker);
            return;
        }

        var record = timeless[0];
        var hours = ToApiHours(note.DailyHours.Value);

        var request = new LogetoTimeEntryRequest
        {
            Person = record.Person,
            Activity = record.Activity,
            Date = record.Date,
            Hours = hours,
            Billable = record.Billable,
            Description = record.Description,
            ExternalKey = record.ExternalKey,
            Contract = record.Contract,
            Subcontract = record.Subcontract
        };

        await _client.UpdateTimeEntryAsync(record.Guid, request, cancellationToken);
        summary.HoursFilled++;

        _logger.LogInformation(
            "Filled {Hours} into absence record {EntryGuid} for person {PersonGuid} on {Date}",
            hours, record.Guid, person.Guid, date);
    }

    /// <summary>Formats a duration as the API's required HH:mm:00 (seconds must be zero).</summary>
    private static string ToApiHours(TimeSpan hours) =>
        $"{(int)hours.TotalHours:00}:{hours.Minutes:00}:00";
}

public class AbsenceHoursSummary
{
    public int RecordsScanned { get; set; }
    public int HoursFilled { get; set; }
    public int SkippedNoHours { get; set; }
    public int SkippedAmbiguous { get; set; }
    public int SkippedMixedDay { get; set; }
    public int Failed { get; set; }
}
```

`SkippedAmbiguous` and `SkippedMixedDay` stay at zero until Task 6 — they are declared here so the summary type does not change shape mid-plan.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AbsenceHoursServiceTests"
```

Expected: PASS, 9 tests.

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Attendance/AbsenceHoursOptions.cs \
        backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursServiceTests.cs
git commit -m "feat: fill contracted hours into timeless Logeto absence records"
```

---

### Task 6: The never-guess guards and per-record error isolation

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursServiceTests.cs`

**Interfaces:**
- Consumes: everything from Task 5.
- Produces: no new public surface — `SkippedAmbiguous`, `SkippedMixedDay` and `Failed` start being populated.

- [ ] **Step 1: Write the failing tests**

Append to `AbsenceHoursServiceTests`:

```csharp
    [Fact]
    public async Task SkipsMixedDay_WhenAbsenceSharesTheDayWithWork()
    {
        // A half-day absence beside half a day of work must not receive a full day's hours.
        SetupEntries(
            EmptyAbsence(PastDay),
            new LogetoTimeEntry
            {
                Guid = Guid.NewGuid(),
                Person = Worker,
                Date = PastDay,
                Activity = WorkActivity,
                From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
            });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.SkippedMixedDay.Should().Be(1);
        summary.HoursFilled.Should().Be(0);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipsMixedDay_WhenAnotherAbsenceOnTheDayAlreadyHasHours()
    {
        SetupEntries(
            EmptyAbsence(PastDay),
            new LogetoTimeEntry
            {
                Guid = Guid.NewGuid(),
                Person = Worker,
                Date = PastDay,
                Activity = SickActivity,
                Hours = "02:00:00"
            });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.SkippedMixedDay.Should().Be(1);
        summary.HoursFilled.Should().Be(0);
    }

    [Fact]
    public async Task SkipsAmbiguousDay_WhenTwoTimelessAbsencesShareIt()
    {
        SetupEntries(EmptyAbsence(PastDay), EmptyAbsence(PastDay, SickActivity));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.SkippedAmbiguous.Should().Be(1);
        summary.RecordsScanned.Should().Be(2);
        summary.HoursFilled.Should().Be(0);
    }

    [Fact]
    public async Task SkipsPerson_WhenNoteCarriesNoHours()
    {
        SetupEntries(EmptyAbsence(PastDay));

        var summary = await CreateService(note: "integration").RunAsync(CancellationToken.None);

        summary.SkippedNoHours.Should().Be(1);
        summary.HoursFilled.Should().Be(0);
    }

    [Fact]
    public async Task FailedUpdate_IsCountedAndDoesNotAbortTheRun()
    {
        var failing = EmptyAbsence(new DateOnly(2026, 8, 2));
        var succeeding = EmptyAbsence(PastDay);
        SetupEntries(failing, succeeding);

        _client.Setup(c => c.UpdateTimeEntryAsync(
                failing.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.Failed.Should().Be(1);
        summary.HoursFilled.Should().Be(1);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            succeeding.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AbsenceHoursServiceTests"
```

Expected: the four new guard/isolation tests FAIL — mixed and ambiguous days are currently filled, and the throwing update propagates out of `RunAsync`.

- [ ] **Step 3: Add the guards**

In `ProcessDayAsync`, insert both guards immediately after `summary.RecordsScanned += timeless.Count;` and **before** the `note.DailyHours is null` check, so the reported reason is the real cause:

```csharp
        if (dayEntries.Count > timeless.Count)
        {
            summary.SkippedMixedDay++;
            _logger.LogWarning(
                "Skipping {Date} for person {PersonGuid}: the day mixes a timeless absence with " +
                "{OtherCount} other record(s), so a full day's hours may not apply. Fix manually in Logeto.",
                date, person.Guid, dayEntries.Count - timeless.Count);
            return;
        }

        if (timeless.Count > 1)
        {
            summary.SkippedAmbiguous++;
            _logger.LogWarning(
                "Skipping {Date} for person {PersonGuid}: {Count} timeless absence records share the day, " +
                "so the day's hours cannot be split between them. Fix manually in Logeto.",
                date, person.Guid, timeless.Count);
            return;
        }
```

- [ ] **Step 4: Add per-record error isolation**

In `RunAsync`, wrap the `ProcessDayAsync` call so one bad day cannot abort the run:

```csharp
            foreach (var day in days)
            {
                try
                {
                    await ProcessDayAsync(
                        person, note, day.Key, day.ToList(), absenceActivities, summary, cancellationToken);
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _logger.LogError(ex,
                        "Failed to fill absence hours for person {PersonGuid} on {Date}", person.Guid, day.Key);
                }
            }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AbsenceHoursServiceTests"
```

Expected: PASS, 14 tests — the nine from Task 5 still green.

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursServiceTests.cs
git commit -m "feat: guard absence filling against mixed and ambiguous days"
```

---

### Task 7: The recurring job, registration, and config

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (the `Logeto` section, around line 607)
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursJobTests.cs`

**Interfaces:**
- Consumes: `AbsenceHoursService`, `AbsenceHoursOptions` (Task 5).
- Produces: job name `logeto-absence-hours`.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class AbsenceHoursJobTests
{
    // AbsenceHoursService is a concrete class (no interface), so it can't be mocked directly.
    // We build a real service around a mocked ILogetoClient and use GetActivitiesAsync — the
    // first call RunAsync makes — as a proxy for "did RunAsync execute".
    private readonly Mock<ILogetoClient> _client = new();

    private AbsenceHoursJob CreateJob(Mock<IRecurringJobStatusChecker> statusCheckerMock)
    {
        var service = new AbsenceHoursService(
            _client.Object,
            Options.Create(new AbsenceHoursOptions { StartDate = new DateOnly(2026, 8, 1) }),
            TimeProvider.System,
            NullLogger<AbsenceHoursService>.Instance);

        return new AbsenceHoursJob(
            service,
            statusCheckerMock.Object,
            NullLogger<AbsenceHoursJob>.Instance);
    }

    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(enabled);
        return mock;
    }

    private void SetupClientToReturnEarly() =>
        // No Absence-type activity means RunAsync returns right after GetActivitiesAsync,
        // so GetPeopleAsync/GetTimeTrackingAsync need no stubbing.
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>());

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenJobIsDisabled()
    {
        SetupClientToReturnEarly();

        await CreateJob(StatusChecker(enabled: false)).ExecuteAsync(CancellationToken.None);

        _client.Verify(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RunsService_WhenJobIsEnabled()
    {
        SetupClientToReturnEarly();

        await CreateJob(StatusChecker(enabled: true)).ExecuteAsync(CancellationToken.None);

        _client.Verify(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Metadata_ShipsDisabledWithItsOwnJobName()
    {
        var metadata = CreateJob(StatusChecker(enabled: false)).Metadata;

        metadata.JobName.Should().Be("logeto-absence-hours");
        metadata.DefaultIsEnabled.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
```

Expected: FAIL to compile — `AbsenceHoursJob` does not exist.

- [ ] **Step 3: Write the job**

Create `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;

public class AbsenceHoursJob : IRecurringJob
{
    private readonly AbsenceHoursService _service;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<AbsenceHoursJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "logeto-absence-hours",
        DisplayName = "Logeto — fill hours into absence records",
        Description = "Writes each opted-in worker's net daily contracted hours (from their Logeto " +
                      "Note, e.g. 'integration 6,4') into past absence records that were entered " +
                      "with no time at all.",
        CronExpression = "0 4 * * *",
        DefaultIsEnabled = false
    };

    public AbsenceHoursJob(
        AbsenceHoursService service,
        IRecurringJobStatusChecker statusChecker,
        ILogger<AbsenceHoursJob> logger)
    {
        _service = service;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);
        await _service.RunAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Register options and service**

In `AttendanceModule.AddAttendanceModule`, after the `BreakInsertionOptions` binding and the `BreakInsertionService` registration:

```csharp
        services.AddOptions<AbsenceHoursOptions>()
            .Bind(configuration.GetSection(AbsenceHoursOptions.ConfigKey));

        services.AddScoped<Services.AbsenceHoursService>();
```

The job itself needs no registration — it is auto-discovered by the `IRecurringJob` assembly scan in `AddRecurringJobs()`, same as `BreakInsertionJob`.

- [ ] **Step 5: Add the config section**

In `backend/src/Anela.Heblo.API/appsettings.json`, inside the existing `"Logeto"` object, after the `"BreakInsertion"` block:

```json
    "AbsenceHours": {
      "StartDate": "2026-08-01",
      "LookbackDays": 7,
      "NoteMarker": "integration"
    }
```

Remember the comma after the closing brace of `"BreakInsertion"`.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Attendance"
dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests/Anela.Heblo.Adapters.Logeto.Tests.csproj --no-build
```

Expected: PASS across the whole attendance area.

Verify the JSON is still valid:

```bash
python3 -c "import json; json.load(open('backend/src/Anela.Heblo.API/appsettings.json')); print('appsettings.json OK')"
```

- [ ] **Step 7: Format and commit**

```bash
dotnet format Anela.Heblo.sln
git add backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs \
        backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/test/Anela.Heblo.Tests/Features/Attendance/AbsenceHoursJobTests.cs
git commit -m "feat: schedule nightly Logeto absence-hours job"
```

---

### Task 8: Full-suite verification

**Files:** none — this is the gate before opening a PR.

**Interfaces:**
- Consumes: everything.
- Produces: nothing.

- [ ] **Step 1: Build and format the whole solution**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln --verify-no-changes
```

Expected: build succeeds, formatting is clean. An `AccessMatrixGen` crash in build output is pre-existing noise.

- [ ] **Step 2: Run the full backend test suite**

```bash
dotnet test Anela.Heblo.sln --no-build
```

Expected: PASS. If unrelated pre-existing failures appear, confirm they also fail on `main` before investigating.

- [ ] **Step 3: Confirm the contract test on response types still passes**

The reflection contract test fails in CI for any Application `*Response` type not extending `BaseResponse`. `AbsenceHoursSummary` is deliberately **not** named `*Response`, matching `BreakInsertionSummary`. Confirm:

```bash
dotnet test Anela.Heblo.sln --no-build --filter "FullyQualifiedName~Contract"
```

Expected: PASS.

- [ ] **Step 4: Commit any formatting fallout**

```bash
git status --short
# if dotnet format changed anything:
git add -A && git commit -m "chore: apply dotnet format"
```

---

## Deployment note — not a code task

The job fills nothing until the Logeto notes carry hours. After merge, update `Pracovníci → Note` in Logeto:

| Person | Current | New |
|---|---|---|
| Andrea Pajgrt | `integration` | `integration 8` |
| Petra Zilvarová | `integration` | `integration 6,4` |
| Olga Petrová | `integration` | `integration 6,4` |
| Lydie Fellnerová | `integration` | confirm against `Úvazky pracovníků`; `HPP 0,8` suggests `integration 6,4` |

Then enable `logeto-absence-hours` in the background-jobs admin UI. Until a person's note is updated, their absence days are skipped with a `SkippedNoHours` warning naming them, and their break insertion is unaffected.
