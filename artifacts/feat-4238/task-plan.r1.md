# Persist an IsAllDay flag on MarketingAction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist an `IsAllDay` boolean on `MarketingAction`, set it from Microsoft Graph's own `isAllDay` on import and read it directly (never guessed) on export, so a timed Outlook event can no longer silently mutate into an all-day event across an edit round-trip.

**Architecture:** Add `IsAllDay` as a domain-owned, `private set` property on `MarketingAction`, set explicitly by every constructor/`UpdateDetails` call (the import mapper passes Graph's `evt.IsAllDay`; the two manual-create/update handlers compute it via a new `MarketingAction.ComputeIsAllDay` static helper that reproduces today's midnight-to-midnight guess). `OutlookCalendarSyncService.BuildEventBody` reads `action.IsAllDay` directly and the old `IsDateOnly` guess is deleted. `Reschedule` is untouched — a move never changes all-day-ness. An EF Core migration adds the column and backfills existing rows with the same midnight-to-midnight rule so no row's live export behavior changes at migration time.

**Tech Stack:** .NET 8, EF Core (PostgreSQL), MediatR, xUnit, FluentAssertions, Moq.

---

### task: domain-isalldy-property

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionTestBuilder.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionConstructorTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionUpdateDetailsTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionConstructorTests.cs`, `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionUpdateDetailsTests.cs`

This task adds the persisted property, the shared default-computation helper, and updates the constructor/`UpdateDetails` signatures. `Reschedule` is intentionally NOT touched (architect Decision 2 — a move never changes all-day-ness).

- [ ] **Step 1: Write the failing tests for the new property and helper**

Add these two facts to the end of `MarketingActionConstructorTests.cs` (inside the `MarketingActionConstructorTests` class, before the final closing `}`):

```csharp
        [Fact]
        public void Ctor_SetsIsAllDayExactlyAsPassed()
        {
            var action = new MarketingAction(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: true,
                createdByUserId: "user-1",
                createdByUsername: "alice",
                utcNow: UtcNow);

            action.IsAllDay.Should().BeTrue();
        }

        [Theory]
        [InlineData("2026-09-18T00:00:00", "2026-09-19T00:00:00", true)]   // midnight to midnight
        [InlineData("2026-09-01T07:00:00", "2026-09-01T08:30:00", false)]  // timed same-day
        [InlineData("2026-09-18T00:00:00", null, false)]                  // midnight start, no end
        public void ComputeIsAllDay_MatchesTheMidnightToMidnightRule(
            string startText, string? endText, bool expected)
        {
            var start = DateTime.Parse(startText, null, System.Globalization.DateTimeStyles.RoundtripKind);
            DateTime? end = endText is null
                ? null
                : DateTime.Parse(endText, null, System.Globalization.DateTimeStyles.RoundtripKind);

            MarketingAction.ComputeIsAllDay(start, end).Should().Be(expected);
        }
```

Add this fact to the end of `MarketingActionUpdateDetailsTests.cs` (inside the `MarketingActionUpdateDetailsTests` class, before the final closing `}`):

```csharp
        [Fact]
        public void UpdateDetails_SetsIsAllDayExactlyAsPassed()
        {
            var action = NewAction();

            action.UpdateDetails(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: true,
                modifiedByUserId: "user-1",
                modifiedByUsername: "alice",
                utcNow: UtcNow);

            action.IsAllDay.Should().BeTrue();
        }
```

- [ ] **Step 2: Run the new tests to verify they fail to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionConstructorTests|FullyQualifiedName~MarketingActionUpdateDetailsTests"`
Expected: build error — `IsAllDay` does not exist on `MarketingAction`, no `isAllDay` parameter on the constructor/`UpdateDetails`, no `ComputeIsAllDay` method.

- [ ] **Step 3: Add the `IsAllDay` property, constructor/UpdateDetails parameter, and `ComputeIsAllDay` helper**

In `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`, add the property next to `EndDate` (after line `public DateTime? EndDate { get; private set; }`):

