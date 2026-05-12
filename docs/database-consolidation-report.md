# Database Consolidation Report

> Generated: 2026-04-24  
> Scope: All EF Core entity configurations in `Anela.Heblo.Persistence`  
> Database: PostgreSQL (Azure)

---

## Summary

| Issue | Count | Severity |
|-------|-------|----------|
| Tables in `dbo` schema (SQL Server legacy) | 10 | HIGH |
| Tables with no explicit schema | 11 | MEDIUM |
| Tables using snake_case naming | 5 | HIGH |
| Tables using singular form (inconsistent) | 5 | MEDIUM |
| Entity-table name mismatch | 1 | LOW |
| Duplicate table mappings (two config files for same table) | 2 | MEDIUM |
| Legacy DbMapper pattern (should be IEntityTypeConfiguration) | 2 | LOW |

**Total tables: 36**

---

## Full Table Inventory

### Schema: `dbo` — 10 tables ⚠️

> `dbo` is a SQL Server convention. On PostgreSQL it creates a non-standard schema. These tables should be migrated to `public`.

| Table | EF Config File | Naming | Notes |
|-------|---------------|--------|-------|
| `BankStatements` | `Features/Bank/BankStatementImportConfiguration.cs` | PascalCase | |
| `imported_marketing_transactions` | `Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` | **snake_case** | Also mapped via `IssuedInvoiceDbMapper.cs` — see duplicate issue |
| `IssuedInvoice` | `Invoices/IssuedInvoiceConfiguration.cs` | PascalCase | **Singular** form; also in `Mapping/IssuedInvoiceDbMapper.cs` |
| `IssuedInvoiceSyncData` | `Invoices/IssuedInvoiceSyncDataConfiguration.cs` | PascalCase | Also in `Mapping/IssuedInvoiceDbMapper.cs` |
| `KnowledgeBaseChunks` | `KnowledgeBase/KnowledgeBaseChunkConfiguration.cs` | PascalCase | |
| `KnowledgeBaseDocuments` | `KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs` | PascalCase | |
| `KnowledgeBaseQuestionLogs` | `KnowledgeBase/KnowledgeBaseQuestionLogConfiguration.cs` | PascalCase | |
| `StockTakingResults` | `Logistics/StockTaking/StockTakingConfiguration.cs` | PascalCase | Entity class is `StockTakingRecord` — name mismatch |
| `Jobs` | `Mapping/RecurringJobsDbMapper.cs` | PascalCase | Legacy mapper; **singular** |
| `ScheduledTask` | `Mapping/ScheduledTaskDbMapper.cs` | PascalCase | Legacy mapper; **singular** |

---

### Schema: `public` — 15 tables ✅

| Table | EF Config File | Naming | Notes |
|-------|---------------|--------|-------|
| `JournalEntries` | `Catalog/Journal/JournalEntryConfiguration.cs` | PascalCase | |
| `JournalEntryProducts` | `Catalog/Journal/JournalEntryProductConfiguration.cs` | PascalCase | |
| `JournalEntryTagAssignments` | `Catalog/Journal/JournalEntryTagAssignmentConfiguration.cs` | PascalCase | |
| `JournalEntryTags` | `Catalog/Journal/JournalEntryTagConfiguration.cs` | PascalCase | |
| `StockUpOperations` | `Catalog/Stock/StockUpOperationConfiguration.cs` | PascalCase | |
| `TransportBox` | `Logistics/TransportBoxes/TransportBoxConfiguration.cs` | PascalCase | **Singular** |
| `TransportBoxItem` | `Logistics/TransportBoxes/TransportBoxItemConfiguration.cs` | PascalCase | **Singular** |
| `TransportBoxStateLog` | `Logistics/TransportBoxes/TransportBoxStateLogConfiguration.cs` | PascalCase | **Singular** |
| `ManufactureOrders` | `Manufacture/ManufactureOrderConfiguration.cs` | PascalCase | |
| `ManufactureOrderNotes` | `Manufacture/ManufactureOrderNoteConfiguration.cs` | PascalCase | |
| `ManufactureOrderProducts` | `Manufacture/ManufactureOrderProductConfiguration.cs` | PascalCase | |
| `ManufactureOrderSemiProducts` | `Manufacture/ManufactureOrderSemiProductConfiguration.cs` | PascalCase | |
| `PurchaseOrders` | `Purchase/PurchaseOrders/PurchaseOrderConfiguration.cs` | PascalCase | |
| `PurchaseOrderHistory` | `Purchase/PurchaseOrders/PurchaseOrderHistoryConfiguration.cs` | PascalCase | |
| `PurchaseOrderLines` | `Purchase/PurchaseOrders/PurchaseOrderLineConfiguration.cs` | PascalCase | |

