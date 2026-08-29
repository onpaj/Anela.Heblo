### task: add-tasksextractiondegraded-domain-and-migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` (line 16)
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` (after line 68)
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddTasksExtractionDegraded.cs`
  (and its auto-generated `.Designer.cs`, plus an update to `ApplicationDbContextModelSnapshot.cs`)

Reference files read to produce this task (do not modify):
- `MeetingTranscript.cs` — confirmed current shape (`Participants` list property at line 16,
  `AccessLevel` at line 17).
- `MeetingTranscriptConfiguration.cs` — confirmed the `Participants` property configuration ends
  at line 68 (`.Metadata.SetValueComparer(ParticipantsComparer);`) and `AccessLevel`'s
  configuration begins at line 70 with `.HasDefaultValue(MeetingAccessLevel.Private)` as the
  precedent for declaring a Fluent-API default.
- `20260714103910_AddMeetingParticipants.cs` — precedent for a single-column migration on this
  same `MeetingTranscripts` table (`jsonb`/`string` shape, not directly reusable for a bool but
  confirms the file/namespace/`#nullable disable` boilerplate).
- `20250901084258_AddInvoiceAcquiredToPurchaseOrder.cs` — exact precedent for a single
  `bool` column addition:
  ```csharp
  migrationBuilder.AddColumn<bool>(
      name: "InvoiceAcquired",
      schema: "public",
      table: "PurchaseOrders",
      type: "boolean",
      nullable: false,
      defaultValue: false);
  ```
- `ApplicationDbContextModelSnapshot.cs` lines 2866-2935 — confirmed the exact current
  `MeetingTranscript` entity block (property list order: `Id`, `AccessLevel`, `Participants`,
  `PlaudCreatedAt`, `PlaudRecordingId`, `RawTranscript`, `ReceivedAt`, `ReviewedAt`,
  `ReviewedByUser`, `Status`, `Subject`, `Summary`, then `HasKey`/indexes/`ToTable`) and, at lines
  3554-3555, confirmed that a non-nullable `bool` property with no Fluent-API default renders in
  the snapshot as `b.Property<bool>("InvoiceAcquired").HasColumnType("boolean");` (no
  `.IsRequired()`, since value types are non-nullable by default) — whereas `AccessLevel` (which
  *does* declare `.HasDefaultValue(...)` in its configuration) renders with
  `.ValueGeneratedOnAdd()` and `.HasDefaultValue(...)` in the snapshot. Since this task's new
  property *will* declare `.HasDefaultValue(false)`, the auto-generated snapshot entry is expected
  to be:
  ```csharp
  b.Property<bool>("TasksExtractionDegraded")
      .ValueGeneratedOnAdd()
      .HasColumnType("boolean")
      .HasDefaultValue(false);
  ```
  inserted alphabetically after `Summary` (line 2917) and before `HasKey("Id")` (line 2919).
- `ls backend/src/Anela.Heblo.Persistence/Migrations` — confirmed the latest existing migration
  timestamp is `20260810105649` (`AddOvertimeLedger`), so the new migration's EF-tool-generated
  timestamp (today, 2026-08-29) sorts after it with no manual timestamp collision risk.

This is a schema-migration task — there is no meaningful unit test for an EF Core migration file
itself (it is verified by generating it against the updated entity/configuration and diffing the
result against the expected shape below), so this task explicitly skips TDD.

Steps:

- [ ] **Step 1: Add the property to the domain entity.**
  Edit `MeetingTranscript.cs` — insert after line 16 (`public List<string> Participants { get; set; } = new();`):
  ```csharp
      public bool TasksExtractionDegraded { get; set; }
  ```

- [ ] **Step 2: Add the Fluent API configuration.**
  Edit `MeetingTranscriptConfiguration.cs` — insert after line 68
  (`.Metadata.SetValueComparer(ParticipantsComparer);`) and before line 70
  (`builder.Property(x => x.AccessLevel)`):
  ```csharp
          builder.Property(x => x.TasksExtractionDegraded)
              .IsRequired()
              .HasDefaultValue(false);
  ```

- [ ] **Step 3: Generate the migration via the EF Core CLI.**
  ```bash
  dotnet ef migrations add AddTasksExtractionDegraded --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
  ```
  This scaffolds `{timestamp}_AddTasksExtractionDegraded.cs`, its `.Designer.cs`, and updates
  `ApplicationDbContextModelSnapshot.cs` automatically.

- [ ] **Step 4: Verify the generated migration's `Up`/`Down` exactly matches the expected shape.**
  Open the newly generated `{timestamp}_AddTasksExtractionDegraded.cs` and confirm it reads:
  ```csharp
  using Microsoft.EntityFrameworkCore.Migrations;

  #nullable disable

  namespace Anela.Heblo.Persistence.Migrations
  {
      /// <inheritdoc />
      public partial class AddTasksExtractionDegraded : Migration
      {
          /// <inheritdoc />
          protected override void Up(MigrationBuilder migrationBuilder)
          {
              migrationBuilder.AddColumn<bool>(
                  name: "TasksExtractionDegraded",
                  schema: "public",
                  table: "MeetingTranscripts",
                  type: "boolean",
                  nullable: false,
                  defaultValue: false);
          }

          /// <inheritdoc />
          protected override void Down(MigrationBuilder migrationBuilder)
          {
              migrationBuilder.DropColumn(
                  name: "TasksExtractionDegraded",
                  schema: "public",
                  table: "MeetingTranscripts");
          }
      }
  }
  ```
  If the tool produces a different shape (e.g. missing `defaultValue: false`, or a different
  `schema`/`table`), hand-correct the file to match the above exactly before proceeding — this is
  the contractually expected shape per the `AddInvoiceAcquiredToPurchaseOrder` precedent and the
  Data Model section of the spec (`nullable: false, defaultValue: false`, no backfill of existing
  rows beyond the column default).

- [ ] **Step 5: Verify the model snapshot update.**
  In `ApplicationDbContextModelSnapshot.cs`, confirm a new property block was inserted into the
  `MeetingTranscript` entity (alphabetically after `Summary`, before `HasKey("Id")`):
  ```csharp
                      b.Property<bool>("TasksExtractionDegraded")
                          .ValueGeneratedOnAdd()
                          .HasColumnType("boolean")
                          .HasDefaultValue(false);
  ```

- [ ] **Step 6: Build.**
  ```bash
  dotnet build
  ```

- [ ] **Step 7: Apply the migration to the local database (manual step per project convention —
  not part of automated deployment; staging/production application happens separately after this
  PR merges).**
  ```bash
  dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
  ```

- [ ] **Step 8: Format.**
  ```bash
  dotnet format
  ```

- [ ] **Step 9: Commit.**
  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs \
          backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs \
          backend/src/Anela.Heblo.Persistence/Migrations/
  git commit -m "Add TasksExtractionDegraded column to MeetingTranscripts"
  ```

---
