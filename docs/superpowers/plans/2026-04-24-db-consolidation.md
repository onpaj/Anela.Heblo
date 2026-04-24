# DB Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate the database model — remove dead code, unify all tables into the `public` schema, standardize naming conventions, and fix singular/plural inconsistencies.

**Architecture:** All changes are EF Core configuration-only (Tasks 1–2) or EF Core migration + config updates (Tasks 3–6). Each task is independently deployable. Tasks 1 and 2 carry zero DB migration risk. Tasks 3–6 each require a manual `dotnet ef database update` in every environment (dev, stg, prod).

**Tech Stack:** .NET 8, EF Core 8, PostgreSQL (Npgsql), `dotnet ef` CLI

---

## Context & Key Findings

Before implementing, know these facts gathered from reading the source:

**Dead code (mapper files already commented out in `ApplicationDbContext`):**
- `Mapping/IssuedInvoiceDbMapper.cs` — commented out: `//modelBuilder.ConfigureIssuedInvoices();`
- `Mapping/RecurringJobsDbMapper.cs` — commented out: `//modelBuilder.ConfigureRecurringJobs();`
- `Mapping/ScheduledTaskDbMapper.cs` — commented out: `//modelBuilder.ConfigureScheduledTasks();`
- The DbSets for `RecurringJob` and `ScheduledTask` are also commented out in `ApplicationDbContext.cs`

**Live `dbo` tables (10):** `BankStatements`, `imported_marketing_transactions`, `IssuedInvoice`, `IssuedInvoiceSyncData`, `KnowledgeBaseChunks`, `KnowledgeBaseDocuments`, `KnowledgeBaseQuestionLogs`, `StockTakingResults`, `PackingMaterial`, `PackingMaterialLog`

**Naming decision (required before Task 4):** See Task 4 preamble. Default recommendation: **keep PascalCase** (rename 5 snake_case tables, far less work than renaming 31).

---

## File Map

### Files to delete (Task 1)
- `backend/src/Anela.Heblo.Persistence/Mapping/IssuedInvoiceDbMapper.cs`
- `backend/src/Anela.Heblo.Persistence/Mapping/RecurringJobsDbMapper.cs`
- `backend/src/Anela.Heblo.Persistence/Mapping/ScheduledTaskDbMapper.cs`

### Files to modify — schema only (Task 2, no DB migration)
- `backend/src/Anela.Heblo.Persistence/Catalog/ManufactureDifficulty/ManufactureDifficultySettingsConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardSettingsConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardTileConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/DataQuality/InvoiceDqtResultConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/GiftPackageManufactureItemConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/GiftPackageManufactureLogConfiguration.cs`

### Files to modify — `dbo` → `public` schema (Task 3)
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceSyncDataConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseChunkConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseQuestionLogConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/StockTaking/StockTakingConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialLogConfiguration.cs`

### Files to modify — snake_case → PascalCase rename (Task 4, if PascalCase chosen)
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/DataQuality/InvoiceDqtResultConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/GiftPackageManufactureItemConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/GiftPackageManufactureLogConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`

### Files to modify — singular → plural + entity-table mismatch (Task 5)
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxItemConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxStateLogConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/StockTaking/StockTakingConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialLogConfiguration.cs`

---

## Task 1: Delete dead mapper files

**No DB migration needed. No application behavior changes.**

The three mapper files in `Mapping/` are already fully inert — every call to them is commented out in `ApplicationDbContext.cs`. Deleting them removes confusion about which mapping is authoritative.

**Files to delete:**
- `backend/src/Anela.Heblo.Persistence/Mapping/IssuedInvoiceDbMapper.cs`
- `backend/src/Anela.Heblo.Persistence/Mapping/RecurringJobsDbMapper.cs`
- `backend/src/Anela.Heblo.Persistence/Mapping/ScheduledTaskDbMapper.cs`

- [ ] **Step 1.1: Verify the mapper calls are commented out**

```bash
grep -n "ConfigureIssuedInvoices\|ConfigureRecurringJobs\|ConfigureScheduledTasks" \
  backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
