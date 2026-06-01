# Architecture Review: Remove Unimplemented Sync Scaffolding from MarketingInvoices

## Skip Design: true

Pure backend cleanup — interface method removal, two entity property removals, EF mapping cleanup, and a column-drop migration. No new or changed UI components, screens, layouts, or visual decisions.

## Architectural Fit Assessment

This change is strictly subtractive and aligns perfectly with the existing Clean Architecture layering: `Domain → Persistence → Application` (no inverse coupling). The grep performed during exploration confirms the spec's claim — `GetUnsyncedAsync`, `IsSynced`, and `ErrorMessage` are referenced **only** in:

- `Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:7` (interface decl)
- `Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs:14-15` (entity props)
- `Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs:26-28` (concrete impl)
- `Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs:47-55` (EF mapping)
- `Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs:75` (one assignment to `false`)

The existing test suite (`MarketingInvoiceImportServiceTests.cs`) never references either property or `GetUnsyncedAsync`, so test stability is guaranteed.

The integration with the codebase's broader patterns is clean:
- `IssuedInvoice` (the eventually-richer sync precedent) lives in a different module (`Features/Invoices/`) and uses a separate sync-history persistence model. Nothing about that module touches what's being removed here.
- The schema lives under `public` (per `ImportedMarketingTransactionConfiguration.cs:11`) — **not `dbo`** as the older 2026-04-14 migration suggests. The PascalCase plural table name (`ImportedMarketingTransactions`) is correct per the `StandardizeTableNamingToPascalCase` / `RenameSingularTablesToPluralForm` migrations.

## Proposed Architecture

### Component Overview

```
Domain layer
└── IImportedMarketingTransactionRepository           [−1 method: GetUnsyncedAsync]
└── ImportedMarketingTransaction (entity)             [−2 props: IsSynced, ErrorMessage]

Persistence layer
└── ImportedMarketingTransactionRepository            [−1 method body: GetUnsyncedAsync]
└── ImportedMarketingTransactionConfiguration         [−2 property mappings]
└── Migrations/
    └── <timestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs   [NEW]

Application layer
└── MarketingInvoiceImportService                     [−1 line: IsSynced = false]

Test layer
└── MarketingInvoiceImportServiceTests                [unchanged — no refs]
└── ImportMarketingInvoicesHandlerTests               [unchanged — verify no refs]
```

No new files except the migration. No file moves. No DI registration changes (the repository registration in `MarketingInvoicesModule` is interface-based and unaffected).

### Key Design Decisions

#### Decision 1: Keep the migration reversible (`Down` rebuilds the columns)
**Options considered:**
- (a) Drop-only migration with empty `Down` (matches "we'll never reuse this schema").
- (b) Drop with a working `Down` that re-adds `IsSynced bool NOT NULL DEFAULT false` and `ErrorMessage text NULL`.

**Chosen approach:** (b).

**Rationale:** The repo convention (see `20260525152436_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs`) is that every migration has a working `Down`. Migrations are applied manually per environment, so a malformed `Down` can corner a deploy. Even though the spec notes the FlexiBee sync is unlikely to revive these exact columns, the cost of a 4-line `Down` is trivial and preserves the rollback contract.

#### Decision 2: Place the migration on the `public` schema, not `dbo`
**Options considered:**
- (a) Use `schema: "dbo"` to match the original 2026-04-14 creation migration.
- (b) Use `schema: "public"` to match the entity configuration's `builder.ToTable("ImportedMarketingTransactions", "public")` and the `MoveDboTablesToPublicSchema` migration from 2026-04-24.

**Chosen approach:** (b).

**Rationale:** The table has already been moved to `public` and renamed to `ImportedMarketingTransactions` (PascalCase plural) by the 2026-04-24 standardization migrations. Targeting `dbo` would fail at runtime against the current schema head. The most recent migration touching this table (`20260525152436_AddCurrencyDescription...`) targets `public.ImportedMarketingTransactions` — follow that precedent exactly.

#### Decision 3: Remove `IsSynced = false` from the service insert path in the same PR as the entity change
**Options considered:**
- (a) Remove the property first (PR-A), then the service-side assignment (PR-B).
- (b) Single PR: remove property, mapping, interface method, **and** the `IsSynced = false` line together.

**Chosen approach:** (b).

**Rationale:** Removing the property without removing its assignment site would leave the codebase in a non-compiling state mid-PR. The change must be atomic. The spec already groups these under FR-1 through FR-4 within a single deliverable.

#### Decision 4: Do not introduce a deprecation/obsolete phase
**Options considered:**
- (a) Mark properties `[Obsolete]` first, ship, then remove.
- (b) Remove directly.

**Chosen approach:** (b).

**Rationale:** Zero external consumers (confirmed by repo-wide grep). The OpenAPI surface does not expose `ImportedMarketingTransaction` directly — only `MarketingImportResult` is returned by `ImportMarketingInvoicesHandler`. There is no API client, frontend, or external integration relying on these fields. A deprecation phase adds ceremony with no protective value.

## Implementation Guidance

### Directory / Module Structure

All edits land in their existing files. Only one new file:

```
backend/src/Anela.Heblo.Persistence/Migrations/
    <UTCtimestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.cs
    <UTCtimestamp>_RemoveUnusedSyncColumnsFromImportedMarketingTransactions.Designer.cs   (auto-generated)
ApplicationDbContextModelSnapshot.cs                                                     (auto-updated)
```

