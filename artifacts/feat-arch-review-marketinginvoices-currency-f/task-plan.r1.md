# Persist Currency, Description, and RawData on Imported Marketing Transactions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist `Currency`, `Description`, and `RawData` from `MarketingTransaction` into `ImportedMarketingTransaction` end-to-end (domain entity, EF mapping, database schema, import service), so monetary data carries an explicit ISO 4217 currency code and adapter-supplied audit context isn't discarded.

**Architecture:** Pure additive change inside the `MarketingInvoices` vertical slice. Three columns are added to the `public."ImportedMarketingTransactions"` table; `Currency` is non-nullable and backfilled to `'CZK'` for existing rows using the project's established three-step migration pattern (add-nullable → `UPDATE` backfill → `ALTER` to NOT NULL). The import service maps the three fields verbatim and skips (counts `Failed`) any inbound transaction with an empty/whitespace `Currency`. No public API, contract, or interface signature changes.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql provider), PostgreSQL, MediatR (untouched), xUnit + Moq for tests.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` | Modify | Add three POCO properties to the persisted entity. |
| `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` | Modify | Add three EF property mappings (column names, types, length constraints, NOT NULL for `Currency`). |
| `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` | Create (via EF tooling, then hand-edit) | Add the three columns to the database; backfill `Currency = 'CZK'`; enforce NOT NULL on `Currency`. |
| `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.Designer.cs` | Create (auto-generated) | Standard EF migration designer snapshot. |
| `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` | Modify (auto-regenerated) | Snapshot reflects the new properties. |
| `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | Modify | (a) Add empty-Currency guard at top of per-transaction loop (before duplicate-check). (b) Extend object initializer to map `Currency`, `Description`, `RawData`. |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Modify | Add three test cases: full round-trip, empty-Currency, whitespace-Currency. |

No new files for production code beyond the migration. No new directories. No DI registration changes.

---

## Task 1: Add Currency, Description, RawData properties to the domain entity

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`

This is a structural change that unblocks the EF configuration (Task 2) and the test fixtures (Task 6). On its own it ships compile-only behavior.

- [ ] **Step 1: Open the entity file and add three properties**

Current content of `ImportedMarketingTransaction.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class ImportedMarketingTransaction : IEntity<int>
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; } = false;
    public string? ErrorMessage { get; set; }
}
```

Replace its contents with:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class ImportedMarketingTransaction : IEntity<int>
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public string? Description { get; set; }
    public string? RawData { get; set; }
}
```

Notes:
- `Currency` is non-nullable (matches `MarketingTransaction.Currency`, which is also non-nullable `string` defaulted to `string.Empty`).
- `Description` and `RawData` are nullable (`string?`) — adapters may legitimately provide null/empty for description, and `RawData` is purely diagnostic.
- Keep the entity a `class` (not a `record`) — see CLAUDE.md "DTOs/entities are classes" gotcha.

- [ ] **Step 2: Build the solution to confirm no compile errors**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds (`Build succeeded. 0 Error(s)`).

The existing tests in `MarketingInvoiceImportServiceTests.cs` will still compile because they don't reference the new entity properties yet.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs
git commit -m "feat: add Currency, Description, RawData properties to ImportedMarketingTransaction entity"
```

---

## Task 2: Configure the three new columns in EF Core

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`

This makes EF Core aware of the new columns. It will be picked up by `dotnet ef migrations add` in Task 3.

- [ ] **Step 1: Open the configuration file**

Current configuration (file end, before the `HasIndex` call at line 57):

```csharp
        builder.Property(e => e.ErrorMessage)
            .HasColumnName("ErrorMessage")
            .HasColumnType("text");

        builder.HasIndex(e => new { e.Platform, e.TransactionId })
            .IsUnique()
            .HasDatabaseName("IX_ImportedMarketingTransactions_Platform_TransactionId");