```

Expected output (all commented):
```
94:        //modelBuilder.ConfigureScheduledTasks();
95:        //modelBuilder.ConfigureIssuedInvoices();
96:        //modelBuilder.ConfigureRecurringJobs();
```

If any line is NOT commented, comment it out before proceeding.

- [ ] **Step 1.2: Delete the three files**

```bash
rm backend/src/Anela.Heblo.Persistence/Mapping/IssuedInvoiceDbMapper.cs
rm backend/src/Anela.Heblo.Persistence/Mapping/RecurringJobsDbMapper.cs
rm backend/src/Anela.Heblo.Persistence/Mapping/ScheduledTaskDbMapper.cs
```

- [ ] **Step 1.3: Check whether the `Mapping/` directory has any remaining files**

```bash
ls backend/src/Anela.Heblo.Persistence/Mapping/
```

If the directory is now empty, delete it:
```bash
rmdir backend/src/Anela.Heblo.Persistence/Mapping/
```

- [ ] **Step 1.4: Build to confirm no compile errors**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 1.5: Run backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 1.6: Commit**

```bash
git add -A
git commit -m "chore(db): delete dead mapper files (IssuedInvoice, RecurringJobs, ScheduledTask)"
```

---

## Task 2: Explicit schema for implicit tables

**No DB migration needed.** On PostgreSQL without `HasDefaultSchema`, EF resolves unqualified `ToTable("Name")` to the `public` schema. This task makes that explicit so future changes (e.g. adding `HasDefaultSchema`) can't silently break things.

**11 configuration files.** Pattern: change `builder.ToTable("X")` → `builder.ToTable("X", "public")`.

- [ ] **Step 2.1: Update the 6 PascalCase implicit configs**

In each file below, find the `ToTable` call and add `"public"` as the second argument:

**`Catalog/ManufactureDifficulty/ManufactureDifficultySettingsConfiguration.cs`**
```csharp
// Before:
builder.ToTable("ManufactureDifficultySettings");
// After:
builder.ToTable("ManufactureDifficultySettings", "public");
```

**`Dashboard/UserDashboardSettingsConfiguration.cs`**
```csharp
// Before:
builder.ToTable("UserDashboardSettings");
// After:
builder.ToTable("UserDashboardSettings", "public");
```

**`Dashboard/UserDashboardTileConfiguration.cs`**
```csharp
// Before:
builder.ToTable("UserDashboardTiles");
// After:
builder.ToTable("UserDashboardTiles", "public");
```

**`GridLayouts/GridLayoutConfiguration.cs`**
```csharp
// Before:
builder.ToTable("GridLayouts");
// After:
builder.ToTable("GridLayouts", "public");
```

**`InvoiceClassification/ClassificationHistoryConfiguration.cs`**
```csharp
// Before:
builder.ToTable("ClassificationHistory");
// After:
builder.ToTable("ClassificationHistory", "public");
```

**`InvoiceClassification/ClassificationRuleConfiguration.cs`**
```csharp
// Before:
builder.ToTable("ClassificationRules");
// After:
builder.ToTable("ClassificationRules", "public");
```

- [ ] **Step 2.2: Update the 5 snake_case implicit configs**

**`BackgroundJobs/RecurringJobConfigurationConfiguration.cs`**
```csharp
// Before:
builder.ToTable("recurring_job_configurations");
// After:
builder.ToTable("recurring_job_configurations", "public");
```

**`DataQuality/DqtRunConfiguration.cs`**
```csharp
// Before:
builder.ToTable("dqt_runs");
// After:
builder.ToTable("dqt_runs", "public");
```

**`DataQuality/InvoiceDqtResultConfiguration.cs`**
```csharp
// Before:
builder.ToTable("invoice_dqt_results");
// After:
builder.ToTable("invoice_dqt_results", "public");
```

**`Logistics/GiftPackageManufacture/GiftPackageManufactureItemConfiguration.cs`**
```csharp
// Before:
builder.ToTable("gift_package_manufacture_items");
// After:
builder.ToTable("gift_package_manufacture_items", "public");
```

**`Logistics/GiftPackageManufacture/GiftPackageManufactureLogConfiguration.cs`**
```csharp
// Before:
builder.ToTable("gift_package_manufacture_logs");
// After:
builder.ToTable("gift_package_manufacture_logs", "public");
```

- [ ] **Step 2.3: Verify no ToTable call is missing a schema**

```bash
grep -rn "\.ToTable(" \
  backend/src/Anela.Heblo.Persistence/ \
  --include="*.cs" \
  --exclude-dir=obj \
  --exclude-dir=Migrations \
  | grep -v '"dbo"' \
  | grep -v '"public"' \
  | grep -v "null"
