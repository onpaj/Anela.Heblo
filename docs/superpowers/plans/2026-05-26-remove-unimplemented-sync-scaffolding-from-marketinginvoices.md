# Remove Unimplemented Sync Scaffolding from MarketingInvoices — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the unused `GetUnsyncedAsync` repository method and the unused `IsSynced` / `ErrorMessage` columns on `ImportedMarketingTransaction`, including a reversible EF Core migration that drops both columns.

**Architecture:** Subtractive change across the existing Clean Architecture layers — Domain (interface + entity), Persistence (concrete repo + EF configuration + new migration), Application (one line in the import service). No new abstractions, no DI changes, no API/contract changes, no frontend changes. Existing tests act as the regression harness (NFR-5: no new tests required for dead-code removal).

**Tech Stack:** .NET 8, C#, EF Core 8 (Npgsql), PostgreSQL (schema `public`), xUnit + FluentAssertions for tests.

---

## File Structure

**Files modified (5):**
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` — remove `GetUnsyncedAsync` declaration.
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` — remove `IsSynced` and `ErrorMessage` properties.
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` — remove `GetUnsyncedAsync` body.
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` — remove `IsSynced` and `ErrorMessage` property mappings.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — remove `IsSynced = false` from the entity-construction block.

**Files created (1, auto-generated):**
- `backend/src/Anela.Heblo.Persistence/Migrations/<UTCtimestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<UTCtimestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.Designer.cs` (tool output)

**Files auto-updated:**
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (tool output)

**Files NOT touched:**
- Test files — `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` and `ImportMarketingInvoicesHandlerTests.cs` have **zero** references to `IsSynced`, `ErrorMessage`, or `GetUnsyncedAsync` (verified by grep). They run as-is post-change.

---

## Prerequisites Check

Before starting Task 1, confirm:

- [ ] **PR #1771 is merged to `main`** (touches `IImportedMarketingTransactionRepository.AddAsync` and concrete repo).
- [ ] **PR #1766 is merged to `main`** (touches `MarketingInvoiceImportService.cs` near the entity-construction block).
- [ ] **Current branch is rebased onto post-merge `main`**, with no conflicts in the five files listed above.
- [ ] **Local DB is at current schema head** (`dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`), so the migration tool's diff in Task 7 captures only the intended column drops.

Run to verify prerequisite #4:

```bash
cd backend
dotnet ef migrations list \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: all migrations show as applied. If any show `(Pending)`, run `dotnet ef database update` first.

If any prerequisite is unmet, **stop and resolve before continuing** — generating the migration against a stale snapshot or pre-rebase tree will produce wrong diffs (see "Risks and Mitigations" in arch-review.r1.md).

---

### Task 1: Remove `GetUnsyncedAsync` from the domain interface and concrete repository

Removing only one of the two would leave the build broken (the interface contract would be unmet, or the concrete class would override a non-existent member). Both edits land together.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` (line 7)
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` (lines 26–29)

- [ ] **Step 1: Edit the interface — remove the `GetUnsyncedAsync` line**

Replace the content of `IImportedMarketingTransactionRepository.cs` so the final file reads exactly:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

Edit operation: delete line 7 (`Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);`) entirely. No other changes.

- [ ] **Step 2: Edit the concrete repo — remove the `GetUnsyncedAsync` method body**

In `ImportedMarketingTransactionRepository.cs`, delete the entire method block (lines 26–29):

```csharp
    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct)
    {
        return (await FindAsync(x => !x.IsSynced, ct)).ToList();
    }
```

After deletion, the final file reads exactly:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Repositories;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    public ImportedMarketingTransactionRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct)
    {
        return await AnyAsync(
            x => x.Platform == platform && x.TransactionId == transactionId,
            ct);
    }

    public new async Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
    {
        await base.AddAsync(entity, ct);
    }
}
```

- [ ] **Step 3: Verify build compiles**

Run from the repo root:

```bash
cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded. 0 Error(s)`.

If it fails with "no callers found" for `GetUnsyncedAsync`, that's a stale build cache — rerun `dotnet build` on the whole solution. If it fails with a compilation error pointing at another file, **stop and investigate** — the spec/arch-review assert there are no other callers; an unexpected one would be a finding to report.

- [ ] **Step 4: Verify no remaining references to `GetUnsyncedAsync` in the codebase**

```bash
grep -rn "GetUnsyncedAsync" backend/
```

Expected: zero hits.

---

### Task 2: Remove `IsSynced = false` from `MarketingInvoiceImportService`