```csharp
        public DateTime? EndDate { get; private set; }

        public bool IsAllDay { get; private set; }
```

Change the constructor signature and body (add `isAllDay` after `endDate`, set it in the body after `EndDate = endDate;`):

```csharp
        public MarketingAction(
            string title,
            string? description,
            MarketingActionType actionType,
            DateTime startDate,
            DateTime? endDate,
            bool isAllDay,
            string createdByUserId,
            string? createdByUsername,
            DateTime utcNow)
        {
            Title = NormalizeTitle(title);
            Description = NormalizeDescription(description);
            ActionType = actionType;
            StartDate = startDate;
            EndDate = endDate;
            IsAllDay = isAllDay;
            CreatedAt = utcNow;
            ModifiedAt = utcNow;
            CreatedByUserId = createdByUserId;
            CreatedByUsername = createdByUsername ?? "Unknown User";
        }
```

Change `UpdateDetails`'s signature and body (add `isAllDay` after `endDate`, set it in the body after `EndDate = endDate;`):

```csharp
        public void UpdateDetails(
            string title,
            string? description,
            MarketingActionType actionType,
            DateTime startDate,
            DateTime? endDate,
            bool isAllDay,
            string modifiedByUserId,
            string? modifiedByUsername,
            DateTime utcNow)
        {
            Title = NormalizeTitle(title);
            Description = NormalizeDescription(description);
            ActionType = actionType;
            StartDate = startDate;
            EndDate = endDate;
            IsAllDay = isAllDay;
            ModifiedAt = utcNow;
            ModifiedByUserId = modifiedByUserId;
            ModifiedByUsername = modifiedByUsername ?? "Unknown User";
        }
```

Add the static helper directly above `Reschedule` (leave `Reschedule` itself completely unchanged — no new parameter, per architect Decision 2), and add an XML-doc comment on `Reschedule` noting the omission (per arch-review Risks mitigation):

```csharp
        /// <summary>
        /// The midnight-to-midnight rule this codebase has always used to guess
        /// all-day-ness from dates alone, when no more authoritative source (like
        /// Graph's own isAllDay flag) is available — i.e. for actions created or
        /// edited directly in Heblo rather than imported from Outlook.
        /// </summary>
        public static bool ComputeIsAllDay(DateTime startDate, DateTime? endDate) =>
            endDate is not null
            && startDate.TimeOfDay == TimeSpan.Zero
            && endDate.Value.TimeOfDay == TimeSpan.Zero;

        /// <summary>
        /// Rescheduling changes when an action happens, never what kind of time
        /// range it spans — IsAllDay is intentionally left untouched here.
        /// </summary>
        public void Reschedule(
```

- [ ] **Step 4: Fix the now-broken `MarketingActionTestBuilder` and existing constructor/UpdateDetails calls in the same two test files**

In `MarketingActionTestBuilder.cs`, add a backing field and fluent setter, then thread it through `Build()`:

```csharp
        private DateTime? _endDate;
        private bool _isAllDay;
```

```csharp
        public MarketingActionTestBuilder WithEndDate(DateTime? endDate) { _endDate = endDate; return this; }
        public MarketingActionTestBuilder WithIsAllDay(bool isAllDay) { _isAllDay = isAllDay; return this; }
```

In `Build()`, add `isAllDay: _isAllDay,` after `endDate: _endDate,` in both the constructor call and the `UpdateDetails` call:

```csharp
            var action = new MarketingAction(
                title: _title,
                description: _description,
                actionType: _actionType,
                startDate: _startDate,
                endDate: _endDate,
                isAllDay: _isAllDay,
                createdByUserId: _createdByUserId,
                createdByUsername: _createdByUsername,
                utcNow: _createdAt);

            if (_modifiedByUserId is not null || _modifiedByUsername is not null || _modifiedAt != _createdAt)
            {
                action.UpdateDetails(
                    title: _title,
                    description: _description,
                    actionType: _actionType,
                    startDate: _startDate,
                    endDate: _endDate,
                    isAllDay: _isAllDay,
                    modifiedByUserId: _modifiedByUserId ?? _createdByUserId,
                    modifiedByUsername: _modifiedByUsername,
                    utcNow: _modifiedAt);
            }
```

