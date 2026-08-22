# Logeto Contract Hours — Single Source of Truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Logeto person `Note` (`integration 6,4`) the only source of daily contract hours in Heblo, and restore break insertion, which has silently inserted nothing since 2026-08-11 because it matched that note with exact equality.

**Architecture:** A new `IntegrationNote` domain type parses the note into `(IsEnrolled, DailyHours)`. `BreakInsertionService` selects people through it instead of exact equality. A new `LogetoContractHoursProvider` implements the overtime ledger's existing `IContractHoursProvider` from the same parsed note, and the `Overtime:ContractHours` configuration dictionary — the second source of the same fact — is deleted outright.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq.

**Spec:** `docs/superpowers/specs/2026-08-14-logeto-contract-hours-single-source.md`, which extends the still-accurate `docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md`.

## Relationship to the 2026-08-10 absence-hours plan

Tasks 1 and 2 below are **taken verbatim** from Tasks 1 and 2 of
`docs/superpowers/plans/2026-08-10-logeto-absence-hours.md`, with one additive change
(the `DefaultMarker` constant and the single-argument `Parse` overload, needed by Task 3).
When that plan is executed later, **skip its Tasks 1 and 2** — they are delivered here.
Its Tasks 3–8 (the absence-hours job) are untouched by this plan and remain to be done.

## Global Constraints

- **DTOs are classes, never C# records** — OpenAPI client generators mishandle record parameter order. (`docs/architecture/development_guidelines.md`)
- **Decimal parsing must be culture-invariant** and accept both `,` and `.`, so behaviour does not depend on server locale.
- **Net hours, not gross.** `80% úvazek bez pauz (6,4hod/den)` → `6,4`. A vacation day carries no unpaid break.
- **No fallback to configuration.** A person without parseable hours in their note has no contract hours; that is already handled as `"Chybí úvazek"` + a blocked month close.
- Validation before completion: `dotnet build` + `dotnet format`, and all touched tests pass.

## Build & Test Commands

Run the build once, then test with `--no-build`. Concurrent `dotnet test` across worktrees hangs at 0% CPU, hence `-p:UseSharedCompilation=false`:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~<TestClass>"
```

An `AccessMatrixGen` crash during the build is pre-existing noise, not a failure.

## File Structure

| File | Responsibility |
|---|---|
| `Domain/Features/Attendance/IntegrationNote.cs` | **new** — parse a person's `Note` into enrollment + net daily hours |
| `Domain/Features/Attendance/LogetoActivityTypes.cs` | `+ Absence` constant (needed by the later absence-hours plan; free to add here) |
| `Application/Features/Attendance/Services/BreakInsertionService.cs` | person selection moves to `IntegrationNote` |
| `Application/Features/Attendance/Overtime/Services/LogetoContractHoursProvider.cs` | **new** — `IContractHoursProvider` from the note, scoped-memoized |
| `Application/Features/Attendance/Overtime/Services/ConfigurationContractHoursProvider.cs` | **deleted** |
| `Application/Features/Attendance/Overtime/OvertimeOptions.cs` | `- ContractHours` |
| `Application/Features/Attendance/Overtime/OvertimeModule.cs` | register the new provider |
| `API/appsettings.json` | `- Overtime:ContractHours` |

---

### Task 1: `IntegrationNote` — parse enrollment and hours from the Note

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/IntegrationNoteTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IntegrationNote.Parse(string? note, string marker) → IntegrationNote` and the overload `IntegrationNote.Parse(string? note)` using `IntegrationNote.DefaultMarker` (`"integration"`), with `bool IsEnrolled` and `TimeSpan? DailyHours`. Also `LogetoActivityTypes.Absence` (value `"Absence"`). Tasks 2 and 3 depend on both.

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
    public void Parse_UsesDefaultMarker_WhenMarkerIsOmitted()
    {
        // The overtime provider has no per-job NoteMarker option of its own.
        var result = IntegrationNote.Parse("integration 6,4");

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().Be(new TimeSpan(6, 24, 0));
        IntegrationNote.DefaultMarker.Should().Be("integration");
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
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
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
    /// <summary>Marker used by callers that have no configurable NoteMarker of their own.</summary>
    public const string DefaultMarker = "integration";

    private const double MinDailyHours = 0;
    private const double MaxDailyHours = 24;

    private static readonly IntegrationNote NotEnrolledNote = new() { IsEnrolled = false };

    public bool IsEnrolled { get; private init; }

    /// <summary>Net daily hours, or null when the note carries no usable number.</summary>
    public TimeSpan? DailyHours { get; private init; }

    public static IntegrationNote Parse(string? note) => Parse(note, DefaultMarker);

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
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~IntegrationNoteTests"
```

Expected: PASS, 21 tests.

- [ ] **Step 5: Format and commit**

```bash
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --no-restore
git add backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs \
        backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/IntegrationNoteTests.cs
git commit -m "feat: parse integration opt-in and daily hours from Logeto person note"
```

---

### Task 2: Break insertion adopts the shared note parser

This is the production outage: since 2026-08-11 the nightly job has matched zero people,
because every enrolled note now reads `integration 6.4` rather than `integration`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs:54-57`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs`

**Interfaces:**
- Consumes: `IntegrationNote.Parse` from Task 1.
- Produces: nothing new.

- [ ] **Step 1: Write the failing regression test**

Append to `BreakInsertionServiceTests`, after the existing `IgnoresPeople_WithoutTheNoteMarker` test:

```csharp
    [Fact]
    public async Task SelectsPerson_WhenNoteCarriesDailyHours()
    {
        // The note carries the person's úvazek: "integration 6,4". Break insertion used to
        // match the note with exact equality, which silently dropped this person.
        SetupDefaults(WorkEntry(8, 0, 16, 30));
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "integration 6,4", Inactive = false }
            });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
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
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~BreakInsertion"
```

Expected: PASS, including every pre-existing test — plain `"integration"` still enrolls.

- [ ] **Step 5: Format and commit**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
git add backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs
git commit -m "fix: select break-insertion people via shared IntegrationNote parser"
```