Must happen **before** Task 3 (entity property removal) — otherwise the build breaks while `IsSynced` still appears on the right-hand side of the object initializer with no matching property.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` (line 75)

- [ ] **Step 1: Delete the `IsSynced = false,` line inside the entity initializer**

In `MarketingInvoiceImportService.cs`, locate the entity-construction block at lines 67–78:

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

Remove the single line `                    IsSynced = false,`. The block becomes:

```csharp
                var entity = new ImportedMarketingTransaction
                {
                    TransactionId = transaction.TransactionId,
                    Platform = source.Platform,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency,
                    TransactionDate = transaction.TransactionDate,
                    ImportedAt = DateTime.UtcNow,
                    Description = transaction.Description,
                    RawData = transaction.RawData,
                };
```

No other lines change. Match indentation (20 spaces — preserved exactly from the surrounding lines).

- [ ] **Step 2: Verify build still succeeds (entity property still exists at this point)**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Error(s)`.

---

### Task 3: Remove `IsSynced` and `ErrorMessage` from the entity

After this task, the build will be temporarily broken (the EF configuration still maps these properties) until Task 4 lands. That's expected — Tasks 3 and 4 together restore the build.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` (lines 14–15)

- [ ] **Step 1: Delete the two property declarations**

Replace the entity file content so it reads exactly:

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
    public string? Description { get; set; }
    public string? RawData { get; set; }
}
```

Delete lines 14 (`public bool IsSynced { get; set; } = false;`) and 15 (`public string? ErrorMessage { get; set; }`). No other changes.

- [ ] **Step 2: Do NOT build yet — build will fail until Task 4 removes the EF configuration**

Skip the verification step — proceed directly to Task 4.

---

### Task 4: Remove `IsSynced` and `ErrorMessage` mappings from the EF configuration

Pairs with Task 3 — together they restore the build.

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` (lines 47–55)

- [ ] **Step 1: Delete the two property-mapping blocks**

In `ImportedMarketingTransactionConfiguration.cs`, locate and delete this block (lines 47–55):

```csharp
        builder.Property(e => e.IsSynced)
            .IsRequired()
            .HasDefaultValue(false)
            .HasColumnName("IsSynced")
            .HasColumnType("boolean");

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("ErrorMessage")
            .HasColumnType("text");

```

