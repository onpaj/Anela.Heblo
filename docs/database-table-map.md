# Database Table Map

> **Purpose:** Complete pre-implementation reference for the DB consolidation project.  
> Every table in the database is listed with its current state, every planned change, and which task in `docs/superpowers/plans/2026-04-24-db-consolidation.md` performs it.  
> No change may be implemented without first being defined here.

**Legend:**
- ✅ No change needed
- 🗑️ DROP — table is orphaned
- 🔀 SCHEMA — schema change only
- ✏️ RENAME — table/column rename (DB migration)
- 📋 CONFIG — config file change only, no DB migration
- 🔀✏️ SCHEMA + RENAME — both in same migration

---

## Summary Table

| # | Current schema | Current table name | Target schema | Target table name | Changes | Task |
|---|---------------|-------------------|--------------|------------------|---------|------|
| 1 | `dbo` | `Jobs` | — | — | 🗑️ DROP (orphaned) | T0 |
| 2 | `dbo` | `ScheduledTask` | — | — | 🗑️ DROP (orphaned) | T0 |
| 3 | `dbo` | `BankStatements` | `public` | `BankStatements` | 🔀 SCHEMA | T3 |
| 4 | `dbo` | `imported_marketing_transactions` | `public` | `ImportedMarketingTransactions` | 🔀✏️ SCHEMA + RENAME | T3+T4 |
| 5 | `dbo` | `IssuedInvoice` | `public` | `IssuedInvoices` | 🔀✏️ SCHEMA + RENAME + index renames | T3+T5 |
| 6 | `dbo` | `IssuedInvoiceSyncData` | `public` | `IssuedInvoiceSyncData` | 🔀 SCHEMA | T3 |
| 7 | `dbo` | `KnowledgeBaseChunks` | `public` | `KnowledgeBaseChunks` | 🔀 SCHEMA | T3 |
| 8 | `dbo` | `KnowledgeBaseDocuments` | `public` | `KnowledgeBaseDocuments` | 🔀 SCHEMA | T3 |
| 9 | `dbo` | `KnowledgeBaseQuestionLogs` | `public` | `KnowledgeBaseQuestionLogs` | 🔀 SCHEMA | T3 |
| 10 | `dbo` | `StockTakingResults` | `public` | `StockTakingRecords` | 🔀✏️ SCHEMA + RENAME (entity mismatch) | T3+T5 |
| 11 | `dbo` | `PackingMaterial` | `public` | `PackingMaterials` | 🔀✏️ SCHEMA + RENAME + index rename | T3+T5 |
| 12 | `dbo` | `PackingMaterialLog` | `public` | `PackingMaterialLogs` | 🔀✏️ SCHEMA + RENAME | T3+T5 |
| 13 | implicit→`public` | `ManufactureDifficultySettings` | `public` | `ManufactureDifficultySettings` | 📋 CONFIG (explicit schema) | T2 |
| 14 | implicit→`public` | `UserDashboardSettings` | `public` | `UserDashboardSettings` | 📋 CONFIG (explicit schema) | T2 |
| 15 | implicit→`public` | `UserDashboardTiles` | `public` | `UserDashboardTiles` | 📋 CONFIG (explicit schema) | T2 |
| 16 | implicit→`public` | `GridLayouts` | `public` | `GridLayouts` | 📋 CONFIG (explicit schema) | T2 |
| 17 | implicit→`public` | `ClassificationHistory` | `public` | `ClassificationHistory` | 📋 CONFIG (explicit schema) | T2 |
| 18 | implicit→`public` | `ClassificationRules` | `public` | `ClassificationRules` | 📋 CONFIG (explicit schema) | T2 |
| 19 | implicit→`public` | `recurring_job_configurations` | `public` | `RecurringJobConfigurations` | ✏️ RENAME table + 8 column renames | T2+T4 |
| 20 | implicit→`public` | `dqt_runs` | `public` | `DqtRuns` | ✏️ RENAME table + 10 column renames + 1 index rename | T2+T4 |
| 21 | implicit→`public` | `invoice_dqt_results` | `public` | `InvoiceDqtResults` | ✏️ RENAME table + 7 column renames + 2 index renames | T2+T4 |
| 22 | implicit→`public` | `gift_package_manufacture_items` | `public` | `GiftPackageManufactureItems` | ✏️ RENAME table + 4 column renames + 2 index renames + sequence rename | T2+T4 |
| 23 | implicit→`public` | `gift_package_manufacture_logs` | `public` | `GiftPackageManufactureLogs` | ✏️ RENAME table + 7 column renames + 3 index renames + sequence rename | T2+T4 |
| 24 | `public` | `JournalEntries` | `public` | `JournalEntries` | ✅ no change | — |
| 25 | `public` | `JournalEntryProducts` | `public` | `JournalEntryProducts` | ✅ no change | — |
| 26 | `public` | `JournalEntryTagAssignments` | `public` | `JournalEntryTagAssignments` | ✅ no change | — |
| 27 | `public` | `JournalEntryTags` | `public` | `JournalEntryTags` | ✅ no change | — |
| 28 | `public` | `StockUpOperations` | `public` | `StockUpOperations` | ✅ no change | — |
| 29 | `public` | `TransportBox` | `public` | `TransportBoxes` | ✏️ RENAME (singular→plural) | T5 |
| 30 | `public` | `TransportBoxItem` | `public` | `TransportBoxItems` | ✏️ RENAME (singular→plural) | T5 |
| 31 | `public` | `TransportBoxStateLog` | `public` | `TransportBoxStateLogs` | ✏️ RENAME (singular→plural) | T5 |
| 32 | `public` | `ManufactureOrders` | `public` | `ManufactureOrders` | ✅ no change | — |
| 33 | `public` | `ManufactureOrderNotes` | `public` | `ManufactureOrderNotes` | ✅ no change | — |
| 34 | `public` | `ManufactureOrderProducts` | `public` | `ManufactureOrderProducts` | ✅ no change | — |
| 35 | `public` | `ManufactureOrderSemiProducts` | `public` | `ManufactureOrderSemiProducts` | ✅ no change | — |
| 36 | `public` | `PurchaseOrders` | `public` | `PurchaseOrders` | ✅ no change | — |
| 37 | `public` | `PurchaseOrderHistory` | `public` | `PurchaseOrderHistory` | ✅ no change | — |
| 38 | `public` | `PurchaseOrderLines` | `public` | `PurchaseOrderLines` | ✅ no change | — |

