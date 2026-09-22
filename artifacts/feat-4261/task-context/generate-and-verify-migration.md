### task: generate-and-verify-migration

**Generate the EF Core migration that drops the `JobName` column and its unique index, and verify `Down()` is correct**

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.Designer.cs`
- Modify (auto-generated): `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **Step 1: Generate the migration from the updated model**

Run:
```bash
dotnet ef migrations add DropRedundantJobNameColumn \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: Command succeeds and prints something like `Done. To undo this action, use 'ef migrations remove'`, and creates the two new migration files plus updates `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 2: Read the generated `Up()` method and confirm it drops both the column and the index**

Open the newly created `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.cs`. `Up()` must contain both:
```csharp
migrationBuilder.DropIndex(
    name: "IX_RecurringJobConfigurations_JobName",
    schema: "public",
    table: "RecurringJobConfigurations");

migrationBuilder.DropColumn(
    name: "JobName",
    schema: "public",
    table: "RecurringJobConfigurations");
```
(order may vary — EF typically drops the index before the column). If either statement is missing, add it manually in the matching style shown above, matching the schema (`"public"`) and table name (`"RecurringJobConfigurations"`) used by every other migration touching this table (see `backend/src/Anela.Heblo.Persistence/Migrations/20260718074122_AddTimeZoneIdToRecurringJobConfigurations.cs` for the established style).

- [ ] **Step 3: Read the generated `Down()` method and confirm it restores both the column and the unique index**

`Down()` must contain both a `migrationBuilder.AddColumn<string>(name: "JobName", schema: "public", table: "RecurringJobConfigurations", type: "character varying(100)", maxLength: 100, nullable: false, ...)` call and a `migrationBuilder.CreateIndex(name: "IX_RecurringJobConfigurations_JobName", schema: "public", table: "RecurringJobConfigurations", column: "JobName", unique: true)` call. If `unique: true` or the index name is missing from the generated `Down()`, fix it by hand — a `Down()` that restores the column but not its uniqueness silently changes the schema on rollback, which is worse than no rollback at all.

- [ ] **Step 4: Build the solution to confirm the migration compiles and the model snapshot is consistent**

Run: `dotnet build`
Expected: Build succeeds with 0 errors, including the new migration files and the updated `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(background-jobs): add migration dropping redundant JobName column and unique index"
```

---

