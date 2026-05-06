# Design: HTTP 409 Spike Remediation on `PUT /api/transport-boxes/{id}/state`

## Component Design

### Overview

All changes amend existing files. The only net-new artefacts are: one interface (`IDbConstraintClassifier`), one implementing class (`NpgsqlConstraintClassifier`), one EF Core migration, and one test file (`TransportBoxRaceConditionTests`). No new modules or directories are required.

---

### `TransportBoxConfiguration` — Persistence layer

**File:** `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxConfiguration.cs`

Adds a constant for the index name and the filtered unique index in `Configure`:

```csharp
internal const string ActiveCodeIndexName = "IX_TransportBoxes_Code_Active";

// In Configure():
builder.HasIndex(x => x.Code)
    .IsUnique()
    .HasDatabaseName(ActiveCodeIndexName)
    .HasFilter(
        """
        "Code" IS NOT NULL
        AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
        """);
```

The constant is `internal` so `NpgsqlConstraintClassifier` (same assembly) can reference it without leaking into the Application layer.

**Why string literals in the filter:** `State` is stored as `text` via `.HasConversion<string>()` (line 16 of the current config). Integer values would never match any row.

---

### `IDbConstraintClassifier` — Application layer (new interface)

**File:** `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/IDbConstraintClassifier.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public interface IDbConstraintClassifier
{
    bool IsDuplicateActiveBoxCodeViolation(DbUpdateException ex);
}
```

This interface keeps the Application layer free of a direct `Npgsql` reference. `DbUpdateException` is from `Microsoft.EntityFrameworkCore`, already available in Application.

---

### `NpgsqlConstraintClassifier` — Persistence layer (new class)

**File:** `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/NpgsqlConstraintClassifier.cs`

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Persistence.Logistics.TransportBoxes;

public sealed class NpgsqlConstraintClassifier : IDbConstraintClassifier
{
    public bool IsDuplicateActiveBoxCodeViolation(DbUpdateException ex) =>
        ex.GetBaseException() is PostgresException pg
        && pg.SqlState == "23505"
        && pg.ConstraintName == TransportBoxConfiguration.ActiveCodeIndexName;
}
```

Registered in DI as `IDbConstraintClassifier → NpgsqlConstraintClassifier` (singleton; stateless).

`SqlState == "23505"` is the PostgreSQL unique-violation class. Combined with `ConstraintName`, this ensures unrelated unique constraints on `TransportBoxes` do not take the 409 path.

---

### `TransportBoxRepository.IsBoxCodeActiveAsync` — Persistence layer

**File:** `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs`

Add `TransportBoxState.Quarantine` to the `activeStates` array (currently lines 98–105):

```csharp
var activeStates = new[]
{
    TransportBoxState.New,
    TransportBoxState.Opened,
    TransportBoxState.InTransit,
    TransportBoxState.Received,
    TransportBoxState.Reserve,
    TransportBoxState.Quarantine,   // FR-3: box in Quarantine still owns its Code
};
```

---

### `ChangeTransportBoxStateHandler` — Application layer

**File:** `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

#### Constructor — add `IDbConstraintClassifier`

```csharp
private readonly IDbConstraintClassifier _constraintClassifier;

public ChangeTransportBoxStateHandler(
    ITransportBoxRepository repository,
    IMediator mediator,
    ILogger<ChangeTransportBoxStateHandler> logger,
    ICurrentUserService currentUserService,
    IStockUpProcessingService stockUpProcessingService,
    TimeProvider timeProvider,
    IDbConstraintClassifier constraintClassifier)
{
    // ... existing assignments ...
    _constraintClassifier = constraintClassifier;
}
```

#### `Handle` — new `catch` arm (between `ValidationException` and `Exception`)

Insert after the existing `catch (ValidationException)` block and before `catch (Exception)` (currently at line 149):