**Count:** 38 tables total — 2 drop, 9 schema-only, 16 rename (or schema+rename), 6 config-only, 15 no change.

---

## Tables 1–2: DROP (orphaned)

### `dbo.Jobs` → DROP

**Config file:** `Mapping/RecurringJobsDbMapper.cs` (delete entire file)  
**Entity:** `RecurringJob` (domain class — delete after confirming no other use)  
**Why orphaned:** `DbSet<RecurringJob>` and `ConfigureRecurringJobs()` are both commented out in `ApplicationDbContext`. The feature was replaced by Hangfire + `RecurringJobConfigurations`.

**Pre-drop verification:**
```sql
SELECT COUNT(*) FROM dbo."Jobs";
-- Expected: 0
```

**Drop SQL:**
```sql
DROP TABLE IF EXISTS dbo."Jobs";
```

---

### `dbo.ScheduledTask` → DROP

**Config file:** `Mapping/ScheduledTaskDbMapper.cs` (delete entire file)  
**Entity:** `ScheduledTask`  
**Why orphaned:** Same as above — commented out entirely in `ApplicationDbContext`.

**Pre-drop verification:**
```sql
SELECT COUNT(*) FROM dbo."ScheduledTask";
-- Expected: 0
```

**Drop SQL:**
```sql
DROP TABLE IF EXISTS dbo."ScheduledTask";
```

---

## Tables 3–12: Schema change `dbo` → `public`

All 10 of these share the same migration pattern. Handled in Task 3.

### Simple schema changes (name stays the same)