```

- [ ] **Step 2: Insert three new `builder.Property(...)` calls before the `HasIndex(...)` call**

After the `ErrorMessage` configuration block and before `builder.HasIndex(...)`, add:

```csharp
        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasColumnName("Currency")
            .HasColumnType("character varying(3)");

        builder.Property(e => e.Description)
            .HasMaxLength(500)
            .HasColumnName("Description")
            .HasColumnType("character varying(500)");

        builder.Property(e => e.RawData)
            .HasColumnName("RawData")
            .HasColumnType("text");
```

Notes:
- Style matches the existing `Property(...)` calls in the file (explicit `HasColumnName` + `HasColumnType`).
- `IsRequired()` only on `Currency` — others are nullable.
- No `HasDefaultValue('CZK')` here; the default is purely a migration concern (existing rows), not a runtime EF concern.

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs
git commit -m "feat: configure Currency, Description, RawData columns in EF mapping"
```

---

## Task 3: Scaffold the EF migration with `dotnet ef migrations add`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.Designer.cs`
- Modify (auto-regenerated): `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

The CLI generates a vanilla `AddColumn` migration. We will hand-edit it in Task 4 to add the backfill + `AlterColumn`.

- [ ] **Step 1: Run the EF migrations add command**

Run from the repo root:

```bash
dotnet ef migrations add AddCurrencyDescriptionRawDataToImportedMarketingTransactions \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: three new/changed files appear in `backend/src/Anela.Heblo.Persistence/Migrations/`:
1. `{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` (new)
2. `{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.Designer.cs` (new)
3. `ApplicationDbContextModelSnapshot.cs` (modified — should now include the three new properties on the `ImportedMarketingTransaction` entity block)

- [ ] **Step 2: Verify the generated `.cs` migration has three `AddColumn` calls**

Open the newly-generated `{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs`. The `Up` method should contain something like:

```csharp
migrationBuilder.AddColumn<string>(
    name: "Currency",
    schema: "public",
    table: "ImportedMarketingTransactions",
    type: "character varying(3)",
    maxLength: 3,
    nullable: false,
    defaultValue: "");

migrationBuilder.AddColumn<string>(
    name: "Description",
    schema: "public",
    table: "ImportedMarketingTransactions",
    type: "character varying(500)",
    maxLength: 500,
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "RawData",
    schema: "public",
    table: "ImportedMarketingTransactions",
    type: "text",
    nullable: true);
```

