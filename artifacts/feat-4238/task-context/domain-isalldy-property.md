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
