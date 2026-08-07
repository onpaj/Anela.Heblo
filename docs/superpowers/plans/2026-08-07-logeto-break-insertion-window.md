# Logeto Break Insertion — Rolling Window and Same-Day Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound the nightly Logeto break-insertion walk to a rolling 7-day window above a fixed floor date, and let it process today's days as long as no record is still open.

**Architecture:** Two surgical changes to the existing `BreakInsertionService`. The date range it walks becomes `[max(StartDate, today - LookbackDays), today]` instead of `[StartDate, today - 1]`, computed before any API call so an empty window costs nothing. A new per-day guard skips any day holding an entry with `From` set and `To` null — the open record Logeto keeps while a worker is clocked in — which is what makes including today safe. No change to slot placement, thresholds, merge semantics, or the cron schedule.

**Tech Stack:** .NET 8, C#, xUnit + FluentAssertions + Moq, Hangfire recurring jobs.

**Spec:** `docs/superpowers/specs/2026-08-07-logeto-break-insertion-window-design.md`. It amends `docs/superpowers/specs/2026-08-05-logeto-break-insertion-design.md`, which remains the reference for everything not restated.

## Global Constraints

- DTOs crossing the API boundary are **classes, never C# records** (project rule). `BreakInsertionOptions` and `BreakInsertionSummary` are already classes — keep them so.
- Never mutate an existing option or entry object; the service reads `IOptions<BreakInsertionOptions>.Value` and must not write to it.
- `StartDate` keeps its current value `2026-08-01` and its current meaning: an absolute floor the walk never crosses.
- Existing tests in `BreakInsertionServiceTests` must keep passing **unchanged**. Their fixed "now" is `2026-08-04 06:00Z` (Prague 08:00, so `today` = `2026-08-04`) and `StartDate` is `2026-08-01`; with a 7-day lookback the window clamps to `[2026-08-01, 2026-08-04]`, which still contains their `Day` of `2026-08-03`. If one of them breaks, the implementation is wrong — do not edit the test.
- The Czech strings `"Přestávka"` and `"Automatická přestávka"` are load-bearing (they match live Logeto data). Do not touch them.
- Build and test commands for this repo — a concurrent build in another worktree makes plain `dotnet test` hang at 0% CPU, so always build first and then test with `--no-build`:

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~BreakInsertion"
```

An `AccessMatrixGen` crash during the build is known non-fatal noise; ignore it.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/Attendance/BreakInsertionOptions.cs` | Modify | Gains `LookbackDays`. |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs` | Modify | Window computation in `RunAsync`; in-progress guard in `ProcessDayAsync`; `SkippedInProgress` on `BreakInsertionSummary`. |
| `backend/src/Anela.Heblo.API/appsettings.json:610-618` | Modify | Explicit `LookbackDays` default. |
| `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs` | Modify | All new tests. Existing cases untouched. |

No new files. `BreakSlotCalculator`, `LogetoTimeConverter`, `BreakInsertionJob`, and the Logeto adapter are not touched by this plan.

---

### Task 1: Rolling window with the `StartDate` floor

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/BreakInsertionOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs:51-61`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json:610-618`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `BreakInsertionOptions.LookbackDays` (`int`, default `7`). `BreakInsertionService.ProcessDayAsync` gains a `DateOnly today` parameter positioned immediately before `CancellationToken cancellationToken` — Task 2 relies on that parameter existing.

- [ ] **Step 1: Write the failing tests**

Add these three tests to `BreakInsertionServiceTests.cs`, immediately after the existing `InsertsBreak_ForEightHourDayWithoutBreak` test:

```csharp
    [Fact]
    public async Task RequestsTimeTracking_ForTheRollingWindow_WhenStartDateIsFarInThePast()
    {
        SetupDefaults();
        var options = new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 1, 1), // far past — the lookback governs
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false
        };

        await CreateService(options).RunAsync(CancellationToken.None);

        // "now" is 2026-08-04; default lookback of 7 days → window starts 2026-07-28, ends today.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 7, 28), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClampsWindowStart_ToStartDate_WhenLookbackReachesPastIt()
    {
        SetupDefaults();

        // Default options: StartDate 2026-08-01, lookback 7 → 2026-07-28 clamped up to the floor.
        await CreateService().RunAsync(CancellationToken.None);

        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNothing_AndCallsNoApi_WhenStartDateIsInTheFuture()
    {
        SetupDefaults();
        var options = new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 9, 1), // after "now" of 2026-08-04
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false
        };

        var summary = await CreateService(options).RunAsync(CancellationToken.None);

        summary.DaysScanned.Should().Be(0);
        summary.BreaksInserted.Should().Be(0);
        _client.Verify(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.GetTimeTrackingAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~BreakInsertionServiceTests"
```

Expected: the three new tests compile (they rely on the default lookback, never naming `LookbackDays`) and all three FAIL. The current service requests `[2026-08-01, 2026-08-03]` — it ends at *yesterday* and ignores the lookback entirely — and it calls `GetActivitiesAsync`/`GetPeopleAsync` before looking at the date range at all.

- [ ] **Step 3: Add the `LookbackDays` option**

In `BreakInsertionOptions.cs`, insert after the `StartDate` property:

```csharp
    /// <summary>Days of history scanned before today. The walk covers
    /// [max(StartDate, today - LookbackDays), today], so the default spans 8 calendar days.
    /// Bounds the nightly scan cost while letting transiently-skipped days be retried.</summary>
    public int LookbackDays { get; set; } = 7;
```

- [ ] **Step 4: Move the window computation to the top of `RunAsync` and extend it to today**

In `BreakInsertionService.cs`, replace lines 28-54 (from `var options = _options.Value;` through the `GetTimeTrackingAsync` call) with:

```csharp
        var options = _options.Value;
        var summary = new BreakInsertionSummary();

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var today = DateOnly.FromDateTime(pragueNow.Date);
        var windowStart = today.AddDays(-options.LookbackDays);
        var from = windowStart < options.StartDate ? options.StartDate : windowStart;

        if (from > today)
        {
            _logger.LogWarning(
                "Break insertion window is empty: StartDate {StartDate} is after today {Today}. Nothing to do.",
                options.StartDate, today);
            return summary;
        }

        var activities = await _client.GetActivitiesAsync(cancellationToken);
        var breakActivity = activities.FirstOrDefault(a =>
                a.Type == LogetoActivityTypes.Break
                && string.Equals(a.Name?.Trim(), options.BreakActivityName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Break activity '{options.BreakActivityName}' not found in Logeto or is not of type Break.");

        var typeByActivity = activities.ToDictionary(a => a.Guid, a => a.Type);

        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Where(p => !p.Inactive
                && string.Equals(p.Note?.Trim(), options.NoteMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (people.Count == 0)
        {
            _logger.LogWarning("No active Logeto workers found with note marker '{NoteMarker}'", options.NoteMarker);
            return summary;
        }

        var entries = await _client.GetTimeTrackingAsync(from, today, cancellationToken);
```

The activity/people fetching blocks are unchanged apart from now sitting *after* the window guard. The whole point of the reordering is that an empty window costs zero API calls.

- [ ] **Step 5: Update the per-person day filter and thread `today` through**

In the same file, replace the per-person filter and the `ProcessDayAsync` call:

```csharp
            var days = entries
                .Where(e => e.Person == person.Guid && e.Date >= from && e.Date <= today)
                .GroupBy(e => e.Date)
                .OrderBy(g => g.Key);

            foreach (var day in days)
            {
                try
                {
                    await ProcessDayAsync(
                        person, day.Key, day.ToList(), typeByActivity, breakActivity, options, summary,
                        today, cancellationToken);
                }
```

and add the matching parameter to the `ProcessDayAsync` signature, immediately before `cancellationToken`:

```csharp
    private async Task ProcessDayAsync(
        LogetoPerson person,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlyDictionary<Guid, string> typeByActivity,
        LogetoActivity breakActivity,
        BreakInsertionOptions options,
        BreakInsertionSummary summary,
        DateOnly today,
        CancellationToken cancellationToken)
```

`today` is unused in `ProcessDayAsync` until Task 2 — that is expected and will not fail the build.

The old `lastDay` local is gone. Verify no reference to it remains.

- [ ] **Step 6: Add the config default**

In `backend/src/Anela.Heblo.API/appsettings.json`, inside the `Logeto.BreakInsertion` object, add `LookbackDays` after `StartDate`:

```json
    "BreakInsertion": {
      "StartDate": "2026-08-01",
      "LookbackDays": 7,
      "NoteMarker": "integration",
      "BreakActivityName": "Přestávka",
      "PreferredWindowStart": "11:00",
      "BreakDurationMinutes": 30,
      "MinWorkHours": 6,
      "ApiTimesAreUtc": false
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~BreakInsertion"
```

Expected: PASS — the three new tests plus **all 11 pre-existing** `BreakInsertionServiceTests` and all `BreakSlotCalculatorTests`. If a pre-existing test now fails, the window arithmetic is wrong; fix the service, not the test.

- [ ] **Step 8: Format and commit**

```bash
dotnet format Anela.Heblo.sln --no-restore
git add backend/src/Anela.Heblo.Application/Features/Attendance/BreakInsertionOptions.cs \
        backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs
git commit -m "feat: bound Logeto break insertion to a rolling window above the start date"
```

---

### Task 2: Skip days with an open record, so today is safe to process

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs` (`ProcessDayAsync` and `BreakInsertionSummary`)
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs`

**Interfaces:**
- Consumes: `ProcessDayAsync`'s `DateOnly today` parameter from Task 1.
- Produces: `BreakInsertionSummary.SkippedInProgress` (`int`). Both log messages contain the literal substring `open record`, which the tests assert on.

- [ ] **Step 1: Write the failing tests**

First add a date-parameterised entry helper. In `BreakInsertionServiceTests.cs`, replace the existing `WorkEntry` helper (currently at lines 64-72) with these two, and add the `Today` constant next to the existing `Day` constant:

```csharp
    private static readonly DateOnly Today = new(2026, 8, 4); // matches the fixed "now" in CreateService

    private static LogetoTimeEntry WorkEntryOn(DateOnly date, int fromHour, int fromMin, int toHour, int toMin) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = date,
        Activity = WorkActivity,
        From = new DateTimeOffset(date.Year, date.Month, date.Day, fromHour, fromMin, 0, TimeSpan.Zero),
        To = new DateTimeOffset(date.Year, date.Month, date.Day, toHour, toMin, 0, TimeSpan.Zero)
    };

    private static LogetoTimeEntry WorkEntry(int fromHour, int fromMin, int toHour, int toMin) =>
        WorkEntryOn(Day, fromHour, fromMin, toHour, toMin);
```

`WorkEntry` keeps its exact old signature and behaviour, so every existing test still compiles and passes.

Then add these four tests:

```csharp
    [Fact]
    public async Task SkipsDay_WhenAnEntryIsStillOpen()
    {
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 3, 17, 0, 0, TimeSpan.Zero),
            To = null // still clocked in
        };
        SetupDefaults(WorkEntry(8, 0, 16, 30), openEntry); // 8.5 h closed work would otherwise qualify

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedInProgress.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogsWarning_WhenAPastDayHasAnOpenRecord()
    {
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day, // 2026-08-03, before "today" — the worker never clocked out
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            To = null
        };
        SetupDefaults(openEntry);

        var loggerMock = new Mock<ILogger<BreakInsertionService>>();

        await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("open record")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DoesNotWarn_WhenTodayHasAnOpenRecord()
    {
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Today, // worker is at work right now — expected, not an anomaly
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            To = null
        };
        SetupDefaults(openEntry);

        var loggerMock = new Mock<ILogger<BreakInsertionService>>();

        var summary = await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        summary.SkippedInProgress.Should().Be(1);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("open record")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InsertsBreak_ForToday_WhenAllRecordsAreClosed()
    {
        SetupDefaults(WorkEntryOn(Today, 6, 0, 14, 30)); // 8.5 h, finished

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoCreateTimeEntryRequest>(r =>
                r.Date == Today
                && r.From == "2026-08-04T11:00:00"
                && r.To == "2026-08-04T11:30:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~BreakInsertionServiceTests"
```

Expected: compile error `'BreakInsertionSummary' does not contain a definition for 'SkippedInProgress'`.

- [ ] **Step 3: Add the summary counter**

In `BreakInsertionService.cs`, add to the `BreakInsertionSummary` class after `SkippedExistingBreak`:

```csharp
    public int SkippedInProgress { get; set; }
```

- [ ] **Step 4: Add the in-progress guard**

In `ProcessDayAsync`, insert this block immediately after `summary.DaysScanned++;` and **before** the existing-break check:

```csharp
        if (dayEntries.Any(e => e.From.HasValue && !e.To.HasValue))
        {
            summary.SkippedInProgress++;

            if (date < today)
            {
                _logger.LogWarning(
                    "Skipping {Date} for person {PersonGuid}: an open record (no end time) is present — " +
                    "the worker never clocked out; fix it manually in Logeto.",
                    date, person.Guid);
            }
            else
            {
                _logger.LogDebug(
                    "Skipping {Date} for person {PersonGuid}: an open record (no end time) is present — " +
                    "the worker is still at work.",
                    date, person.Guid);
            }

            return;
        }
```

Order matters: this runs before the existing-break check so an unfinished day is never counted as anything else. Hours-only records have both `From` and `To` null and are deliberately not matched here — they keep their existing handling further down.

- [ ] **Step 5: Add the counter to the run summary log**

Replace the `_logger.LogInformation` call at the end of `RunAsync` with:

```csharp
        _logger.LogInformation(
            "Break insertion finished: {Scanned} days scanned, {Inserted} breaks inserted, " +
            "{ExistingBreak} had a break, {InProgress} in progress, {BelowThreshold} below threshold, " +
            "{HoursOnly} hours-only, {NoSlot} no slot, {Failed} failed",
            summary.DaysScanned, summary.BreaksInserted, summary.SkippedExistingBreak,
            summary.SkippedInProgress, summary.SkippedBelowThreshold, summary.SkippedHoursOnly,
            summary.SkippedNoSlot, summary.Failed);
```

Count the placeholders against the arguments — there are eight of each, in that order.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~BreakInsertion"
```

Expected: PASS, all tests including Task 1's and every pre-existing one.

- [ ] **Step 7: Format and commit**

```bash
dotnet format Anela.Heblo.sln --no-restore
git add backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs \
        backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs
git commit -m "feat: skip Logeto days with an open record so today can be processed"
```

---

### Task 3: Lock in one break per day for split shifts

No production change. This closes the coverage gap the spec's R3 names: `BreakSlotCalculatorTests` covers multi-segment placement, but no service-level test proves a two-shift day yields exactly one insert.

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs`

**Interfaces:**
- Consumes: the `WorkEntry` helper (unchanged signature) from Task 2.
- Produces: nothing.

- [ ] **Step 1: Write the test**

Add to `BreakInsertionServiceTests.cs`:

```csharp
    [Fact]
    public async Task InsertsExactlyOneBreak_ForTwelveHourDayWorkedInTwoShifts()
    {
        // Two 6 h shifts with an hour between them: BuildSegments keeps them separate
        // (not adjacent), and ComputeBreakSlot returns a single slot regardless.
        SetupDefaults(WorkEntry(6, 0, 12, 0), WorkEntry(13, 0, 19, 0));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoCreateTimeEntryRequest>(r =>
                r.Date == Day
                && r.From == "2026-08-03T11:00:00" // preferred window sits strictly inside the morning shift
                && r.To == "2026-08-03T11:30:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run it — it must pass on the first run**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~InsertsExactlyOneBreak_ForTwelveHourDayWorkedInTwoShifts"
```

Expected: PASS immediately. This is a characterisation test for behaviour that already holds — unlike the TDD cycles above, a failure here means the *existing* code does not do what the spec claims, so stop and report rather than changing production code to suit the test.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs
git commit -m "test: assert one break per day for a split-shift twelve hour day"
```

---

### Task 4: Full backend verification

**Files:** none modified.

- [ ] **Step 1: Build the whole solution**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
```

Expected: build succeeded, 0 errors. An `AccessMatrixGen` crash is known non-fatal noise.

- [ ] **Step 2: Run the full backend test suite**

```bash
dotnet test Anela.Heblo.sln --no-build -p:UseSharedCompilation=false
```

Expected: all tests pass. The reflection-based contract tests (every Application `*Response` must inherit `BaseResponse`) are unaffected — this plan adds no `*Response` type.

- [ ] **Step 3: Confirm formatting is clean**

```bash
dotnet format Anela.Heblo.sln --no-restore --verify-no-changes
```

Expected: exit 0, no output. If it reports changes, run without `--verify-no-changes` and amend the last commit.
