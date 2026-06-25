### task: fix-sync-service-map-methods

**Goal:** Fix all four sync service `Map()` methods to use `TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)` instead of `.ToUniversalTime()`, eliminating the ambient-timezone dependency and matching the canonical pattern in `UnspecifiedDateTimeConverter.cs`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs`

**Background:**

The FlexiBee SDK returns `DateTime` with `Kind=Unspecified` representing Prague local time. All four services call `.ToUniversalTime()` which works only if the container's `TZ` is `Europe/Prague`, but this is an implicit ambient dependency. `TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)` makes the intent explicit (matches `UnspecifiedDateTimeConverter.cs` and `DateTimeLocalKindConverter.cs` already in the codebase).

Two structural patterns exist across the four services:
- **`LedgerSyncService` and `ContactSyncService`**: `dto.LastUpdate` is `DateTime?` (nullable). Current: `dto.LastUpdate?.ToUniversalTime()`. Fix: use the `HasValue` guard.
- **`DepartmentSyncService` and `AccountingTemplateSyncService`**: `dto.LastUpdate` is `DateTime` (non-nullable). Current: `dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime()`. Fix: replace only the conversion call, keep the `== default` guard.

Also change each `Map()` from `private static` to `internal static` so regression tests in the next task can call them directly without going through `SyncAsync`. `InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")` is already configured in both `AssemblyInfo.cs` and the `.csproj`.

**Steps:**

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs`.

  Change line 140 from `private static LedgerEntry Map(...)` to `internal static LedgerEntry Map(...)`.

  Change line 151 from:
  ```csharp
          LastModified = dto.LastUpdate?.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          LastModified = dto.LastUpdate.HasValue
              ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)
              : (DateTime?)null,
  ```

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs`.

  Change line 137 from `private static Contact Map(...)` to `internal static Contact Map(...)`.

  Change line 144 from:
  ```csharp
          LastModified = dto.LastUpdate?.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          LastModified = dto.LastUpdate.HasValue
              ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)
              : (DateTime?)null,
  ```

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`.

  Change line 111 from `private static Department Map(...)` to `internal static Department Map(...)`.

  Change line 116 from:
  ```csharp
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          // dto.LastUpdate is non-nullable DateTime; == default guard is correct here (no nullable operator).
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate, TimeZoneInfo.Local),
  ```

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs`.

  Change line 112 from `private static AccountingTemplate Map(...)` to `internal static AccountingTemplate Map(...)`.

  Change line 118 from:
  ```csharp
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          // dto.LastUpdate is non-nullable DateTime; == default guard is correct here (no nullable operator).
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate, TimeZoneInfo.Local),
  ```

- [ ] Build to confirm all four compile cleanly:

```
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

- [ ] Run the full test suite for the adapter to confirm no regressions:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests
```

  All previously passing tests should remain green.

- [ ] Run `dotnet format` on the project:

```
cd backend && dotnet format src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

- [ ] Commit:

```
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs
git commit -m "fix: use ConvertTimeToUtc in all four sync service Map() methods (#3335)"
```

---
