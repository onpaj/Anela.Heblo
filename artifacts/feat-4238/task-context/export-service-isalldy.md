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