| # | Table | Config file |
|---|-------|-------------|
| 3 | `BankStatements` | `Features/Bank/BankStatementImportConfiguration.cs` |
| 6 | `IssuedInvoiceSyncData` | `Invoices/IssuedInvoiceSyncDataConfiguration.cs` |
| 7 | `KnowledgeBaseChunks` | `KnowledgeBase/KnowledgeBaseChunkConfiguration.cs` |
| 8 | `KnowledgeBaseDocuments` | `KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs` |
| 9 | `KnowledgeBaseQuestionLogs` | `KnowledgeBase/KnowledgeBaseQuestionLogConfiguration.cs` |

Config change in each: `"dbo"` → `"public"`.  
DB migration: `RenameTable(name: "X", schema: "dbo", newSchema: "public")`.  
No column changes. No sequence risk (PostgreSQL moves owned sequences with `SET SCHEMA`).

---

### Schema change + additional rename (handled in two tasks)

#### Table 4: `dbo.imported_marketing_transactions` → `public.ImportedMarketingTransactions`

**Config file:** `Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`  
**Auto-increment PK:** yes (`ValueGeneratedOnAdd`) — sequence moves automatically with schema change  
**Column naming:** EF defaults (PascalCase) — no column renames needed  
**Index names:** check config for any `HasDatabaseName` with snake_case — update to PascalCase  

Task 3 migration step: `RenameTable(name: "imported_marketing_transactions", schema: "dbo", newSchema: "public")`  
Task 4 migration step: `RenameTable(name: "imported_marketing_transactions", schema: "public", newName: "ImportedMarketingTransactions")`

---

#### Table 5: `dbo.IssuedInvoice` → `public.IssuedInvoices`

**Config file:** `Invoices/IssuedInvoiceConfiguration.cs`  
**Column naming:** explicit PascalCase `HasColumnName` — no column renames needed  
**Indexes to rename** (all in Task 5 migration):

| Current index name | Target index name |
|-------------------|------------------|
| `IX_IssuedInvoice_InvoiceDate` | `IX_IssuedInvoices_InvoiceDate` |
| `IX_IssuedInvoice_LastSyncTime` | `IX_IssuedInvoices_LastSyncTime` |
| `IX_IssuedInvoice_IsSynced` | `IX_IssuedInvoices_IsSynced` |
| `IX_IssuedInvoice_ErrorType` | `IX_IssuedInvoices_ErrorType` |
| `IX_IssuedInvoice_CustomerName` | `IX_IssuedInvoices_CustomerName` |

Task 3: schema move. Task 5: rename table + rename 5 indexes.

---

#### Table 10: `dbo.StockTakingResults` → `public.StockTakingRecords`

**Config file:** `Logistics/StockTaking/StockTakingConfiguration.cs`  
**Entity class:** `StockTakingRecord`  
**Issue:** table name `StockTakingResults` does not match entity name — should be `StockTakingRecords`  
**Column naming:** check config for any index names to update  

Task 3: schema move. Task 5: rename table to `StockTakingRecords`.

---

#### Table 11: `dbo.PackingMaterial` → `public.PackingMaterials`

**Config file:** `PackingMaterials/PackingMaterialConfiguration.cs`  
**Indexes to rename:**

| Current | Target |
|---------|--------|
| `IX_PackingMaterial_Name` | `IX_PackingMaterials_Name` |

Task 3: schema move. Task 5: rename table + rename 1 index.

---

#### Table 12: `dbo.PackingMaterialLog` → `public.PackingMaterialLogs`

**Config file:** `PackingMaterials/PackingMaterialLogConfiguration.cs`  
**Indexes:** check config — update any `HasDatabaseName` that embeds the old table name.

Task 3: schema move. Task 5: rename table.

---

## Tables 13–18: Config-only (explicit schema)

No DB migration. Only add `"public"` as second argument to `ToTable()`.