In `MarketingActionConstructorTests.cs`, every existing `new MarketingAction(...)` call (6 call sites: `Ctor_TrimsTitleWhitespace`, `Ctor_TrimsDescriptionWhenPresent`, `Ctor_PreservesNullDescription`, `Ctor_DefaultsCreatedByUsernameToUnknownUserWhenNull`, `Ctor_SetsCreatedAtAndModifiedAtToUtcNow`, `Ctor_AssignsRemainingScalarsExactlyAsPassed`) needs `isAllDay: false,` inserted after its `endDate:` line — e.g. the first one becomes:

```csharp
            var action = new MarketingAction(
                title: "  Hello  ",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                createdByUserId: "user-1",
                createdByUsername: "alice",
                utcNow: UtcNow);
```

Apply the identical `isAllDay: false,` insertion (right after `endDate:`) to the other five call sites in that file.

In `MarketingActionUpdateDetailsTests.cs`, every existing `action.UpdateDetails(...)` call (7 call sites: `UpdateDetails_TrimsLeadingAndTrailingWhitespaceFromTitle`, `UpdateDetails_ReplacesNullTitleWithEmptyString`, `UpdateDetails_PreservesNullDescription`, `UpdateDetails_TrimsDescriptionWhenPresent`, `UpdateDetails_DefaultsModifiedByUsernameToUnknownUserWhenNull`, `UpdateDetails_SetsModifiedAtToProvidedUtcNow`, `UpdateDetails_AssignsAllOtherScalarFieldsExactlyAsPassed`) needs `isAllDay: false,` inserted after its `endDate:` line, e.g. the first one becomes:

```csharp
            action.UpdateDetails(
                title: "  Spring Launch  ",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                modifiedByUserId: "user-1",
                modifiedByUsername: "alice",
                utcNow: UtcNow);
```

Apply the identical insertion to the other six call sites in that file.

- [ ] **Step 5: Run the domain tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionConstructorTests|FullyQualifiedName~MarketingActionUpdateDetailsTests"`
Expected: PASS (all tests, including the two new ones from Step 1).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionTestBuilder.cs backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionConstructorTests.cs backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionUpdateDetailsTests.cs
git commit -m "feat(marketing): add IsAllDay property to MarketingAction domain entity"
```

---

### task: persistence-migration-isalldy

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddIsAllDayToMarketingAction.cs` (generated by tooling)
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddIsAllDayToMarketingAction.Designer.cs` (generated by tooling)
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (regenerated by tooling)

This task requires `task: domain-isalldy-property` to be complete first (EF needs the `IsAllDay` property to exist to scaffold the migration).

- [ ] **Step 1: Map the new property in `MarketingActionConfiguration`**

In `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionConfiguration.cs`, add this property mapping directly after the existing `EndDate` mapping:

```csharp
            builder.Property(x => x.EndDate)
                .IsRequired(false)
                .AsUtcTimestamp();

            builder.Property(x => x.IsAllDay)
                .IsRequired();
