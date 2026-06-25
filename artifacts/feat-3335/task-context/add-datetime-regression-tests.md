### task: add-datetime-regression-tests

**Goal:** Add unit tests that exercise `Kind=Unspecified` inputs — the exact crash path — in all four sync service `Map()` methods and verify `GetInitialBackfillDateTime()` returns correct UTC midnight, so regressions are caught before they reach Npgsql.

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs`

**Background:**

The existing `MakeXxxDto` helpers all pass `DateTimeKind.Utc` inputs, so the tests pass today whether or not the `Map()` methods use `ToUniversalTime()` or `ConvertTimeToUtc`. The crash only occurs with `Kind=Unspecified` inputs (what the SDK actually produces). Each new test passes `Kind=Unspecified` directly to the `Map()` method (now `internal static` after the previous task) and asserts the output is `Kind=Utc` and equals the value that `TimeZoneInfo.ConvertTimeToUtc` would produce.

`InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")` is already declared in `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` — no new assembly attribute is needed.

The test uses `TimeZoneInfo.ConvertTimeToUtc(input, TimeZoneInfo.Local)` as the expected value, which means the test is environment-sensitive (it will produce a different absolute UTC value depending on what `TimeZoneInfo.Local` is on the test runner). This is intentional and correct: the test verifies that the `Map()` method applies the same conversion as the reference pattern, not a hardcoded UTC offset. The Kind=Utc assertion is the primary regression guard; the value equality assertion confirms the correct offset was applied.

**Steps:**

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatusAndKeepsWatermarkUnchanged` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // Map() must call ConvertTimeToUtc, not ToUniversalTime().
        // Kind=Unspecified + ToUniversalTime() works only if container TZ=Europe/Prague;
        // ConvertTimeToUtc(value, TimeZoneInfo.Local) is explicit and testable.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new LedgerItemFlexiDto
        {
            Id = 99,
            AccountingDate = unspecified,
            LastUpdate = unspecified,
            AmountLocal = 100.0,
            ParSymbol = "CODE99",
            DebitAccountShowAs = "501000",
            CreditAccountShowAs = "221000",
            CurrencyRef = "code:CZK",
            Description = "Regression test entry",
        };

        var entry = LedgerSyncService.Map(dto);

        Assert.NotNull(entry.LastModified);
        Assert.Equal(DateTimeKind.Utc, entry.LastModified!.Value.Kind);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, entry.LastModified.Value);
    }
```

- [ ] Run just this new test to confirm it compiles and passes:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~LedgerSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatus` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new ContactFlexiDto
        {
            Id = 99L,
            Code = "TEST99",
            Name = "Test Contact",
            CIN = "CIN00000099",
            VATIN = "CZ00000099",
            LastUpdate = unspecified,
        };

        var contact = ContactSyncService.Map(dto);

        Assert.NotNull(contact.LastModified);
        Assert.Equal(DateTimeKind.Utc, contact.LastModified!.Value.Kind);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, contact.LastModified.Value);
    }
```

- [ ] Run just this new test:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~ContactSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatus` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // DepartmentFlexiDto.LastUpdate is non-nullable DateTime; default guard applies.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new DepartmentFlexiDto
        {
            Id = 99,
            Code = "TEST",
            Name = "Test Department",
            LastUpdate = unspecified,
        };

        var department = DepartmentSyncService.Map(dto);

        Assert.NotNull(department.LastModified);
        // Entity.LastModified is DateTimeOffset? for Department; UtcDateTime gives Kind=Utc
        Assert.Equal(TimeSpan.Zero, department.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, department.LastModified.Value.UtcDateTime);
    }
```

  Note: `DepartmentSyncService.Map()` returns a `Department` whose `LastModified` is `DateTimeOffset?` (the cast in the `== default` branch produces `DateTimeOffset`). The test asserts `Offset == TimeSpan.Zero` (UTC) rather than `Kind=Utc`, because `DateTimeOffset` does not have a `Kind` property.

- [ ] Run just this new test:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~DepartmentSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatus` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // AccountingTemplateFlexiDto.LastUpdate is non-nullable DateTime; default guard applies.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new AccountingTemplateFlexiDto
        {
            Id = 99,
            Code = "TEST",
            Name = "Test Template",
            Description = "Regression test",
            LastUpdate = unspecified,
        };

        var template = AccountingTemplateSyncService.Map(dto);

        Assert.NotNull(template.LastModified);
        // Entity.LastModified is DateTimeOffset? for AccountingTemplate; Offset=0 confirms UTC.
        Assert.Equal(TimeSpan.Zero, template.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, template.LastModified.Value.UtcDateTime);
    }
```

- [ ] Run just this new test:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~AccountingTemplateSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Run the full test suite for the adapter to confirm everything is green:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests
```

- [ ] Run the full backend build and format:

```
cd backend && dotnet build && dotnet format
```

- [ ] Commit:

```
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs
git commit -m "test: add Kind=Unspecified regression tests for all four sync service Map() methods (#3335)"
```

---

## Notes for the implementer

**Type differences between services:**

| Service | `dto.LastUpdate` type | Entity `LastModified` type | Guard pattern |
|---|---|---|---|
| `LedgerSyncService` | `DateTime?` | `DateTime?` | `.HasValue` + `ConvertTimeToUtc(dto.LastUpdate.Value, ...)` |
| `ContactSyncService` | `DateTime?` | `DateTime?` | `.HasValue` + `ConvertTimeToUtc(dto.LastUpdate.Value, ...)` |
| `DepartmentSyncService` | `DateTime` (non-nullable) | `DateTimeOffset?` | `== default ? null : (DateTimeOffset?)ConvertTimeToUtc(dto.LastUpdate, ...)` |
| `AccountingTemplateSyncService` | `DateTime` (non-nullable) | `DateTimeOffset?` | `== default ? null : (DateTimeOffset?)ConvertTimeToUtc(dto.LastUpdate, ...)` |

`Department.LastModified` and `AccountingTemplate.LastModified` are `DateTimeOffset?` even though the source comes from a `DateTime` conversion. The cast `(DateTimeOffset?)someDateTime` creates a `DateTimeOffset` with `Offset=TimeSpan.Zero` when `Kind=Utc`, which is correct for Npgsql `timestamptz` writes.

**Do not** change `LedgerSyncService` or `ContactSyncService` to return `DateTimeOffset?` — their entity properties are `DateTime?` and changing them would require an EF Core mapping change and a migration (out of scope).

**`InternalsVisibleTo` is already in place** at `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` — no assembly attribute changes needed in this task.

**Verifying the fix is deployed** (out of scope for code tasks, but critical): after these commits merge to `main`, approve the `deploy-production` environment gate in the `ci-main-branch.yml` Actions tab. Verify the Docker image tag in Azure Portal matches the new build. The nightly job at 02:00 UTC should complete without `ArgumentException` on the following morning.