(The exact `defaultValue` clause on `Currency` will depend on EF's defaults — EF emits `defaultValue: ""` when scaffolding NOT NULL string columns. This is exactly what we will replace in Task 4 with the explicit three-step pattern.)

The `Down` method should contain three `DropColumn` calls.

- [ ] **Step 3: Verify the snapshot was updated correctly**

In `ApplicationDbContextModelSnapshot.cs`, the `ImportedMarketingTransaction` block (around line 2336) should now include three additional `b.Property<string>(...)` entries for `Currency`, `Description`, and `RawData`.

Quick spot-check command (no need to run; visual inspection is fine):

```bash
grep -n "Currency\|Description\|RawData" backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs | head -30
```

Expected: lines showing the three new properties registered on the `Anela.Heblo.Domain.Features.MarketingInvoices.ImportedMarketingTransaction` entity.

- [ ] **Step 4: Build to confirm the scaffolded files compile**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 5: Commit the scaffolded migration as-is**

We will hand-edit it in Task 4; committing the scaffold first makes the manual changes reviewable as a separate diff.

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "chore: scaffold migration for Currency, Description, RawData columns"
```

---

## Task 4: Hand-edit the migration to use the add-nullable → backfill → alter pattern

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs`

Background — see arch review Decision 1 and Amendment 1. The prior art `20251208184900_AddTransferIdColumnWithDataHandling.cs:44-67` adds a NOT NULL column safely on an existing table by:
1. Adding the column as nullable.
2. Running an explicit `UPDATE … WHERE … IS NULL` to backfill.
3. Altering the column to NOT NULL.

`Description` and `RawData` are nullable, so they stay as plain `AddColumn(nullable: true)` calls. Only `Currency` needs the three-step pattern.

- [ ] **Step 1: Replace the `Up` method**

Open the migration file and replace the body of `Up(MigrationBuilder migrationBuilder)` with the following. The existing `using` directives and class skeleton stay as-is.

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Currency: add as nullable so backfill can populate existing rows.
    migrationBuilder.AddColumn<string>(
        name: "Currency",
        schema: "public",
        table: "ImportedMarketingTransactions",
        type: "character varying(3)",
        maxLength: 3,
        nullable: true);

    // Backfill existing rows. Assumption verified pre-deploy: all historical
    // ImportedMarketingTransactions rows are CZK-billed (see spec FR-4 and
    // arch review Amendment 4 — confirm with the SELECT DISTINCT query on prod
    // before applying this migration there).
    migrationBuilder.Sql(
        "UPDATE public.\"ImportedMarketingTransactions\" SET \"Currency\" = 'CZK' WHERE \"Currency\" IS NULL;");

    // Now enforce NOT NULL. New inserts must supply Currency explicitly;
    // the FR-6 guard in MarketingInvoiceImportService rejects empties.
    migrationBuilder.AlterColumn<string>(
        name: "Currency",
        schema: "public",
        table: "ImportedMarketingTransactions",
        type: "character varying(3)",
        maxLength: 3,
        nullable: false,
        oldClrType: typeof(string),
        oldType: "character varying(3)",
        oldMaxLength: 3,
        oldNullable: true);

    // Description: nullable, no backfill needed.
    migrationBuilder.AddColumn<string>(
        name: "Description",
        schema: "public",
        table: "ImportedMarketingTransactions",
        type: "character varying(500)",
        maxLength: 500,
        nullable: true);

    // RawData: nullable, no backfill needed.
    migrationBuilder.AddColumn<string>(
        name: "RawData",
        schema: "public",
        table: "ImportedMarketingTransactions",
        type: "text",
        nullable: true);
}
```

- [ ] **Step 2: Replace the `Down` method**

Replace the `Down` body with three `DropColumn` calls (the order doesn't matter for `Down`):

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "RawData",
        schema: "public",
        table: "ImportedMarketingTransactions");

    migrationBuilder.DropColumn(
        name: "Description",
        schema: "public",
        table: "ImportedMarketingTransactions");

    migrationBuilder.DropColumn(
        name: "Currency",
        schema: "public",
        table: "ImportedMarketingTransactions");
}
```

- [ ] **Step 3: Build to confirm the hand-edited migration compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 4: Apply the migration to a fresh local Postgres dev database**

Run from the repo root:

```bash
dotnet ef database update \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: command completes with `Done.` and no error. If the local database already has the columns (from a prior partial attempt), drop and recreate it, or revert to the pre-migration state with `dotnet ef database update <previous-migration-name>` first.

- [ ] **Step 5: Spot-check the schema via `psql`**

If `psql` is available locally, verify the three columns exist with the right types and that `Currency` is NOT NULL:

```bash
psql "$ConnectionStrings__DefaultConnection" -c "\d public.\"ImportedMarketingTransactions\""
```

Expected output includes lines roughly like:

```
 Currency        | character varying(3)   | not null
 Description     | character varying(500) |
 RawData         | text                   |
```

If the local DB had any pre-existing rows, also verify the backfill:

```bash
psql "$ConnectionStrings__DefaultConnection" \
  -c "SELECT \"Currency\", COUNT(*) FROM public.\"ImportedMarketingTransactions\" GROUP BY \"Currency\";"
```

Expected: any existing rows show `Currency = 'CZK'`; new ones (none yet) will follow.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: backfill Currency='CZK' on existing ImportedMarketingTransactions rows"
```

---

## Task 5: Add failing test for Currency/Description/RawData round-trip in import service

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

We follow the project's TDD discipline: write the test first, watch it fail, then implement (Task 6).

- [ ] **Step 1: Add the new test case to `MarketingInvoiceImportServiceTests.cs`**

Append the following test inside the class (after `ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce`, before the closing `}` of the class):