```

- [ ] **Step 2: Generate the EF Core migration**

Run (from the repository root, adjust the relative project paths if your working directory differs):
```bash
cd backend && dotnet ef migrations add AddIsAllDayToMarketingAction \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.Api
```
Expected: three files created/updated — a new `<timestamp>_AddIsAllDayToMarketingAction.cs`, its `.Designer.cs`, and `ApplicationDbContextModelSnapshot.cs` updated to include `IsAllDay`. (If the startup project path differs from `src/Anela.Heblo.Api`, use `dotnet ef dbcontext list --project src/Anela.Heblo.Persistence` from within `backend/` to confirm the correct startup project first — do not guess.)

- [ ] **Step 3: Add the backfill SQL to the generated migration's `Up()` method**

Open the newly generated `<timestamp>_AddIsAllDayToMarketingAction.cs`. After the generated `migrationBuilder.AddColumn<bool>(...)` call inside `Up()`, add:

```csharp
            // Backfill: reproduce the legacy IsDateOnly guess as a one-time value so no
            // existing row's live export behavior changes at the moment this migration
            // runs. Rows with an active OutlookEventId self-correct on their next import
            // cycle (see arch-review.r1.md, Risks and Mitigations).
            migrationBuilder.Sql(@"
                UPDATE ""public"".""MarketingActions""
                SET ""IsAllDay"" = TRUE
                WHERE ""EndDate"" IS NOT NULL
                  AND date_trunc('day', ""StartDate"") = ""StartDate""
                  AND date_trunc('day', ""EndDate"") = ""EndDate"";
            ");
```

Leave the generated `Down()` method as EF scaffolded it (a plain `DropColumn`) — the backfill has no meaningful inverse and dropping the column is sufficient for rollback.

- [ ] **Step 4: Verify the migration builds and the model snapshot is consistent**

Run: `cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: build succeeds with no errors.

Run: `cd backend && dotnet ef migrations has-pending-model-changes --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.Api`
Expected: reports no pending model changes (confirms the snapshot matches the model after this migration).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionConfiguration.cs backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(marketing): add IsAllDay column via migration with backfill

NOTE: this migration is NOT auto-applied in deployment (manual migrations
per project convention) — run it against dev, staging, and production
after this change merges."
```

**Do not apply this migration to any shared database from within this task** — per `CLAUDE.md`, migrations in this repository are applied manually by whoever deploys, not by the implementing task.

---

### task: import-mapper-isalldy

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/OutlookEventImportMapperTests.cs`

Depends on `task: domain-isalldy-property`.

- [ ] **Step 1: Write the failing tests**

Add these to `OutlookEventImportMapperTests.cs` (inside the class, before the final closing `}`):

```csharp
    [Fact]
    public void BuildAction_SetsIsAllDayFromGraphsFlag_ForAllDayEvent()
    {
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: true);

        var action = Build(evt);

        action.IsAllDay.Should().BeTrue();
    }

    [Fact]
    public void BuildAction_SetsIsAllDayFromGraphsFlag_ForTimedMidnightToMidnightEvent()
    {
        // This is the exact failure scenario from the issue: a genuinely timed
        // 24-hour event whose dates happen to look like an all-day event must
        // NOT be recorded as all-day, because Graph says isAllDay: false.
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: false);

        var action = Build(evt);

        action.IsAllDay.Should().BeFalse();
        action.EndDate.Should().Be(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void HasChanges_ForToggledIsAllDayWithUnchangedDates_ReportsAChange()
    {
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            isAllDay: false);

        var existing = new MarketingAction(
            title: "Linda Odyssea",
            description: null,
            actionType: MarketingActionType.Event,
            startDate: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            isAllDay: true,
            createdByUserId: Actor.UserId,
            createdByUsername: Actor.Username,
            utcNow: UtcNow);

        var hasChanges = OutlookEventImportMapper.HasChanges(existing, evt, MarketingActionType.Event);

        hasChanges.Should().BeTrue();
    }
```

Update the existing `HasChanges_ForAlreadyImportedAllDayEventWithExclusiveEnd_ReportsAChange` test's `new MarketingAction(...)` call to add `isAllDay: false,` after its `endDate:` line (it represents "a row written by the old mapper," which never set `IsAllDay`, so `false` is correct — the point of that test is that dates alone already prove a change, and it should keep passing regardless of the new flag):

```csharp
        var existing = new MarketingAction(
            title: "Linda Odyssea",
            description: null,
            actionType: MarketingActionType.Event,
            startDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            endDate: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: false,
            createdByUserId: Actor.UserId,
            createdByUsername: Actor.Username,
            utcNow: UtcNow);
```

- [ ] **Step 2: Run the tests to verify the new ones fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutlookEventImportMapperTests"`
Expected: `BuildAction_SetsIsAllDayFromGraphsFlag_ForAllDayEvent` and `BuildAction_SetsIsAllDayFromGraphsFlag_ForTimedMidnightToMidnightEvent` FAIL (`action.IsAllDay` is `false` when `true` expected, or vice versa — the mapper doesn't set it yet). `HasChanges_ForToggledIsAllDayWithUnchangedDates_ReportsAChange` FAILS (`hasChanges` is `false`, expected `true` — `HasChanges` doesn't compare `IsAllDay` yet).

- [ ] **Step 3: Update `OutlookEventImportMapper` to set and compare `IsAllDay`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`, update `BuildAction`:

```csharp
        internal static MarketingAction BuildAction(
            OutlookEventDto evt,
            SyncActor actor,
            DateTime utcNow,
            MarketingActionType actionType)
        {
            var action = new MarketingAction(
                title: ParseTitle(evt.Subject),
                description: ParseDescription(evt.BodyText),
                actionType: actionType,
                startDate: evt.StartUtc,
                endDate: ParseEndDate(evt),
                isAllDay: evt.IsAllDay,
                createdByUserId: actor.UserId,
                createdByUsername: actor.Username,
                utcNow: utcNow);

            action.MarkOutlookSynced(evt.Id, utcNow);

            return action;
        }
```

Update `HasChanges`:

```csharp
        internal static bool HasChanges(MarketingAction existing, OutlookEventDto evt, MarketingActionType actionType)
        {
            // Compare against the values UpdateDetails / the constructor would
            // persist — both trim title and description. Without this, re-importing
            // a whitespace-bearing event would always report Updated.
            var normalizedTitle = ParseTitle(evt.Subject).Trim();
            var normalizedDescription = ParseDescription(evt.BodyText)?.Trim();

            return existing.Title != normalizedTitle
                || existing.Description != normalizedDescription
                || existing.StartDate != evt.StartUtc
                || existing.EndDate != ParseEndDate(evt)
                || existing.IsAllDay != evt.IsAllDay
                || existing.ActionType != actionType;
        }
```

Update `ApplyChanges`:

```csharp
        internal static void ApplyChanges(
            MarketingAction existing,
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncActor actor,
            DateTime utcNow)
        {
            existing.UpdateDetails(
                title: ParseTitle(evt.Subject),
                description: ParseDescription(evt.BodyText),
                actionType: actionType,
                startDate: evt.StartUtc,
                endDate: ParseEndDate(evt),
                isAllDay: evt.IsAllDay,
                modifiedByUserId: actor.UserId,
                modifiedByUsername: actor.Username,
                utcNow: utcNow);

            existing.MarkOutlookSynced(evt.Id, utcNow);
        }
```

`ParseEndDate` is unchanged — it already consumes `evt.IsAllDay` correctly for the exclusive→inclusive conversion.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutlookEventImportMapperTests"`
Expected: PASS (all tests, including the three new ones and the updated one from Step 1).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs backend/test/Anela.Heblo.Tests/Features/Marketing/OutlookEventImportMapperTests.cs
git commit -m "fix(marketing): set MarketingAction.IsAllDay from Graph's own flag on import"
```

---

### task: export-service-isalldy

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs`

Depends on `task: domain-isalldy-property`. This task also delivers spec FR-5 (the missing import→export round-trip regression test) and is the direct fix for the issue's failure scenario.

- [ ] **Step 1: Update the test file's local `BuildAction` helper to accept and forward `isAllDay`**

In `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs`, change the private `BuildAction` helper:

```csharp
        private static MarketingAction BuildAction(
            string? outlookEventId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool isAllDay = false)
        {
            var resolvedEndDate = endDate == DateTime.MinValue ? (DateTime?)null : (endDate ?? DefaultEndDate);

            return new MarketingActionTestBuilder()
                .WithId(42)
                .WithTitle("Spring Launch")
                .WithDescription("Big spring launch event")
                .WithActionType(MarketingActionType.Newsletter)
                .WithStartDate(startDate ?? DefaultStartDate)
                .WithEndDate(resolvedEndDate)
                .WithIsAllDay(isAllDay)
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .WithOutlookEventId(outlookEventId)
                .Build();
        }
```

- [ ] **Step 2: Update the three date-only/timed export tests to set `isAllDay` explicitly, and write the new import→export round-trip regression test**

These three existing tests currently rely on `BuildEventBody`'s old date-shape guess; once it reads `action.IsAllDay` directly, they must pass the flag explicitly to keep testing the same thing. Update each call:

```csharp
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
                isAllDay: true);
```
(in `CreateEventAsync_ForDateOnlyAction_SendsGraphsExclusiveEnd`)

```csharp
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                isAllDay: true);
```
(in `CreateEventAsync_ForMultiDayDateOnlyAction_SendsGraphsExclusiveEnd`)

```csharp
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
                isAllDay: false);
```
(in `CreateEventAsync_ForTimedAction_SendsItsEndUnchanged` — `isAllDay: false` here is a no-op relative to the parameter default, but pass it explicitly so the test reads as being about all-day-ness, not relying on a default the reader has to go look up.)

Now add the new regression test (spec FR-5) directly after the existing `CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates` theory test:

```csharp
        [Fact]
        public async Task CreateEventAsync_ForATimedMidnightToMidnightAction_DoesNotExportAsAllDay()
        {
            // Regression test for the issue: a genuinely timed 24-hour action whose
            // dates happen to look like an all-day event (both at midnight) must NOT
            // export as isAllDay: true just because of its dates — IsAllDay is now an
            // explicit, persisted flag, not re-derived from the dates on every export.
            var responseJson = JsonSerializer.Serialize(new { id = "evt-timed-midnight" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
                isAllDay: false);

            await service.CreateEventAsync(action, CancellationToken.None);

            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("isAllDay").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-19T00:00:00");
        }

        [Fact]
        public async Task ImportedTimedMidnightToMidnightEvent_ThenReExported_StaysTimed()
        {
            // The exact failure scenario from the issue, walked end-to-end: import a
            // genuinely timed Graph event whose dates look like an all-day event, then
            // re-export the resulting MarketingAction and confirm it is still timed —
            // not silently promoted to a 3-day all-day event on the shared calendar.
            var importedEvent = new OutlookEventDto
            {
                Id = "evt-timed-midnight",
                Subject = "24-hour timed meeting",
                Start = new GraphEventDateTime
                {
                    DateTimeString = "2026-09-18T00:00:00.0000000",
                    TimeZone = "UTC"
                },
                End = new GraphEventDateTime
                {
                    DateTimeString = "2026-09-19T00:00:00.0000000",
                    TimeZone = "UTC"
                },
                IsAllDay = false,
                Categories = Array.Empty<string>(),
            };

            var imported = OutlookEventImportMapper.BuildAction(
                importedEvent,
                new SyncActor("user-1", "Import User"),
                DateTime.UtcNow,
                MarketingActionType.Newsletter);

            imported.IsAllDay.Should().BeFalse();
            imported.EndDate.Should().Be(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));

            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new { id = "evt-timed-midnight" }));
            var service = CreateService(handler);

            await service.CreateEventAsync(imported, CancellationToken.None);

            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("isAllDay").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-19T00:00:00");
        }

        [Fact]
        public async Task ImportedGenuineAllDayEvent_ThenReExported_StaysAllDay()
        {
            // Companion to the above: a genuine all-day import must still round-trip
            // as all-day, confirming the fix doesn't overcorrect the other direction.
            var importedEvent = new OutlookEventDto
            {
                Id = "evt-genuine-allday",
                Subject = "Company holiday",
                Start = new GraphEventDateTime
                {
                    DateTimeString = "2026-09-18T00:00:00.0000000",
                    TimeZone = "UTC"
                },
                End = new GraphEventDateTime
                {
                    DateTimeString = "2026-09-19T00:00:00.0000000",
                    TimeZone = "UTC"
                },
                IsAllDay = true,
                Categories = Array.Empty<string>(),
            };

            var imported = OutlookEventImportMapper.BuildAction(
                importedEvent,
                new SyncActor("user-1", "Import User"),
                DateTime.UtcNow,
                MarketingActionType.Newsletter);

            imported.IsAllDay.Should().BeTrue();
            imported.EndDate.Should().Be(new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc));

            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new { id = "evt-genuine-allday" }));
            var service = CreateService(handler);

            await service.CreateEventAsync(imported, CancellationToken.None);

            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("isAllDay").GetBoolean().Should().BeTrue();
            body.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-19T00:00:00");
        }
```

- [ ] **Step 3: Run the tests to verify the new/updated ones fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutlookCalendarSyncServiceTests"`
Expected: the three updated date-only/timed tests still PASS (the guess and the explicit flag agree for those inputs today). `CreateEventAsync_ForATimedMidnightToMidnightAction_DoesNotExportAsAllDay`, `ImportedTimedMidnightToMidnightEvent_ThenReExported_StaysTimed` FAIL — `isAllDay` comes back `true` (the old guess fires because the export still calls `IsDateOnly`).

- [ ] **Step 4: Fix `OutlookCalendarSyncService` — read `action.IsAllDay`, delete the guess**

In `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs`, change `BuildEventBody`:

```csharp
        private string BuildEventBody(MarketingAction action)
        {
            var isAllDay = action.IsAllDay;
            var endDate = BuildGraphEnd(action, isAllDay);
```

Delete the entire `IsDateOnly` method (it has no remaining callers):

```csharp
        /// <summary>
        /// A date-only action (midnight to midnight) is Heblo's shape for an all-day event.
        /// The same answer drives both the exclusive end and the isAllDay flag sent to Graph —
        /// they must agree, or the event round-trips back through the import as a timed one.
        /// </summary>
        private static bool IsDateOnly(MarketingAction action) =>
            action.EndDate is not null
            && action.StartDate.TimeOfDay == TimeSpan.Zero
            && action.EndDate.Value.TimeOfDay == TimeSpan.Zero;
```
— remove this block entirely. `BuildGraphEnd` is unchanged (still takes `MarketingAction action, bool isAllDay` and behaves identically; only the value passed in as `isAllDay` now comes from the entity instead of a guess).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutlookCalendarSyncServiceTests"`
Expected: PASS (all tests, including the three new ones from Step 2).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs
git commit -m "fix(marketing): read IsAllDay directly on export instead of guessing from dates

Fixes the round-trip data corruption in #4238: a timed midnight-to-midnight
Outlook event no longer gets promoted to a multi-day all-day event after
one edit round-trip through Heblo."
```

---

### task: manual-handlers-isalldy

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs`

Depends on `task: domain-isalldy-property`. This closes the last two production call sites of the (now three-parameter) constructor, so `dotnet build` is fully green across the solution.

- [ ] **Step 1: Update `CreateMarketingActionHandler` to compute and pass `IsAllDay`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`, change the `new MarketingAction(...)` call:

```csharp
            var action = new MarketingAction(
                title: request.Title,
                description: request.Description,
                actionType: request.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                isAllDay: MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate),
                createdByUserId: currentUser.Id,
                createdByUsername: currentUser.Name,
                utcNow: now);