---

### Schema: implicit (no `ToTable` schema arg) — 11 tables ⚠️

> EF Core on PostgreSQL with no `HasDefaultSchema` set will use `public` by default, but this is implicit and fragile. Should be made explicit.

#### PascalCase (6)

| Table | EF Config File |
|-------|---------------|
| `ManufactureDifficultySettings` | `Catalog/ManufactureDifficulty/ManufactureDifficultySettingsConfiguration.cs` |
| `UserDashboardSettings` | `Dashboard/UserDashboardSettingsConfiguration.cs` |
| `UserDashboardTiles` | `Dashboard/UserDashboardTileConfiguration.cs` |
| `GridLayouts` | `GridLayouts/GridLayoutConfiguration.cs` |
| `ClassificationHistory` | `InvoiceClassification/ClassificationHistoryConfiguration.cs` |
| `ClassificationRules` | `InvoiceClassification/ClassificationRuleConfiguration.cs` |

#### snake_case (5)

| Table | EF Config File |
|-------|---------------|
| `recurring_job_configurations` | `BackgroundJobs/RecurringJobConfigurationConfiguration.cs` |
| `dqt_runs` | `DataQuality/DqtRunConfiguration.cs` |
| `invoice_dqt_results` | `DataQuality/InvoiceDqtResultConfiguration.cs` |
| `gift_package_manufacture_items` | `Logistics/GiftPackageManufacture/GiftPackageManufactureItemConfiguration.cs` |
| `gift_package_manufacture_logs` | `Logistics/GiftPackageManufacture/GiftPackageManufactureLogConfiguration.cs` |

---

## Issues Detail

### Issue 1 — `dbo` schema on PostgreSQL (HIGH)

**10 tables** explicitly use `schema: "dbo"`. On SQL Server, `dbo` is the default schema. On PostgreSQL, this creates a real separate schema called `dbo` alongside `public`. This means:

- Queries must always include the schema qualifier
- No PostgreSQL tooling treats `dbo` as default
- Inconsistent with the other 26 tables in `public`

**Affected tables:** `BankStatements`, `imported_marketing_transactions`, `IssuedInvoice`, `IssuedInvoiceSyncData`, `KnowledgeBaseChunks`, `KnowledgeBaseDocuments`, `KnowledgeBaseQuestionLogs`, `StockTakingResults`, `Jobs`, `ScheduledTask`

**Fix:** Migrate all `dbo` tables to `public` schema via EF migration (`ALTER TABLE dbo.X SET SCHEMA public`).

---

### Issue 2 — Inconsistent naming convention (HIGH)

**31 tables** use `PascalCase`, **5 tables** use `snake_case`. The snake_case tables are all recent additions (DQT, GiftPackage, RecurringJobs). PostgreSQL convention strongly favors `snake_case`; the PascalCase tables work but require quoted identifiers in raw SQL.

**Snake_case tables:** `recurring_job_configurations`, `dqt_runs`, `invoice_dqt_results`, `gift_package_manufacture_items`, `gift_package_manufacture_logs`

**Decision needed:** Pick one convention and apply it. Options:
- **Option A:** Keep PascalCase (existing majority) — rename the 5 snake_case tables.
- **Option B:** Migrate to snake_case (PostgreSQL idiomatic) — rename 31 PascalCase tables. Higher effort, better long-term.

---

### Issue 3 — Tables with no explicit schema (MEDIUM)

**11 tables** call `ToTable("Name")` without a schema argument. EF Core resolves this to the default schema, which is `public` on PostgreSQL if `HasDefaultSchema` is not set on the context. This works but is fragile — adding `HasDefaultSchema("dbo")` to the context would silently move all of them.

**Fix:** Add `"public"` as the second argument to all 11 `ToTable()` calls. One-line change per file, no migration needed.