| # | Table | Config file |
|---|-------|-------------|
| 13 | `ManufactureDifficultySettings` | `Catalog/ManufactureDifficulty/ManufactureDifficultySettingsConfiguration.cs` |
| 14 | `UserDashboardSettings` | `Dashboard/UserDashboardSettingsConfiguration.cs` |
| 15 | `UserDashboardTiles` | `Dashboard/UserDashboardTileConfiguration.cs` |
| 16 | `GridLayouts` | `GridLayouts/GridLayoutConfiguration.cs` |
| 17 | `ClassificationHistory` | `InvoiceClassification/ClassificationHistoryConfiguration.cs` |
| 18 | `ClassificationRules` | `InvoiceClassification/ClassificationRuleConfiguration.cs` |

---

## Tables 19–23: snake_case → PascalCase (table + columns)

These tables have both snake_case table names AND snake_case column names forced via `HasColumnName`. Both must be changed together in Task 4. Task 2 makes the schema explicit first (config-only, no DB change).

---

### Table 19: `recurring_job_configurations` → `RecurringJobConfigurations`

**Config file:** `BackgroundJobs/RecurringJobConfigurationConfiguration.cs`  
**Auto-increment PK:** no

**Column renames (DB migration required):**

| Current column | Target column |
|---------------|--------------|
| `id` | `Id` |
| `job_name` | `JobName` |
| `display_name` | `DisplayName` |
| `description` | `Description` |
| `cron_expression` | `CronExpression` |
| `is_enabled` | `IsEnabled` |
| `last_modified_at` | `LastModifiedAt` |
| `last_modified_by` | `LastModifiedBy` |

**Config changes:** remove all `HasColumnName(...)` calls — EF will map property names directly.

---

### Table 20: `dqt_runs` → `DqtRuns`

**Config file:** `DataQuality/DqtRunConfiguration.cs`  
**Auto-increment PK:** no (Guid)  
**FK referenced by:** `invoice_dqt_results.dqt_run_id` → `InvoiceDqtResults.DqtRunId`

**Column renames:**

| Current column | Target column |
|---------------|--------------|
| `id` | `Id` |
| `test_type` | `TestType` |
| `date_from` | `DateFrom` |
| `date_to` | `DateTo` |
| `status` | `Status` |
| `started_at` | `StartedAt` |
| `completed_at` | `CompletedAt` |
| `trigger_type` | `TriggerType` |
| `total_checked` | `TotalChecked` |
| `total_mismatches` | `TotalMismatches` |
| `error_message` | `ErrorMessage` |

**Index renames:**

| Current | Target |
|---------|--------|
| `IX_dqt_runs_test_type_started_at` | `IX_DqtRuns_TestType_StartedAt` |

---

### Table 21: `invoice_dqt_results` → `InvoiceDqtResults`

**Config file:** `DataQuality/InvoiceDqtResultConfiguration.cs`  
**Auto-increment PK:** no (Guid)  
**FK:** `dqt_run_id` references `dqt_runs.id` — both column and referenced table are being renamed, must rename FK constraint too

**Column renames:**

| Current column | Target column |
|---------------|--------------|
| `id` | `Id` |
| `dqt_run_id` | `DqtRunId` |
| `invoice_code` | `InvoiceCode` |
| `mismatch_type` | `MismatchType` |
| `shoptet_value` | `ShoptetValue` |
| `flexi_value` | `FlexiValue` |
| `details` | `Details` |

**Index renames:**

| Current | Target |
|---------|--------|
| `IX_invoice_dqt_results_dqt_run_id` | `IX_InvoiceDqtResults_DqtRunId` |
| `IX_invoice_dqt_results_invoice_code` | `IX_InvoiceDqtResults_InvoiceCode` |

---

### Table 22: `gift_package_manufacture_items` → `GiftPackageManufactureItems`

**Config file:** `Logistics/GiftPackageManufacture/GiftPackageManufactureItemConfiguration.cs`  
**Auto-increment PK:** yes — sequence `gift_package_manufacture_items_id_seq` stays functional but name becomes stale after rename; optionally rename to `GiftPackageManufactureItems_Id_seq`  
**FK:** `manufacture_log_id` references `gift_package_manufacture_logs.id`

**Column renames:**