```csharp
[Fact]
public async Task ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData()
{
    // Arrange
    var from = new DateTime(2026, 4, 1);
    var to = new DateTime(2026, 4, 2);

    var transactions = new List<MarketingTransaction>
    {
        new()
        {
            TransactionId = "TX-EUR-001",
            Platform = "TestPlatform",
            Amount = 123.45m,
            TransactionDate = from,
            Description = "campaign X",
            Currency = "EUR",
            RawData = "{\"foo\":1}",
        },
    };

    _mockSource
        .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
        .ReturnsAsync(transactions);

    _mockRepository
        .Setup(x => x.ExistsAsync("TestPlatform", "TX-EUR-001", It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);

    ImportedMarketingTransaction? captured = null;
    _mockRepository
        .Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
        .Callback<ImportedMarketingTransaction, CancellationToken>((entity, _) => captured = entity)
        .Returns(Task.CompletedTask);

    _mockRepository
        .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(1);

    // Act
    var result = await _service.ImportAsync(_mockSource.Object, from, to);

    // Assert
    Assert.Equal(1, result.Imported);
    Assert.Equal(0, result.Skipped);
    Assert.Equal(0, result.Failed);
    Assert.NotNull(captured);
    Assert.Equal("EUR", captured!.Currency);
    Assert.Equal("campaign X", captured.Description);
    Assert.Equal("{\"foo\":1}", captured.RawData);
}
```

Notes:
- `Currency = "EUR"` confirms FR-5's "must not be coerced to CZK" requirement.
- `captured` is set via Moq `Callback` (same pattern Moq uses elsewhere in this codebase).
- `MarketingTransaction` already has `Currency`, `Description`, `RawData` properties (see `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`).
- Entity `Currency`, `Description`, `RawData` properties exist now thanks to Task 1.

- [ ] **Step 2: Run the test to confirm it FAILS**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData"
```

Expected: test fails on one of the three `Assert.Equal(...)` checks — `captured.Currency` will be `""` (default), `captured.Description` and `captured.RawData` will be `null`. The service has not yet been updated to map these fields.

If the test passes unexpectedly, the implementation is already wrong — re-read the current `MarketingInvoiceImportService.cs:58-66` object initializer and verify it does NOT yet include `Currency`/`Description`/`RawData`.

(Do NOT commit yet — test + impl get committed together in Task 6.)

---

## Task 6: Update import service to map Currency, Description, RawData

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

- [ ] **Step 1: Extend the object initializer**

Open `MarketingInvoiceImportService.cs`. Find the existing object initializer at lines 58-66:

```csharp
var entity = new ImportedMarketingTransaction
{
    TransactionId = transaction.TransactionId,
    Platform = source.Platform,
    Amount = transaction.Amount,
    TransactionDate = transaction.TransactionDate,
    ImportedAt = DateTime.UtcNow,
    IsSynced = false,
};
```

Replace it with:

```csharp
var entity = new ImportedMarketingTransaction
{
    TransactionId = transaction.TransactionId,
    Platform = source.Platform,
    Amount = transaction.Amount,
    Currency = transaction.Currency,
    TransactionDate = transaction.TransactionDate,
    ImportedAt = DateTime.UtcNow,
    IsSynced = false,
    Description = transaction.Description,
    RawData = transaction.RawData,
};
```

Notes:
- `Currency` is passed verbatim — no `ToUpperInvariant()`, no normalization. ISO-4217 validation is upstream's responsibility per the spec's "Out of Scope".
- `Description` from `MarketingTransaction` is non-nullable `string` (defaults to `string.Empty`); the entity stores it as `string?`. Passing a value through this assignment is implicit conversion — empty strings pass through verbatim.
- `RawData` is `string?` on both sides — pass through.

- [ ] **Step 2: Run the Task-5 test to confirm it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData"
```

Expected: 1 passed, 0 failed.

- [ ] **Step 3: Run the full import-service test class to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
```

Expected: all tests pass (6 pre-existing + 1 new = 7). The existing tests already construct fixtures with `Currency = "CZK"`, `Description = "Ad charge"` (see test file lines 38-39, 74, 103-104, 139-140, 197-198), so they're unaffected by the additive mapping.

- [ ] **Step 4: Commit test + implementation together**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs \
        backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
git commit -m "feat: map Currency, Description, RawData in MarketingInvoiceImportService"
```