```

Expected: **no output** (every ToTable now has an explicit schema).

- [ ] **Step 2.4: Build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 2.5: Confirm EF model sees no schema drift (no pending migration)**

```bash
cd backend/src/Anela.Heblo.Persistence && \
  dotnet ef migrations add Verify_ExplicitSchemas --no-build 2>&1 | head -5
```

Expected output contains: `No model changes were detected`  
If a migration IS generated, inspect it — it should be empty. Delete it immediately:
```bash
# If empty migration was created, delete it:
dotnet ef migrations remove
```

- [ ] **Step 2.6: Commit**

```bash
cd ../../../..
git add backend/src/Anela.Heblo.Persistence/
git commit -m "chore(db): make schema explicit for 11 implicit ToTable calls (all => public)"
```

---

## Task 3: Migrate `dbo` tables to `public` schema

**Requires an EF Core migration + manual `dotnet ef database update` in every environment.**

**Rollback SQL** (run in psql if migration must be reverted):
```sql
ALTER TABLE public."BankStatements"              SET SCHEMA dbo;
ALTER TABLE public."imported_marketing_transactions" SET SCHEMA dbo;
ALTER TABLE public."IssuedInvoice"               SET SCHEMA dbo;
ALTER TABLE public."IssuedInvoiceSyncData"       SET SCHEMA dbo;
ALTER TABLE public."KnowledgeBaseChunks"         SET SCHEMA dbo;
ALTER TABLE public."KnowledgeBaseDocuments"      SET SCHEMA dbo;
ALTER TABLE public."KnowledgeBaseQuestionLogs"   SET SCHEMA dbo;
ALTER TABLE public."StockTakingResults"          SET SCHEMA dbo;
ALTER TABLE public."PackingMaterial"             SET SCHEMA dbo;
ALTER TABLE public."PackingMaterialLog"          SET SCHEMA dbo;
```

- [ ] **Step 3.1: Update all 10 `dbo` config files**

Change `"dbo"` → `"public"` in the `ToTable` call in each file:

```
Features/Bank/BankStatementImportConfiguration.cs
Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs
Invoices/IssuedInvoiceConfiguration.cs
Invoices/IssuedInvoiceSyncDataConfiguration.cs
KnowledgeBase/KnowledgeBaseChunkConfiguration.cs
KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs
KnowledgeBase/KnowledgeBaseQuestionLogConfiguration.cs
Logistics/StockTaking/StockTakingConfiguration.cs
PackingMaterials/PackingMaterialConfiguration.cs
PackingMaterials/PackingMaterialLogConfiguration.cs
```

Example (same pattern for all 10):
```csharp
// Before:
builder.ToTable("BankStatements", "dbo");
// After:
builder.ToTable("BankStatements", "public");
```

- [ ] **Step 3.2: Verify no `dbo` references remain in active config files**

```bash
grep -rn '"dbo"' \
  backend/src/Anela.Heblo.Persistence/ \
  --include="*.cs" \
  --exclude-dir=obj \
  --exclude-dir=Migrations