---

### Task 3: The overtime ledger reads úvazek from Logeto, and the config table is deleted

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/LogetoContractHoursProvider.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/ConfigurationContractHoursProvider.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeOptions.cs` (remove `ContractHours`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeModule.cs:23`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (remove `Overtime:ContractHours`, around line 633)
- Create test: `backend/test/Anela.Heblo.Tests/Application/Overtime/LogetoContractHoursProviderTests.cs`
- Delete test: `backend/test/Anela.Heblo.Tests/Application/Overtime/ConfigurationContractHoursProviderTests.cs`

**Interfaces:**
- Consumes: `IntegrationNote.Parse(string?)` from Task 1; the existing `IContractHoursProvider.GetDailyHoursAsync(Guid personId, int year, int month, CancellationToken) → Task<decimal?>` and `ILogetoClient.GetPeopleAsync(CancellationToken) → Task<IReadOnlyList<LogetoPerson>>`.
- Produces: `LogetoContractHoursProvider : IContractHoursProvider`, registered scoped. No new public surface for later tasks.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Application/Overtime/LogetoContractHoursProviderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class LogetoContractHoursProviderTests
{
    private static readonly Guid Person = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherPerson = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<ILogetoClient> _client = new();

    private LogetoContractHoursProvider CreateProvider(params LogetoPerson[] people)
    {
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(people.ToList());

        return new LogetoContractHoursProvider(_client.Object);
    }

    [Theory]
    [InlineData("integration 6,4")]
    [InlineData("integration 6.4")]
    public async Task ReturnsHoursFromNote_RegardlessOfDecimalSeparator(string note)
    {
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = note });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().Be(6.4m);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoteCarriesNoHours()
    {
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = "integration" });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenPersonIsNotInLogeto()
    {
        var provider = CreateProvider(new LogetoPerson { Guid = OtherPerson, Note = "integration 8" });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenPersonIsNotEnrolled_EvenWithANumberInTheNote()
    {
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = "brigáda 6,4" });

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        hours.Should().BeNull();
    }

    [Fact]
    public async Task IgnoresYearAndMonth()
    {
        // A note has no history. Closed statements freeze their own RequiredHours, so an
        // open month always follows the current úvazek.
        var provider = CreateProvider(new LogetoPerson { Guid = Person, Note = "integration 8" });

        var august = await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);
        var january = await provider.GetDailyHoursAsync(Person, 2025, 1, CancellationToken.None);

        august.Should().Be(8m);
        january.Should().Be(8m);
    }

    [Fact]
    public async Task FetchesPeopleOnce_PerScope()
    {
        var provider = CreateProvider(
            new LogetoPerson { Guid = Person, Note = "integration 8" },
            new LogetoPerson { Guid = OtherPerson, Note = "integration 6,4" });

        await provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);
        await provider.GetDailyHoursAsync(OtherPerson, 2026, 8, CancellationToken.None);
        await provider.GetDailyHoursAsync(Person, 2026, 7, CancellationToken.None);

        _client.Verify(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PropagatesClientFailure()
    {
        // A Logeto outage must not read as "nobody has an úvazek" across the whole company.
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("logeto down"));
        var provider = new LogetoContractHoursProvider(_client.Object);

        var act = () => provider.GetDailyHoursAsync(Person, 2026, 8, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```

Expected: FAIL to compile — `The type or namespace name 'LogetoContractHoursProvider' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/LogetoContractHoursProvider.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>
/// Daily contract hours come from the person's Logeto Note ("integration 6,4") — the single
/// source of truth for úvazek, since the Logeto API exposes it nowhere else. See
/// docs/superpowers/specs/2026-08-14-logeto-contract-hours-single-source.md.
///
/// Registered scoped: the people lookup is memoized for the lifetime of the scope, so one
/// request or job run issues a single Logeto call however many people it asks about.
/// </summary>
public class LogetoContractHoursProvider : IContractHoursProvider
{
    private readonly ILogetoClient _client;
    private Task<IReadOnlyDictionary<Guid, TimeSpan?>>? _hoursByPerson;

    public LogetoContractHoursProvider(ILogetoClient client)
    {
        _client = client;
    }

    /// <summary>
    /// year/month are ignored: a Note states only the person's current úvazek and carries no
    /// history. Closed statements freeze their own RequiredHours, so only open months
    /// recompute — and an open month should follow the current note.
    /// </summary>
    public async Task<decimal?> GetDailyHoursAsync(
        Guid personId, int year, int month, CancellationToken cancellationToken)
    {
        var hoursByPerson = await LoadOnceAsync(cancellationToken);

        return hoursByPerson.TryGetValue(personId, out var hours) && hours.HasValue
            ? (decimal)hours.Value.Ticks / TimeSpan.TicksPerHour
            : null;
    }

    private Task<IReadOnlyDictionary<Guid, TimeSpan?>> LoadOnceAsync(CancellationToken cancellationToken) =>
        // Memoizes the task rather than the result, so concurrent callers in one scope share
        // a single request instead of racing. A failure is cached for the scope too: one
        // outage surfaces once, not once per person.
        _hoursByPerson ??= LoadAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, TimeSpan?>> LoadAsync(CancellationToken cancellationToken)
    {
        var people = await _client.GetPeopleAsync(cancellationToken);
        var hoursByPerson = new Dictionary<Guid, TimeSpan?>();

        foreach (var person in people)
        {
            var note = IntegrationNote.Parse(person.Note);
            if (note.IsEnrolled)
            {
                hoursByPerson[person.Guid] = note.DailyHours;
            }
        }

        return hoursByPerson;
    }
}
```

Delete `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/ConfigurationContractHoursProvider.cs` and `backend/test/Anela.Heblo.Tests/Application/Overtime/ConfigurationContractHoursProviderTests.cs`.

In `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeOptions.cs`, remove:

```csharp
    /// <summary>Person GUID (string) → daily contract hours without break. Temporary source
    /// until the Logeto-backed IContractHoursProvider lands.</summary>
    public Dictionary<string, decimal> ContractHours { get; set; } = new();
```

In `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeModule.cs`, replace:

```csharp
        services.AddScoped<IContractHoursProvider, Services.ConfigurationContractHoursProvider>();
```

with:

```csharp
        services.AddScoped<IContractHoursProvider, Services.LogetoContractHoursProvider>();
```

In `backend/src/Anela.Heblo.API/appsettings.json`, remove the `"ContractHours": {},` line from the `Overtime` section.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Overtime"
```

Expected: PASS. `OvertimeCalculationServiceTests.MissingContractHours_ProducesWarning_AndNullContract` and `CloseMonthHandlerTests.Close_Fails_WhenContractHoursMissing` mock `IContractHoursProvider` directly and are unaffected.

- [ ] **Step 5: Format and commit**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
git add -A
git commit -m "feat: read overtime contract hours from Logeto note, delete config table"
```

---

### Task 4: Full-suite verification and PR

**Files:** none — this is the gate before opening a PR.

**Interfaces:**
- Consumes: everything.
- Produces: nothing.

- [ ] **Step 1: Confirm no reference to the deleted config survives**

```bash
grep -rn "ContractHours" --include="*.cs" --include="*.json" backend/src backend/test \
  | grep -v "DailyContractHours\|IContractHoursProvider\|OvertimeContractHoursMissing\|LogetoContractHoursProvider\|/bin/"
```

Expected: no output.

- [ ] **Step 2: Run the whole backend suite**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
```

Expected: PASS, 0 failed. (Baseline before this plan: 6453 passed.)

- [ ] **Step 3: Verify formatting across both touched projects**

```bash
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --verify-no-changes --no-restore
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes --no-restore
```

Expected: exit 0 for both.

- [ ] **Step 4: Mark Tasks 1–2 of the absence-hours plan as delivered**

Add a note under the header of `docs/superpowers/plans/2026-08-10-logeto-absence-hours.md` so the
next executor does not implement `IntegrationNote` twice:

```markdown
> **Status 2026-08-14:** Tasks 1 and 2 are already implemented — delivered by
> `docs/superpowers/plans/2026-08-14-logeto-contract-hours-single-source.md`.
> Start from Task 3.
```

- [ ] **Step 5: Push and open the PR**

```bash
git add -A && git commit -m "docs: mark absence-hours tasks 1-2 as delivered"
git push -u origin feat/logeto-contract-hours-single-source
gh pr create --base main --title "feat: Logeto note is the single source of contract hours" --body "..."
```

The PR body must state: the break-insertion outage this fixes, the Aug 18 backfill deadline,
that `Overtime:ContractHours` had no production value so nothing is lost, and that PR #3928
(the stop-gap exact-match fix) is superseded and should be closed.