---

### Issue 4 — Plural vs singular table names (MEDIUM)

Most tables use plural form (`ManufactureOrders`, `KnowledgeBaseChunks`) but 5 tables use singular:

| Singular Table | Schema |
|---------------|--------|
| `IssuedInvoice` | dbo |
| `TransportBox` | public |
| `TransportBoxItem` | public |
| `TransportBoxStateLog` | public |
| `ScheduledTask` | dbo |
| `Jobs` | dbo |

**Fix:** Rename to plural form when migrating schemas (combine with Issue 1 migration).

---

### Issue 5 — Entity-table name mismatch (LOW)

`StockTakingConfiguration.cs` maps the entity class `StockTakingRecord` to table `StockTakingResults`. The table name doesn't reflect the domain concept.

**Config:** `Logistics/StockTaking/StockTakingConfiguration.cs`  
**Entity:** `StockTakingRecord`  
**Table:** `StockTakingResults` (in `dbo`)

---

### Issue 6 — Duplicate table mappings (MEDIUM)

`IssuedInvoice` and `IssuedInvoiceSyncData` are configured in **two places**:
- `Invoices/IssuedInvoiceConfiguration.cs` and `Invoices/IssuedInvoiceSyncDataConfiguration.cs` (standard `IEntityTypeConfiguration<T>`)
- `Mapping/IssuedInvoiceDbMapper.cs` (old mapper pattern calling `modelBuilder.Entity<T>()` directly)

This creates dual registration. EF Core will apply both, which is silently additive but confusing and error-prone.

**Fix:** Delete `Mapping/IssuedInvoiceDbMapper.cs` entirely. Keep the `IEntityTypeConfiguration<T>` files.

Similarly `imported_marketing_transactions` only has a configuration file — no duplicate — but is in the `Features/MarketingInvoices/` path while all other features use the domain-organized path. Minor inconsistency.

---

### Issue 7 — Legacy DbMapper pattern (LOW)

`Mapping/RecurringJobsDbMapper.cs` and `Mapping/ScheduledTaskDbMapper.cs` use an old ad-hoc mapper pattern (direct `modelBuilder.Entity<>()` calls) instead of the standard `IEntityTypeConfiguration<T>` used by all other tables.

**Fix:** Convert to `IEntityTypeConfiguration<T>` and move to the appropriate feature folder. Or accept as legacy if these tables are framework-managed (e.g., Hangfire).

---

## Recommended Consolidation Phases

### Phase 1 — Schema: make implicit explicit (30 min, zero migration risk)
Add `"public"` to the 11 `ToTable()` calls that omit the schema argument.  
Files: 6 PascalCase + 5 snake_case configs listed in the implicit section above.  
No DB migration needed — no change to actual schema.

### Phase 2 — Remove duplicate mapping (15 min)
Delete `Mapping/IssuedInvoiceDbMapper.cs`. The `IEntityTypeConfiguration<T>` files already handle the full mapping.  
No DB migration needed.

### Phase 3 — Migrate `dbo` tables to `public` (1–2 hours)
Write a SQL migration or EF migration to `ALTER TABLE dbo.X SET SCHEMA public` for all 10 `dbo` tables.  
Update the 10 configuration files: change `"dbo"` → `"public"`.  
Update `Mapping/RecurringJobsDbMapper.cs` and `Mapping/ScheduledTaskDbMapper.cs` schemas.  
Risk: medium — requires coordinated DB migration in all environments.

### Phase 4 — Standardize table naming (2–8 hours depending on choice)
Pick a convention (PascalCase or snake_case) and rename tables via migration.  
Also fix singular → plural for the 5/6 singular tables.  
Risk: medium — rename migrations required; raw SQL in any external tools must be updated.

### Phase 5 — Fix entity-table mismatch (LOW)
Rename `StockTakingResults` table to `StockTakingRecords` when doing Phase 4.

---

## Notes on `Jobs` and `ScheduledTask` tables

These are mapped via `RecurringJobsDbMapper.cs` and `ScheduledTaskDbMapper.cs` in `dbo` schema. Verify whether these are:
- **Hangfire tables** — if so, schema is controlled by Hangfire config, not EF, and the mappers are just read models. Do not rename.
- **Application tables** — if so, treat like any other table and migrate.