```csharp
catch (DbUpdateException ex) when (_constraintClassifier.IsDuplicateActiveBoxCodeViolation(ex))
{
    var normalizedCode = request.BoxCode?.ToUpper() ?? string.Empty;
    _logger.LogWarning(
        "Duplicate active box code detected via DB constraint for box {BoxId}. " +
        "RequestedBoxCode: {RequestedBoxCode}, CurrentState: {CurrentState}, " +
        "RequestedNewState: {RequestedNewState}, ConflictReason: {ConflictReason}, Source: {Source}",
        request.BoxId, normalizedCode, box?.State.ToString() ?? "unknown",
        request.NewState, "DuplicateActiveBoxCode", "DbConstraint");

    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound,
        Params = new Dictionary<string, string> { { "code", normalizedCode } }
    };
}
```

`box` is in scope here (loaded before the callback invocation). Any other `DbUpdateException` — FK violation, check constraint, transient infra fault — falls through to the generic `catch (Exception)` arm unchanged.

#### `Handle` — enrich existing `catch (Exception)` arm

Replace the current log call (line 151) to include request context:

```csharp
_logger.LogError(ex,
    "Error changing state for transport box {BoxId}. " +
    "BoxCode: {BoxCode}, Location: {Location}, " +
    "RequestedNewState: {RequestedNewState}, CurrentBoxState: {CurrentBoxState}",
    request.BoxId, request.BoxCode, request.Location,
    request.NewState, box?.State.ToString() ?? "unknown");
```

#### `HandleNewToOpened` — structured log on fast-path duplicate

Replace the early-return block after `isCodeActive` check (currently lines 178–184) with:

```csharp
if (isCodeActive)
{
    var conflictingBox = await _repository.GetByCodeAsync(normalizedCode);

    _logger.LogWarning(
        "Duplicate active box code detected for box {BoxId}. " +
        "RequestedBoxCode: {RequestedBoxCode}, CurrentState: {CurrentState}, " +
        "RequestedNewState: {RequestedNewState}, ConflictReason: {ConflictReason}, Source: {Source}, " +
        "ConflictingBoxId: {ConflictingBoxId}, ConflictingBoxState: {ConflictingBoxState}",
        box.Id, normalizedCode, box.State, request.NewState,
        "DuplicateActiveBoxCode", "FastPathCheck",
        conflictingBox?.Id, conflictingBox?.State.ToString() ?? "unknown");

    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound,
        Params = new Dictionary<string, string> { { "code", normalizedCode } }
    };
}
```

`GetByCodeAsync` is called only on the duplicate unhappy path; the happy path is unchanged (NFR-1).

---

### EF Core Migration — Persistence layer (new file)

**File:** `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddUniqueIndexOnTransportBoxCodeActive.cs`