| Current column | Target column |
|---------------|--------------|
| `id` | `Id` |
| `manufacture_log_id` | `ManufactureLogId` |
| `product_code` | `ProductCode` |
| `quantity_consumed` | `QuantityConsumed` |

**Index renames:**

| Current | Target |
|---------|--------|
| `ix_gift_package_manufacture_items_manufacture_log_id` | `IX_GiftPackageManufactureItems_ManufactureLogId` |
| `ix_gift_package_manufacture_items_product_code` | `IX_GiftPackageManufactureItems_ProductCode` |

---

### Table 23: `gift_package_manufacture_logs` → `GiftPackageManufactureLogs`

**Config file:** `Logistics/GiftPackageManufacture/GiftPackageManufactureLogConfiguration.cs`  
**Auto-increment PK:** yes — same sequence rename note as Table 22  
**FK referenced by:** `gift_package_manufacture_items.manufacture_log_id`

**Column renames:**

| Current column | Target column |
|---------------|--------------|
| `id` | `Id` |
| `gift_package_code` | `GiftPackageCode` |
| `quantity_created` | `QuantityCreated` |
| `stock_override_applied` | `StockOverrideApplied` |
| `created_at` | `CreatedAt` |
| `created_by` | `CreatedBy` |
| `operation_type` | `OperationType` |

**Index renames:**

| Current | Target |
|---------|--------|
| `ix_gift_package_manufacture_logs_created_at` | `IX_GiftPackageManufactureLogs_CreatedAt` |
| `ix_gift_package_manufacture_logs_gift_package_code` | `IX_GiftPackageManufactureLogs_GiftPackageCode` |
| `ix_gift_package_manufacture_logs_operation_type` | `IX_GiftPackageManufactureLogs_OperationType` |

---

## Tables 29–31: Rename (singular → plural)

These are already in the `public` schema with PascalCase names. Only issue is singular form.

| # | Current | Target | Config file |
|---|---------|--------|-------------|
| 29 | `public.TransportBox` | `public.TransportBoxes` | `Logistics/TransportBoxes/TransportBoxConfiguration.cs` |
| 30 | `public.TransportBoxItem` | `public.TransportBoxItems` | `Logistics/TransportBoxes/TransportBoxItemConfiguration.cs` |
| 31 | `public.TransportBoxStateLog` | `public.TransportBoxStateLogs` | `Logistics/TransportBoxes/TransportBoxStateLogConfiguration.cs` |

No column renames. Check each config for index names embedding the old table name and update them.

---

## Tables 24–28, 32–38: No changes

| # | Table | Schema | Reason |
|---|-------|--------|--------|
| 24 | `JournalEntries` | public | ✅ |
| 25 | `JournalEntryProducts` | public | ✅ |
| 26 | `JournalEntryTagAssignments` | public | ✅ |
| 27 | `JournalEntryTags` | public | ✅ |
| 28 | `StockUpOperations` | public | ✅ |
| 32 | `ManufactureOrders` | public | ✅ |
| 33 | `ManufactureOrderNotes` | public | ✅ |
| 34 | `ManufactureOrderProducts` | public | ✅ |
| 35 | `ManufactureOrderSemiProducts` | public | ✅ |
| 36 | `PurchaseOrders` | public | ✅ |
| 37 | `PurchaseOrderHistory` | public | ✅ (`History` is uncountable — not renamed) |
| 38 | `PurchaseOrderLines` | public | ✅ |

---

## Change count by task

| Task | Tables affected | DB migration? | Change type |
|------|----------------|--------------|-------------|
| T0 | 2 (Jobs, ScheduledTask) | Yes | DROP |
| T2 | 11 | No | Config: explicit schema |
| T3 | 10 | Yes | SCHEMA dbo→public |
| T4 | 6 | Yes | RENAME table + columns + indexes |
| T5 | 7 | Yes | RENAME table + indexes |

> Note: Tables 4, 5, 10, 11, 12 are touched in both T3 (schema) and T4/T5 (rename). The two migrations run sequentially — schema first, rename second.