```

- [ ] **Step 2: Update `UpdateMarketingActionHandler` to compute and pass `IsAllDay`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`, change the `action.UpdateDetails(...)` call:

```csharp
            action.UpdateDetails(
                title: request.Title,
                description: request.Description,
                actionType: request.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                isAllDay: MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate),
                modifiedByUserId: currentUser.Id,
                modifiedByUsername: currentUser.Name,
                utcNow: now);
```

- [ ] **Step 3: Fix the remaining test-only constructor call sites**

In `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs`, update the seeding call:

```csharp
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: DateTime.UtcNow,
            endDate: null,
            isAllDay: false,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);
```

In `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs`, update the seeding call:

```csharp
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: startDate,
            endDate: endDate,
            isAllDay: false,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);
```

- [ ] **Step 4: Build the whole solution to confirm no remaining call site was missed**

Run: `cd backend && dotnet build Anela.Heblo.sln`
Expected: build succeeds with 0 errors. If any error remains about a missing `isAllDay` argument, it means a constructor/`UpdateDetails` call site exists that this plan did not enumerate — locate it with `grep -rn "new MarketingAction(\|\.UpdateDetails(" backend --include=*.cs` from the repo root, and add the missing `isAllDay:` argument following the same pattern as the sites above (Graph-originated calls pass `evt.IsAllDay`; every other call passes `MarketingAction.ComputeIsAllDay(startDate, endDate)` or, for a pure test fixture with no request context, an explicit literal `false`/`true` matching the scenario under test) before proceeding.

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test Anela.Heblo.sln`
Expected: PASS — 0 failures. This exercises every test file this plan touched (`domain-isalldy-property`, `import-mapper-isalldy`, `export-service-isalldy`, and this task) plus every other existing test in the solution, confirming nothing else broke.

- [ ] **Step 6: Run `dotnet format` to match project formatting conventions**

Run: `cd backend && dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting changes needed. If changes are reported, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-run the verify command to confirm.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs
git commit -m "fix(marketing): derive IsAllDay for manually created/edited actions