---

## Task 7: Add failing test for the empty-Currency guard

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

This test drives the FR-6 behavior. Per arch review Amendment 2, the guard must run **before** the `stagedIds` check at line 39 and the `ExistsAsync` call at line 48. We assert this directly via `Times.Never` on both `AddAsync` and `ExistsAsync`.

- [ ] **Step 1: Add the empty-Currency test**

Append inside the class:

```csharp
[Fact]
public async Task ImportAsync_EmptyCurrency_Skips_CountsFailed_DoesNotCallExistsOrAdd()
{
    // Arrange
    var from = new DateTime(2026, 4, 1);
    var to = new DateTime(2026, 4, 2);

    var transactions = new List<MarketingTransaction>
    {
        new()
        {
            TransactionId = "TX-BAD-001",
            Platform = "TestPlatform",
            Amount = 100m,
            TransactionDate = from,
            Description = "missing currency",
            Currency = "",
        },
    };

    _mockSource
        .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
        .ReturnsAsync(transactions);

    // Act
    var result = await _service.ImportAsync(_mockSource.Object, from, to);

    // Assert
    Assert.Equal(0, result.Imported);
    Assert.Equal(0, result.Skipped);
    Assert.Equal(1, result.Failed);
    _mockRepository.Verify(
        x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _mockRepository.Verify(
        x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _mockRepository.Verify(
        x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
        Times.Never);

    // Warning log: verify it was emitted with TransactionId and Platform context.
    _mockLogger.Verify(
        l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains("TX-BAD-001") &&
                v.ToString()!.Contains("TestPlatform")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.AtLeastOnce);
}
```

Notes:
- The `_mockLogger.Verify(...)` block is the canonical Moq pattern for asserting `ILogger.Log` was called at a specific level — it works because `ILogger.LogWarning(...)` is an extension method that resolves to `ILogger.Log(LogLevel.Warning, ...)` internally.
- The `v.ToString()!.Contains(...)` checks are loose — they confirm structured properties were inlined into the formatted message. The Task-8 implementation will use `_logger.LogWarning("...", transaction.TransactionId, source.Platform)`-style structured logging, which Moq's `It.IsAnyType` captures via the formatted state object.