The migration **must** be generated via `dotnet ef migrations add RemoveUnusedSyncColumnsFromImportedMarketingTransactions --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`. Do not hand-write the `.Designer.cs` or model snapshot — let the tool produce them after the entity + configuration edits land.

### Interfaces and Contracts

**`IImportedMarketingTransactionRepository`** — final form after change:

```csharp
public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

**`ImportedMarketingTransaction`** — final form after change (business fields only, lines 14–15 deleted):

```csharp
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

**Migration `Up` / `Down` skeleton** — model on `20260525152436_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs`:

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

### Data Flow

The change deletes a never-executed branch of the conceptual flow; no flow needs updating:

```
Active flow (unchanged):
IMarketingTransactionSource → MarketingInvoiceImportService.ImportAsync
    → ExistsAsync (per-tx dedupe) → AddAsync (staged) → SaveChangesAsync (one flush)
    → MarketingImportResult { Imported, Skipped, Failed }

Dead flow (removed):
GetUnsyncedAsync → (no caller) → [future FlexiBee sync, never built]
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Merge order conflict with #1771 (touches same interface) and #1766 (touches same service file) | Medium | Rebase onto `main` after both merge before generating the migration. Generating the migration last guarantees the model snapshot matches schema head. Spec FR-6 already captures this. |
| Migration generated against stale `ApplicationDbContextModelSnapshot.cs` | Medium | Run `dotnet ef migrations add` **after** rebase and **after** entity/configuration edits, so the snapshot diff captures only the column drops. Inspect the generated migration to confirm it contains only the two `DropColumn` operations on `public.ImportedMarketingTransactions`. |
| Production deploy ships code that expects columns to be gone before the migration runs (or vice versa) | High | Migrations are applied manually per project facts. The PR description must explicitly call out the migration name and state: "apply migration before deploying this build" (column drops are backward-compatible with the old build, since the old code only wrote `IsSynced = false` and never read either column — but the new build will fail at startup model validation if the columns still exist? **No — EF Core does not fail startup if a DB column has no model mapping**; it ignores extra columns. So either order is safe. State this in the PR description.) |
| Migration locks `ImportedMarketingTransactions` table for too long on production | Low | PostgreSQL `DROP COLUMN` is metadata-only and fast even on large tables — it does not rewrite rows. Acceptable as-is. Spec NFR-1 already flags this. |
| Designer file / model snapshot drift if edits are partial | Medium | Do not hand-edit `.Designer.cs` or `ApplicationDbContextModelSnapshot.cs`. Always re-run `dotnet ef migrations add` after entity changes. If the generated migration looks wrong, delete it (all three files: `.cs`, `.Designer.cs`, snapshot revert) and regenerate. |
| Hidden caller via reflection / dynamic resolution | Very Low | The grep across `backend/` returned exactly the five expected files. No reflection-based access patterns exist for these properties (they are not on any DTO contract). No mitigation needed beyond the grep already performed. |

## Specification Amendments

1. **Amend FR-3 / FR-4 to specify the schema as `public`, not `dbo`.** The spec currently says "drops both columns" without specifying the schema. The 2026-04-24 `MoveDboTablesToPublicSchema` migration relocated this table; the configuration confirms `public`. Hand-rolled migration code targeting `dbo` will fail.

2. **Amend FR-4 acceptance criteria to explicitly require regenerating `ApplicationDbContextModelSnapshot.cs`** (it is part of the migration tool output; manual edits to the entity/configuration without re-running `dotnet ef migrations add` will leave the snapshot drifted, breaking the next migration generation in any module).

3. **Add to FR-6:** when rebasing, verify the generated migration filename's UTC timestamp is later than every migration on `main` after rebase — otherwise EF Core may apply migrations out of order on `Update-Database`.

4. **Strengthen NFR-3:** the `Down` migration must reproduce the original column types **exactly** — `boolean NOT NULL DEFAULT false` and `text NULL`. Inspect the regenerated `Down` against the 2026-04-14 `AddImportedMarketingTransactions` migration to confirm the type strings match.

5. **Clarify in FR-2 acceptance criteria:** "repo-wide search for `IsSynced` and `ErrorMessage` in the MarketingInvoices module" — note that `ErrorMessage` is a common identifier and will appear in unrelated code (e.g., exception handling, other modules). Scope the search to `**/MarketingInvoices/**` to avoid false positives.

## Prerequisites

1. **PR #1771 merged to `main`** — modifies `IImportedMarketingTransactionRepository.AddAsync` and the concrete repository (same files this change edits).
2. **PR #1766 merged to `main`** — touches `MarketingInvoiceImportService.cs` near the entity-construction block where `IsSynced = false` lives.
3. **This branch rebased onto post-merge `main`** before generating the migration, so:
   - Migration timestamp orders correctly relative to any other migrations that landed.
   - `ApplicationDbContextModelSnapshot.cs` baseline reflects the latest schema.
   - No merge conflicts in the five files this change touches.
4. **Local DB at current schema head** when running `dotnet ef migrations add`, so the tool's diff captures only the intended column drops. If the local DB is behind, the generated migration may include unrelated diffs.
5. **No infrastructure or config changes required.** No new packages, no DI changes, no env vars, no feature flags.
6. **Manual deploy step documented in PR description**: list the migration filename and confirm it must be run via `dotnet ef database update` on each environment after the build ships (per project fact: migrations are not automated). Both orderings (migrate-then-deploy or deploy-then-migrate) are safe in this case because EF Core ignores unmapped DB columns at runtime and the old code never read the columns.