Manually-created actions have no UI-level all-day toggle (out of scope for
#4238 per spec NFR-2), so CreateMarketingActionHandler and
UpdateMarketingActionHandler derive IsAllDay from the same midnight-to-
midnight rule the old export-side guess used, preserving today's observable
export behavior for actions that don't originate from Outlook import."
```

---

## Follow-ups (not implemented by this plan — carried forward from spec Open Questions)

- **`docs/integrations/microsoft-graph-calendar.md` does not exist** in this repository (verified against the full working tree and git history during the analyzing phase), despite the issue referencing it for context. This plan does not create it — creating/backfilling that doc is a product/documentation decision outside this bug-fix's scope (spec Open Question 1). Recommend a separate follow-up issue if the doc is wanted.
- **Exposing `IsAllDay` on `MarketingActionDto`** for read-side transparency (spec Open Question 3) is not implemented here — no consumer needs it yet, and adding it later is a small, additive, non-breaking change with no migration impact.

## Self-Review (performed against spec.r1.md, arch-review.r1.md, design.r1.md)

**Spec coverage:**
- FR-1 (persist `IsAllDay`) → `task: domain-isalldy-property`.
- FR-2 (set from Graph on import, including `HasChanges`) → `task: import-mapper-isalldy`.
- FR-3 (read on export, delete `IsDateOnly`) → `task: export-service-isalldy`.
- FR-4 (manual-path default, shared helper) → `ComputeIsAllDay` in `task: domain-isalldy-property`, consumed in `task: manual-handlers-isalldy`.
- FR-5 (import→export round-trip regression test) → `task: export-service-isalldy`, Step 2 (two new tests: timed-midnight-to-midnight and genuine-all-day).
- FR-6 / NFR-1 (migration + backfill) → `task: persistence-migration-isalldy`.
- NFR-2 (no DTO/API/UI change) → honored throughout; no task touches `MarketingActionDto`, `CreateMarketingActionRequest`, `UpdateMarketingActionRequest`, or any frontend file.
- Architect Decision 2 (`Reschedule` untouched) → honored in `task: domain-isalldy-property`, Step 3 (XML-doc comment added, no parameter added); `MoveMarketingActionHandler` is not listed in any task's Files section because it needs no change.

**Placeholder scan:** no "TBD"/"similar to Task N"/unshown code remains — every step above shows the exact code to write or the exact command to run.

**Type consistency:** `IsAllDay` (bool), `ComputeIsAllDay(DateTime, DateTime?)` (static on `MarketingAction`), and the constructor/`UpdateDetails` parameter name `isAllDay` are used identically across all five tasks.