The final file should read exactly:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionConfiguration : IEntityTypeConfiguration<ImportedMarketingTransaction>
{
    public void Configure(EntityTypeBuilder<ImportedMarketingTransaction> builder)
    {
        builder.ToTable("ImportedMarketingTransactions", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("integer")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.TransactionId)
            .IsRequired()
            .HasMaxLength(255)
            .HasColumnName("TransactionId")
            .HasColumnType("character varying(255)");

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("Platform")
            .HasColumnType("character varying(50)");

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasColumnName("Amount")
            .HasColumnType("numeric(18,2)");

        builder.Property(e => e.TransactionDate)
            .IsRequired()
            .HasColumnName("TransactionDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ImportedAt)
            .IsRequired()
            .HasColumnName("ImportedAt")
            .HasColumnType("timestamp without time zone");

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

        builder.HasIndex(e => new { e.Platform, e.TransactionId })
            .IsUnique()
            .HasDatabaseName("IX_ImportedMarketingTransactions_Platform_TransactionId");
    }
}
```

---

### Task 5: Verify the whole solution builds and existing tests still pass

This task validates that Tasks 1–4 together leave the source tree in a fully consistent, compiling, passing state. The migration is generated in Task 7 from this clean baseline.

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

```bash
cd backend && dotnet build
```

Expected: `Build succeeded. 0 Error(s)` across all projects.

If any project fails: the most likely cause is a lingering reference to `IsSynced`, `ErrorMessage`, or `GetUnsyncedAsync` somewhere outside the five files this plan touches. The arch-review's grep was definitive, but if a build error names another file, stop and report it — do not "fix it up" silently.

- [ ] **Step 2: Run the MarketingInvoices test suite**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoices" \
  --no-build
```

Expected: all tests in `MarketingInvoiceImportServiceTests` and `ImportMarketingInvoicesHandlerTests` pass. These tests do not reference the removed members (verified by grep), so they should pass without modification.

- [ ] **Step 3: Verify no remaining references within the MarketingInvoices module**

```bash
grep -rn "IsSynced\|ErrorMessage" backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ \
  backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ \
  backend/src/Anela.Heblo.Application/Features/MarketingInvoices/
```

Expected: zero hits. (Scoped to the module per arch-review spec amendment #5 — searching repo-wide would surface unrelated `ErrorMessage` matches in other modules.)

```bash
grep -rn "GetUnsyncedAsync" backend/
```

Expected: zero hits.

---

### Task 6: Generate the EF Core migration

The migration is generated **after** the entity and configuration edits are complete and the build is clean, so the tool's diff captures only the column drops.

**Files (auto-generated by tool):**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<UTCtimestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<UTCtimestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.Designer.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **Step 1: Run the EF Core migration-add command**

```bash
cd backend && dotnet ef migrations add RemoveUnusedSyncColumnsFromImportedMarketingTransactions \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: tool outputs `Done.` and creates two new files in `src/Anela.Heblo.Persistence/Migrations/` plus updates `ApplicationDbContextModelSnapshot.cs`.

If the tool errors with "the database operation was expected to affect X row(s)" or model-snapshot conflicts, the prerequisite local-DB-at-head check was skipped. Run `dotnet ef database update` first, then retry.

- [ ] **Step 2: Inspect the generated migration's `Up` method**

Open the newly created `<timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs` file. The `Up` method must contain **exactly** these two `DropColumn` operations targeting `schema: "public"` and `table: "ImportedMarketingTransactions"`:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "ErrorMessage",
        schema: "public",
        table: "ImportedMarketingTransactions");

    migrationBuilder.DropColumn(
        name: "IsSynced",
        schema: "public",
        table: "ImportedMarketingTransactions");
}
```

The order of `DropColumn` calls may differ from the above — that's fine, both are independent.

**If `Up` contains additional operations** (column adds, alters in other tables, index changes), the prerequisite "local DB at current schema head" was not met or another developer's unmigrated change is in the snapshot. Stop, investigate, and regenerate after resolving — do not commit a migration with unrelated diffs.

- [ ] **Step 3: Inspect the generated migration's `Down` method**

Confirm `Down` re-adds both columns with the original types (per arch-review spec amendment #4 — types must match the original `AddImportedMarketingTransactions` migration exactly):

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(
        name: "IsSynced",
        schema: "public",
        table: "ImportedMarketingTransactions",
        type: "boolean",
        nullable: false,
        defaultValue: false);

    migrationBuilder.AddColumn<string>(
        name: "ErrorMessage",
        schema: "public",
        table: "ImportedMarketingTransactions",
        type: "text",
        nullable: true);
}
```

The EF tool generates these `AddColumn` calls automatically from the snapshot it's rolling back **to**. The expected output matches the type strings (`boolean`, `text`) and constraints (`nullable: false, defaultValue: false` for `IsSynced`; `nullable: true` for `ErrorMessage`) that match the original configuration. If the generated `Down` differs from the above, hand-edit only the differing lines to match — but it should not differ in a clean snapshot.

- [ ] **Step 4: Verify the migration's UTC timestamp is later than every existing migration**

```bash
ls backend/src/Anela.Heblo.Persistence/Migrations/*.cs | sort | tail -5
```

Expected: the new `<timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs` is the last entry. (Per arch-review spec amendment #3.)

If it isn't last, the local clock is behind another developer's migration timestamp. Delete all three generated artifacts (`.cs`, `.Designer.cs`, snapshot revert via `git checkout`) and regenerate after correcting the clock, or rebase if a newer migration was pulled in.

- [ ] **Step 5: Verify the model snapshot still compiles**

```bash
cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded. 0 Error(s)`.

---

### Task 7: Apply the migration locally to confirm it executes cleanly

This is a local sanity check — the production deploy is manual per project facts and is documented in the PR description (Task 8).

**Files:** none (validation only)

- [ ] **Step 1: Apply the new migration to the local dev database**

```bash
cd backend && dotnet ef database update \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: tool reports `Applying migration '<timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions'. Done.`

- [ ] **Step 2: Verify columns are dropped in the local DB**

If `psql` is available locally:

```bash
psql -h localhost -U postgres -d <dev_db_name> -c "\d public.\"ImportedMarketingTransactions\""
```

Expected: no `IsSynced` and no `ErrorMessage` columns appear in the listing. Remaining columns: `Id`, `TransactionId`, `Platform`, `Amount`, `Currency`, `TransactionDate`, `ImportedAt`, `Description`, `RawData`.

If `psql` is not available, skip this step — the EF tool's success in Step 1 is sufficient.

- [ ] **Step 3: Test the reversibility — apply `Down` then re-apply `Up`**

```bash
cd backend
# Find the migration immediately preceding the new one:
PREV_MIGRATION=$(ls src/Anela.Heblo.Persistence/Migrations/*.cs \
  | grep -v Designer \
  | grep -v ModelSnapshot \
  | grep -v RemoveUnusedSyncColumnsFromImportedMarketingTransactions \
  | sort | tail -1 | xargs basename | sed 's/\.cs$//')
echo "Rolling back to: $PREV_MIGRATION"

# Roll back the new migration
dotnet ef database update "$PREV_MIGRATION" \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API

# Re-apply the new migration
dotnet ef database update \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: rollback succeeds (Down adds columns back), re-apply succeeds (Up drops them again).

If the rollback fails with a column-already-exists or type-mismatch error, the `Down` method's types diverged from the original creation migration. Fix by editing `Down` to match the type strings exactly: `boolean` + `nullable: false, defaultValue: false` for `IsSynced`; `text` + `nullable: true` for `ErrorMessage`.

- [ ] **Step 4: Re-run the test suite against the migrated DB**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoices" \
  --no-build
```

Expected: all tests pass.

---

### Task 8: Final validation — full build, format, full test suite

**Files:** none (validation only)

- [ ] **Step 1: Run `dotnet format`**

```bash
cd backend && dotnet format
```

Expected: no errors. Format changes (if any) get committed alongside the source edits.

- [ ] **Step 2: Run `dotnet build` on the whole solution**

```bash
cd backend && dotnet build
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)` (or whatever the pre-existing warning baseline is — no new warnings).

- [ ] **Step 3: Run the full backend test suite**

```bash
cd backend && dotnet test --no-build
```

Expected: all tests pass. The change is purely subtractive dead-code removal; no test should regress.

- [ ] **Step 4: Final repo-wide grep for absent references**

```bash
grep -rn "GetUnsyncedAsync" backend/ frontend/
```

Expected: zero hits.

```bash
grep -rn "IsSynced\|\.ErrorMessage" backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ \
  backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ \
  backend/src/Anela.Heblo.Application/Features/MarketingInvoices/
```

Expected: zero hits.

---

### Task 9: Commit and prepare PR description

**Files:** none (git only)

- [ ] **Step 1: Review the staged diff**

```bash
git status
git diff
```

Expected files changed (no others):
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs` (new)
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.Designer.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

If any other files appear in the diff, stop and investigate before committing.

- [ ] **Step 2: Stage all changes and commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ \
        backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ \
        backend/src/Anela.Heblo.Persistence/Migrations/ \
        backend/src/Anela.Heblo.Application/Features/MarketingInvoices/

git commit -m "refactor(marketing-invoices): remove unimplemented sync scaffolding

Remove unused GetUnsyncedAsync repository method, IsSynced and ErrorMessage
entity properties, their EF configuration, and add a reversible migration
dropping both columns. The FlexiBee sync workflow these columns were added
for is explicitly deferred to Future Work per epic #609 and would not reuse
this exact schema. Existing tests pass without modification."
```

- [ ] **Step 3: PR description must explicitly document the manual migration step**

When opening the PR, include this section in the description (database migrations are not automated per project facts):

```markdown
## Manual deployment step

This PR adds the migration:

`<timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions`

It must be applied manually to each environment:

```bash
cd backend && dotnet ef database update \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Migration order is safe in either direction:
- **migrate-then-deploy**: old code still works against the new schema (old
  code only wrote `IsSynced = false` and never read either column).
- **deploy-then-migrate**: new code works against the old schema (EF Core
  ignores unmapped DB columns at runtime).

`DROP COLUMN` in PostgreSQL is metadata-only and fast on any table size.
```

---

## Self-Review

After writing the plan, checking against spec.r2.md and arch-review.r1.md:

**Spec coverage:**
- FR-1 (remove `GetUnsyncedAsync` from interface + impl) → Task 1.
- FR-2 (remove `IsSynced` + `ErrorMessage` from entity, remove `IsSynced = false` insert) → Tasks 2 + 3.
- FR-3 (remove EF configuration mappings) → Task 4.
- FR-4 (add migration with working `Up`/`Down`) → Tasks 6 + 7.
- FR-5 (verify no callers depend on entity-level error storage) → Prerequisite grep in Task 5, Step 3 + Task 8, Step 4.
- FR-6 (sequence behind PRs #1771 and #1766) → "Prerequisites Check" block + arch-review's risk mitigations carried into Task 6, Step 4.
- NFR-1/2/3/4/5 (perf, security, reversibility, BC, test coverage) → covered by reversible-`Down` design in Task 6 Step 3, no test changes per NFR-5, schema is `public` per arch-review Decision 2.

**Arch-review amendments applied:**
- Amendment #1 (schema is `public`, not `dbo`) → enforced in Task 6 Step 2 verification + Task 6 Step 3.
- Amendment #2 (regenerate `ApplicationDbContextModelSnapshot.cs`) → tool re-runs in Task 6 Step 1; manual edits forbidden.
- Amendment #3 (timestamp must be latest) → Task 6 Step 4.
- Amendment #4 (`Down` types match original creation migration exactly) → Task 6 Step 3 + Task 7 Step 3 reversibility test.
- Amendment #5 (scope grep to MarketingInvoices module) → Task 5 Step 3 + Task 8 Step 4 scoped greps.

**Placeholder scan:** no TBDs, no "add appropriate X", no "similar to Task N", no missing code blocks. All file paths absolute or relative-from-repo-root with explicit anchors.

**Type consistency:** the interface and entity shapes in Task 1 / Task 3 final-form code blocks match the arch-review's "final form" appendix exactly. No method or property names drift between tasks.

## Status: COMPLETE