```

Expected: **no output**.

- [ ] **Step 3.3: Build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3.4: Generate EF migration**

```bash
cd backend/src/Anela.Heblo.Persistence && \
  dotnet ef migrations add MoveTablesFromDboToPublicSchema
```

- [ ] **Step 3.5: Inspect the generated migration**

```bash
ls -t Migrations/ | head -2
```

Open the most recent `*_MoveTablesFromDboToPublicSchema.cs` file. The `Up()` method must contain exactly 10 `RenameTable` calls, one per table. Example of expected content:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.RenameTable(name: "BankStatements",    schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "imported_marketing_transactions", schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "IssuedInvoice",     schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "IssuedInvoiceSyncData", schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "KnowledgeBaseChunks", schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "KnowledgeBaseDocuments", schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "KnowledgeBaseQuestionLogs", schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "StockTakingResults", schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "PackingMaterial",   schema: "dbo", newSchema: "public");
    migrationBuilder.RenameTable(name: "PackingMaterialLog", schema: "dbo", newSchema: "public");
}
```

If EF generated anything else (column changes, index changes) — stop and investigate before proceeding.

- [ ] **Step 3.6: Apply migration to local dev database**

```bash
dotnet ef database update
```

Expected: `Done.` with no errors.

- [ ] **Step 3.7: Run backend tests**

```bash
cd ../../.. && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all pass.

- [ ] **Step 3.8: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/
git commit -m "feat(db): migrate all dbo tables to public schema (10 tables)"
```

- [ ] **Step 3.9: Apply to staging**

```bash
# Connect to staging database connection string, then:
dotnet ef database update --connection "<STAGING_CONNECTION_STRING>"
```

Or instruct ops to apply the migration manually.

---

## Task 4: Standardize table naming (snake_case vs PascalCase)

> **DECISION GATE** — Choose before implementing:
>
> **Option A — Keep PascalCase (recommended, lower effort):**  
> Rename 6 snake_case tables to PascalCase. Affects: `recurring_job_configurations` → `RecurringJobConfigurations`, `dqt_runs` → `DqtRuns`, `invoice_dqt_results` → `InvoiceDqtResults`, `gift_package_manufacture_items` → `GiftPackageManufactureItems`, `gift_package_manufacture_logs` → `GiftPackageManufactureLogs`, `imported_marketing_transactions` → `ImportedMarketingTransactions`.
>
> **Option B — Migrate all to snake_case (PostgreSQL idiomatic, higher effort):**  
> Rename 31 PascalCase tables. Requires a large migration and updating all index names that include the table name. Estimate: 2–4 additional hours. Raw SQL queries and pgAdmin views must be updated.
>
> **This plan implements Option A.** If you choose Option B, adapt the steps accordingly.

- [ ] **Step 4.1: Update the 6 snake_case config files to PascalCase table names**

**`BackgroundJobs/RecurringJobConfigurationConfiguration.cs`**
```csharp
// Before:
builder.ToTable("recurring_job_configurations", "public");
// After:
builder.ToTable("RecurringJobConfigurations", "public");
```

Also update column name overrides in this file — change all `HasColumnName("snake_case")` to PascalCase:
```csharp
// Before:
.HasColumnName("id")
.HasColumnName("job_name")
.HasColumnName("display_name")
.HasColumnName("description")
.HasColumnName("cron_expression")
.HasColumnName("is_enabled")
.HasColumnName("last_modified_at")
.HasColumnName("last_modified_by")
// After: remove all HasColumnName calls — EF will use property names directly (which are already PascalCase)
```

**`DataQuality/DqtRunConfiguration.cs`**
```csharp
// Before:
builder.ToTable("dqt_runs", "public");
// After:
builder.ToTable("DqtRuns", "public");
```

Also remove or update any `HasColumnName("snake_case")` overrides in this file to PascalCase.

