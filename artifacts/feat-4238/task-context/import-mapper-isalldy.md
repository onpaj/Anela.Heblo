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