Generated via `dotnet ef migrations add AddUniqueIndexOnTransportBoxCodeActive`, then edited to prepend the fail-fast guard in `Up`:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Pre-flight: abort if any duplicate active codes already exist.
    // Operator must resolve them (see runbook) before applying this migration.
    migrationBuilder.Sql("""
        DO $$
        DECLARE dup_count INTEGER;
        BEGIN
            SELECT COUNT(*) INTO dup_count
            FROM (
                SELECT "Code"
                FROM public."TransportBoxes"
                WHERE "Code" IS NOT NULL
                  AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
                GROUP BY "Code"
                HAVING COUNT(*) > 1
            ) sub;

            IF dup_count > 0 THEN
                RAISE EXCEPTION
                    'Migration aborted: % active TransportBox duplicate code(s) found. '
                    'Resolve them before applying this migration.',
                    dup_count;
            END IF;
        END $$;
        """);

    migrationBuilder.CreateIndex(
        name: "IX_TransportBoxes_Code_Active",
        table: "TransportBoxes",
        schema: "public",
        column: "Code",
        unique: true,
        filter: """
            "Code" IS NOT NULL
            AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
            """);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "IX_TransportBoxes_Code_Active",
        table: "TransportBoxes",
        schema: "public");
}
```

---

### `TransportBoxUniquenessTests` — Test (amended)

**File:** `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs`

Add one test covering the Quarantine gap (FR-3). Runs against InMemory EF — sufficient because it validates the application-layer fast-path check (`IsBoxCodeActiveAsync`), not the DB constraint:

```csharp
[Fact]
public async Task OpenBox_WithCodeHeldByQuarantinedBox_ReturnsDuplicateError()
{
    // Arrange: open box A with B001 → transition to Quarantine
    // Act: attempt to open box B with B001
    // Assert: response.ErrorCode == ErrorCodes.TransportBoxDuplicateActiveBoxFound
}
```

Existing tests are unaffected — none transit through `Quarantine`.

---

### `TransportBoxRaceConditionTests` — Test (new file)

**File:** `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxRaceConditionTests.cs`

Uses `Testcontainers.PostgreSql` (already referenced at v3.6.0 in the test project) to spin up a real PostgreSQL instance, apply migrations, then fire two concurrent `New → Opened` requests with the same `BoxCode`:

```csharp
[Fact]
public async Task ConcurrentOpenWithSameCode_ExactlyOneSucceeds()
{
    // Arrange: real PostgreSQL via Testcontainer; migrations applied;
    //          two boxes in New state, same BoxCode = "B001"
    // Act: run two concurrent ChangeTransportBoxStateRequest { NewState=Opened, BoxCode="B001" }
    // Assert: exactly one response has Success=true;
    //         the other has ErrorCode=TransportBoxDuplicateActiveBoxFound
}
```

InMemory EF does not enforce filtered unique indexes and cannot exercise this scenario.

---

## Data Schemas

### Filtered Unique Index

```sql
CREATE UNIQUE INDEX "IX_TransportBoxes_Code_Active"
    ON public."TransportBoxes" ("Code")
    WHERE "Code" IS NOT NULL
      AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine');
```

The filter uses **string literals** because `State` is stored as `text` via `HasConversion<string>()`. Integer values would never match any row.

---

### Duplicate-detection Pre-flight Queries (Runbook)

Cross-state duplicates (same code held by multiple active boxes):

```sql
SELECT "Code",
       COUNT(*)                                          AS active_count,
       ARRAY_AGG("Id"::text || ':' || "State")           AS boxes
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL
  AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
GROUP BY "Code"
HAVING COUNT(*) > 1;
```

Within-state duplicates (same code and same state):

```sql
SELECT "Code", "State", COUNT(*) AS dup_count
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL
  AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
GROUP BY "Code", "State"
HAVING COUNT(*) > 1;
```

Both queries must return zero rows before applying the migration.

---

### Structured Log Contract (App Insights)

These field names are stable. Renaming them is a breaking change for any saved KQL queries.

| Property | Type | Emitted when |
|---|---|---|
| `BoxId` | `int` | All conflict entries |
| `RequestedBoxCode` | `string` (upper-cased) | All conflict entries |
| `CurrentState` | `string` | All conflict entries |
| `RequestedNewState` | `string` | All conflict entries |
| `ConflictReason` | `"DuplicateActiveBoxCode"` | All conflict entries |
| `Source` | `"FastPathCheck"` \| `"DbConstraint"` | Indicates which detection path fired |
| `ConflictingBoxId` | `int?` | `Source = "FastPathCheck"` only |
| `ConflictingBoxState` | `string?` | `Source = "FastPathCheck"` only |

KQL filter — 24-hour window:

```kusto
traces
| where timestamp > ago(24h)
| where customDimensions["ConflictReason"] == "DuplicateActiveBoxCode"
| project timestamp,
          customDimensions["BoxId"],
          customDimensions["Source"],
          customDimensions["RequestedBoxCode"],
          customDimensions["ConflictingBoxId"]
| order by timestamp desc
```

---

### API Wire Contract (unchanged)

**Request** — `PUT /api/transport-boxes/{id}/state`

```json
{
  "newState": "Opened",
  "boxCode": "B001",
  "location": null,
  "description": null
}
```

**Response — 409 Conflict** (identical from both fast-path and DB-constraint paths)

```json
{
  "success": false,
  "errorCode": 1405,
  "params": { "code": "B001" }
}
```

No DTO fields are added or removed. The TypeScript OpenAPI client does not need regeneration.