**`DataQuality/InvoiceDqtResultConfiguration.cs`**
```csharp
// Before:
builder.ToTable("invoice_dqt_results", "public");
// After:
builder.ToTable("InvoiceDqtResults", "public");
```

Also remove or update any `HasColumnName("snake_case")` overrides.

**`Logistics/GiftPackageManufacture/GiftPackageManufactureItemConfiguration.cs`**
```csharp
// Before:
builder.ToTable("gift_package_manufacture_items", "public");
// After:
builder.ToTable("GiftPackageManufactureItems", "public");
```

**`Logistics/GiftPackageManufacture/GiftPackageManufactureLogConfiguration.cs`**
```csharp
// Before:
builder.ToTable("gift_package_manufacture_logs", "public");
// After:
builder.ToTable("GiftPackageManufactureLogs", "public");
```

**`Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`**
```csharp
// Before:
builder.ToTable("imported_marketing_transactions", "public");
// After:
builder.ToTable("ImportedMarketingTransactions", "public");
```

- [ ] **Step 4.2: Verify no snake_case table names remain**

```bash
grep -rn "ToTable" \
  backend/src/Anela.Heblo.Persistence/ \
  --include="*.cs" \
  --exclude-dir=obj \
  --exclude-dir=Migrations \
  | grep -E '"[a-z][a-z_]+'
```

Expected: **no output**.

- [ ] **Step 4.3: Build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 4.4: Generate EF migration**

```bash
cd backend/src/Anela.Heblo.Persistence && \
  dotnet ef migrations add StandardizeTableNamingToPascalCase
```

- [ ] **Step 4.5: Inspect the generated migration**

Open the generated migration file. The `Up()` method must contain exactly 6 `RenameTable` calls:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.RenameTable(name: "recurring_job_configurations", schema: "public", newName: "RecurringJobConfigurations");
    migrationBuilder.RenameTable(name: "dqt_runs",                     schema: "public", newName: "DqtRuns");
    migrationBuilder.RenameTable(name: "invoice_dqt_results",          schema: "public", newName: "InvoiceDqtResults");
    migrationBuilder.RenameTable(name: "gift_package_manufacture_items", schema: "public", newName: "GiftPackageManufactureItems");
    migrationBuilder.RenameTable(name: "gift_package_manufacture_logs", schema: "public", newName: "GiftPackageManufactureLogs");
    migrationBuilder.RenameTable(name: "imported_marketing_transactions", schema: "public", newName: "ImportedMarketingTransactions");
}
```

Also expect column rename operations for the `RecurringJobConfigurations` table (from snake_case column names to PascalCase), and the DQT tables if they had snake_case column names. Verify each column rename corresponds to a real `HasColumnName` removal.

> **Note:** EF Core may generate `RenameColumn` for each column in `RecurringJobConfigurations` since we removed `HasColumnName`. This is expected and correct — those columns need to be renamed in the DB to match the C# property names.

- [ ] **Step 4.6: Apply migration to local dev database**

```bash
dotnet ef database update
```

Expected: `Done.`

- [ ] **Step 4.7: Run backend tests**

```bash
cd ../../.. && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all pass.