- [ ] **Step 2: Run the test to confirm it FAILS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ImportAsync_EmptyCurrency_Skips_CountsFailed_DoesNotCallExistsOrAdd"
```

Expected: test fails. With no guard in place, the empty-currency transaction will flow through `ExistsAsync` (returns `false` from the default Moq stub) and `AddAsync`, so the `Verify(..., Times.Never)` assertions will fail (most likely the `ExistsAsync` one fails first).

(Do not commit yet — Task 8 commits guard + tests together with the whitespace test from Task 9.)

---

## Task 8: Add the empty-Currency guard at the top of the import loop

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

Per arch review Amendment 2, the guard runs **first** inside the `try` block, before the `stagedIds.Contains(...)` check and before `ExistsAsync`.

- [ ] **Step 1: Insert the guard at the top of the per-transaction loop body**

Open `MarketingInvoiceImportService.cs`. Find the loop body (lines 35-79). Inside the existing `try { ... }` (starting at line 37), the current first statement is the `stagedIds.Contains(transaction.TransactionId)` check.

Insert the new guard immediately after `try {` and before that check. The body of the `try` block becomes:

```csharp
try
{
    if (string.IsNullOrWhiteSpace(transaction.Currency))
    {
        _logger.LogWarning(
            "Marketing transaction {TransactionId} for {Platform} has empty Currency — skipping",
            transaction.TransactionId, source.Platform);
        result.Failed++;
        continue;
    }

    if (stagedIds.Contains(transaction.TransactionId))
    {
        _logger.LogDebug(
            "Transaction {TransactionId} for {Platform} already staged in this run — skipping",
            transaction.TransactionId, source.Platform);
        result.Skipped++;
        continue;
    }

    var exists = await _repository.ExistsAsync(source.Platform, transaction.TransactionId, ct);
    if (exists)
    {
        _logger.LogDebug(
            "Transaction {TransactionId} for {Platform} already imported — skipping",
            transaction.TransactionId, source.Platform);
        result.Skipped++;
        continue;
    }

    var entity = new ImportedMarketingTransaction
    {
        TransactionId = transaction.TransactionId,
        Platform = source.Platform,
        Amount = transaction.Amount,
        Currency = transaction.Currency,
        TransactionDate = transaction.TransactionDate,
        ImportedAt = DateTime.UtcNow,
        IsSynced = false,
        Description = transaction.Description,
        RawData = transaction.RawData,
    };

    await _repository.AddAsync(entity, ct);
    stagedIds.Add(transaction.TransactionId);
    stagedCount++;
}
catch (Exception ex)
{
    // unchanged
}
```

Notes:
- `string.IsNullOrWhiteSpace(...)` handles `null`, `""`, and `"   "` in one check (covers Task 9's case automatically).
- `result.Failed++` (not `Skipped`) — per arch review Decision 2, this is a real defect, not a benign skip.
- `continue` moves to the next transaction; the per-row `try/catch` philosophy is preserved.
- The log message uses structured properties so the Moq verify in Task 7 will match.

- [ ] **Step 2: Run the Task-7 test to confirm it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ImportAsync_EmptyCurrency_Skips_CountsFailed_DoesNotCallExistsOrAdd"
```

Expected: 1 passed, 0 failed.

- [ ] **Step 3: Run the full import-service test class to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
```

Expected: 8 tests pass, 0 fail. The existing tests use `Currency = "CZK"` everywhere so they still pass through the guard.

(Do not commit yet — Task 9 adds the whitespace test, then we commit the guard + both new tests together.)

---

## Task 9: Add whitespace-Currency test (regression guard for `IsNullOrWhiteSpace` branch)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

This is a defensive test that pins the `IsNullOrWhiteSpace` behavior. If a future engineer refactors the guard to `string.IsNullOrEmpty(...)`, this test fails and protects the contract.

- [ ] **Step 1: Add the whitespace test**

Append inside the class:

```csharp
[Fact]
public async Task ImportAsync_WhitespaceCurrency_TreatedAsEmpty_CountsFailed()
{
    // Arrange
    var from = new DateTime(2026, 4, 1);
    var to = new DateTime(2026, 4, 2);

    var transactions = new List<MarketingTransaction>
    {
        new()
        {
            TransactionId = "TX-WS-001",
            Platform = "TestPlatform",
            Amount = 50m,
            TransactionDate = from,
            Description = "whitespace currency",
            Currency = "   ",
        },
    };

    _mockSource
        .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
        .ReturnsAsync(transactions);

    // Act
    var result = await _service.ImportAsync(_mockSource.Object, from, to);

    // Assert
    Assert.Equal(0, result.Imported);
    Assert.Equal(0, result.Skipped);
    Assert.Equal(1, result.Failed);
    _mockRepository.Verify(
        x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _mockRepository.Verify(
        x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

- [ ] **Step 2: Run the test to confirm it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ImportAsync_WhitespaceCurrency_TreatedAsEmpty_CountsFailed"
```

Expected: 1 passed. The guard implemented in Task 8 already uses `IsNullOrWhiteSpace`, so this should pass on the first run.

If it fails, the Task-8 implementation used `IsNullOrEmpty` (or similar) — go back and fix to `IsNullOrWhiteSpace`.

- [ ] **Step 3: Run the full import-service test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
```

Expected: 9 tests pass, 0 fail.

- [ ] **Step 4: Commit guard + empty + whitespace tests together**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs \
        backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
git commit -m "feat: skip and fail-count marketing transactions with empty Currency"
```

---

## Task 10: Final validation and cleanup

**Files:**
- (Verification only — no file changes expected.)

Per CLAUDE.md "Validation before completion":

- [ ] **Step 1: Full backend build (clean)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `Build succeeded. 0 Error(s)`. Warnings unchanged from baseline.

- [ ] **Step 2: Code formatting**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no diff produced. If `dotnet format` reformatted any of the files touched in this plan, inspect the changes — they should be small whitespace fixes only.

If `dotnet format` made changes, commit them as a separate housekeeping commit:

```bash
git add -A
git status
# Review the diff
git commit -m "chore: dotnet format on marketing-invoice-currency changes"
```

- [ ] **Step 3: Full backend test suite (touched test project)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests pass. The `MarketingInvoiceImportServiceTests` class should report 9 tests (6 original + 3 new). No unrelated tests should fail.

- [ ] **Step 4: Confirm migration still applies cleanly to a fresh database**

If you have access to a throwaway local Postgres, drop and re-create the dev database, then run:

```bash
dotnet ef database update \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: all migrations apply in order; the new migration runs at the end without error; the table has the three new columns with the right types and `Currency` is NOT NULL.

- [ ] **Step 5: Pre-deploy verification reminder (manual, NOT executed here)**

Before applying this migration to production, the developer must verify the CZK-backfill assumption holds on the production database. From arch review Amendment 4:

```sql
SELECT "Platform", COUNT(*)
FROM public."ImportedMarketingTransactions"
GROUP BY "Platform";
```

Then, for each platform listed, manually cross-check the connected ad account's billing currency. **If any platform is not 100% CZK-billed**, the migration's backfill SQL must be amended to a per-Platform `CASE WHEN` expression before deploy. This is a one-time operational step — do not skip it.

This step is documented here as a checklist item but is NOT part of the implementation commits. The plan is "ship-ready" once Steps 1-4 above are green.

- [ ] **Step 6: Final commit summary check**

Run: `git log --oneline main..HEAD`

Expected commit list (in order):
1. `feat: add Currency, Description, RawData properties to ImportedMarketingTransaction entity`
2. `feat: configure Currency, Description, RawData columns in EF mapping`
3. `chore: scaffold migration for Currency, Description, RawData columns`
4. `feat: backfill Currency='CZK' on existing ImportedMarketingTransactions rows`
5. `feat: map Currency, Description, RawData in MarketingInvoiceImportService`
6. `feat: skip and fail-count marketing transactions with empty Currency`
7. (Optional) `chore: dotnet format on marketing-invoice-currency changes`

Each commit is independently reviewable and revertable.

---

## Spec Coverage Matrix

| Spec requirement | Task(s) | Notes |
|---|---|---|
| FR-1 — Currency column on entity, EF, mapping | Tasks 1, 2, 6 | Entity prop in Task 1; EF config in Task 2; import service mapping in Task 6; round-trip test in Tasks 5+6. |
| FR-2 — Description column on entity, EF, mapping | Tasks 1, 2, 6 | Same flow as FR-1; round-trip test in Tasks 5+6. |
| FR-3 — RawData column on entity, EF, mapping | Tasks 1, 2, 6 | Same flow as FR-1; round-trip test in Tasks 5+6. |
| FR-4 — Migration with three-step backfill (per arch review Amendment 1) | Tasks 3, 4 | Scaffold in Task 3, hand-edit to three-step pattern in Task 4. |
| FR-5 — Import service maps all three fields | Task 6 | Verbatim mapping, no normalization of Currency. |
| FR-6 — Empty/whitespace Currency ⇒ skip + Failed++ + warning log; guard runs FIRST (per arch review Amendment 2) | Tasks 7, 8, 9 | Test, implement, regression-guard. |
| NFR-1 — No throughput regression | Task 6, 8 | No new DB calls; single `SaveChangesAsync` at end preserved. |
| NFR-2 — Currency NOT NULL after migration | Task 4 | `AlterColumn(nullable: false)` after backfill. |
| NFR-3 — Backward compatibility | All tasks | Purely additive; no public API changes; existing tests unchanged. |
| NFR-4 — Structured warning log with TransactionId + Platform | Task 8 | `_logger.LogWarning(..., transaction.TransactionId, source.Platform)`; asserted in Task 7. |
| Arch review Amendment 4 — Pre-deploy CZK verification SQL | Task 10 Step 5 | Documented as operational checklist; not part of implementation commits. |