- [ ] **Step 4.8: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/
git commit -m "feat(db): standardize all table names to PascalCase (rename 6 snake_case tables)"
```

---

## Task 5: Fix singular table names + entity-table mismatch

**7 table renames.** All require `RenameTable` migrations.

Tables being renamed:

| Old name | New name | Config file |
|----------|----------|-------------|
| `IssuedInvoice` | `IssuedInvoices` | `Invoices/IssuedInvoiceConfiguration.cs` |
| `TransportBox` | `TransportBoxes` | `Logistics/TransportBoxes/TransportBoxConfiguration.cs` |
| `TransportBoxItem` | `TransportBoxItems` | `Logistics/TransportBoxes/TransportBoxItemConfiguration.cs` |
| `TransportBoxStateLog` | `TransportBoxStateLogs` | `Logistics/TransportBoxes/TransportBoxStateLogConfiguration.cs` |
| `StockTakingResults` | `StockTakingRecords` | `Logistics/StockTaking/StockTakingConfiguration.cs` |
| `PackingMaterial` | `PackingMaterials` | `PackingMaterials/PackingMaterialConfiguration.cs` |
| `PackingMaterialLog` | `PackingMaterialLogs` | `PackingMaterials/PackingMaterialLogConfiguration.cs` |

**Rollback SQL:**
```sql
ALTER TABLE public."IssuedInvoices"       RENAME TO "IssuedInvoice";
ALTER TABLE public."TransportBoxes"       RENAME TO "TransportBox";
ALTER TABLE public."TransportBoxItems"    RENAME TO "TransportBoxItem";
ALTER TABLE public."TransportBoxStateLogs" RENAME TO "TransportBoxStateLog";
ALTER TABLE public."StockTakingRecords"   RENAME TO "StockTakingResults";
ALTER TABLE public."PackingMaterials"     RENAME TO "PackingMaterial";
ALTER TABLE public."PackingMaterialLogs"  RENAME TO "PackingMaterialLog";
```

- [ ] **Step 5.1: Update the 7 config files**

**`Invoices/IssuedInvoiceConfiguration.cs`**
```csharp
// Before:
builder.ToTable("IssuedInvoice", "public");
// After:
builder.ToTable("IssuedInvoices", "public");
```

Also update index names that embed the old table name:
```csharp
// Before:
.HasDatabaseName("IX_IssuedInvoice_InvoiceDate");
// After:
.HasDatabaseName("IX_IssuedInvoices_InvoiceDate");
```
Apply the same rename to all 5 indexes in this file (`IX_IssuedInvoice_LastSyncTime`, `IX_IssuedInvoice_IsSynced`, `IX_IssuedInvoice_ErrorType`, `IX_IssuedInvoice_CustomerName`).

**`Logistics/TransportBoxes/TransportBoxConfiguration.cs`**
```csharp
// Before:
builder.ToTable("TransportBox", "public");
// After:
builder.ToTable("TransportBoxes", "public");
```

**`Logistics/TransportBoxes/TransportBoxItemConfiguration.cs`**
```csharp
// Before:
builder.ToTable("TransportBoxItem", "public");
// After:
builder.ToTable("TransportBoxItems", "public");
```

**`Logistics/TransportBoxes/TransportBoxStateLogConfiguration.cs`**
```csharp
// Before:
builder.ToTable("TransportBoxStateLog", "public");
// After:
builder.ToTable("TransportBoxStateLogs", "public");
```

**`Logistics/StockTaking/StockTakingConfiguration.cs`**
```csharp
// Before:
builder.ToTable("StockTakingResults", "public");
// After:
builder.ToTable("StockTakingRecords", "public");
```

**`PackingMaterials/PackingMaterialConfiguration.cs`**
```csharp
// Before:
builder.ToTable("PackingMaterial", "public");
// After:
builder.ToTable("PackingMaterials", "public");
```

Also update index name:
```csharp
// Before:
.HasDatabaseName("IX_PackingMaterial_Name");
// After:
.HasDatabaseName("IX_PackingMaterials_Name");
```

**`PackingMaterials/PackingMaterialLogConfiguration.cs`**
```csharp
// Before:
builder.ToTable("PackingMaterialLog", "public");
// After:
builder.ToTable("PackingMaterialLogs", "public");
```

- [ ] **Step 5.2: Build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5.3: Generate EF migration**

```bash
cd backend/src/Anela.Heblo.Persistence && \
  dotnet ef migrations add NormalizeTableNamesPluralAndEntityMismatch
```

- [ ] **Step 5.4: Inspect the generated migration**

The `Up()` method should contain exactly 7 `RenameTable` calls plus index renames for `IssuedInvoices` and `PackingMaterials`. Verify no unexpected column changes are included.

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.RenameTable(name: "IssuedInvoice",       schema: "public", newName: "IssuedInvoices");
    migrationBuilder.RenameTable(name: "TransportBox",        schema: "public", newName: "TransportBoxes");
    migrationBuilder.RenameTable(name: "TransportBoxItem",    schema: "public", newName: "TransportBoxItems");
    migrationBuilder.RenameTable(name: "TransportBoxStateLog", schema: "public", newName: "TransportBoxStateLogs");
    migrationBuilder.RenameTable(name: "StockTakingResults",  schema: "public", newName: "StockTakingRecords");
    migrationBuilder.RenameTable(name: "PackingMaterial",     schema: "public", newName: "PackingMaterials");
    migrationBuilder.RenameTable(name: "PackingMaterialLog",  schema: "public", newName: "PackingMaterialLogs");
    // Plus RenameIndex calls for IX_IssuedInvoice_* and IX_PackingMaterial_Name
}
```

- [ ] **Step 5.5: Apply migration to local dev database**

```bash
dotnet ef database update
```

Expected: `Done.`

- [ ] **Step 5.6: Run backend tests**

```bash
cd ../../.. && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all pass.

- [ ] **Step 5.7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/
git commit -m "feat(db): rename singular tables to plural and fix StockTakingResults -> StockTakingRecords"
```

---

## Task 6: dotnet format pass + final verification

- [ ] **Step 6.1: Run dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: exits 0, any formatting differences auto-applied.

- [ ] **Step 6.2: Final check — no dbo, no snake_case, no implicit schemas**

```bash
grep -rn '"dbo"' backend/src/Anela.Heblo.Persistence/ --include="*.cs" --exclude-dir=obj --exclude-dir=Migrations
```
Expected: no output.

```bash
grep -rn '\.ToTable("' backend/src/Anela.Heblo.Persistence/ --include="*.cs" --exclude-dir=obj --exclude-dir=Migrations \
  | grep -v '"public"' | grep -v '"dbo"'
```
Expected: no output.

```bash
grep -rn "ToTable" backend/src/Anela.Heblo.Persistence/ --include="*.cs" --exclude-dir=obj --exclude-dir=Migrations \
  | grep -E '"[a-z][a-z_]+'
```
Expected: no output.

- [ ] **Step 6.3: Full backend build + test**

```bash
dotnet build backend/ && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build and all tests pass.

- [ ] **Step 6.4: Commit formatting changes (if any)**

```bash
git add backend/src/Anela.Heblo.Persistence/
git commit -m "chore: dotnet format after db consolidation"
```

- [ ] **Step 6.5: Apply pending migrations to staging**

Apply all migrations from Tasks 3, 4, and 5 to staging environment in order:
1. `MoveTablesFromDboToPublicSchema`
2. `StandardizeTableNamingToPascalCase`
3. `NormalizeTableNamesPluralAndEntityMismatch`

```bash
dotnet ef database update --connection "<STAGING_CONNECTION_STRING>"
```

---

## Summary: Environment Deployment Order

Each environment must have migrations applied in this order:
1. Task 3: `MoveTablesFromDboToPublicSchema`
2. Task 4: `StandardizeTableNamingToPascalCase`
3. Task 5: `NormalizeTableNamesPluralAndEntityMismatch`

Tasks 1 and 2 require only code deployment (no DB changes).

| Task | DB migration? | Risk | Rollback |
|------|-------------|------|---------|
| Task 1 — delete dead mappers | No | None | git revert |
| Task 2 — explicit schema | No | None | git revert |
| Task 3 — dbo → public | Yes | Medium | SQL in Task 3 rollback section |
| Task 4 — PascalCase names | Yes | Medium | `RenameTable` back |
| Task 5 — plural + mismatch | Yes | Medium | SQL in Task 5 rollback section |
| Task 6 — format + verify | No | None | — |
