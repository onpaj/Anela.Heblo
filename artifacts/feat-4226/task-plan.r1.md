# Plan: Move OutlookEventImportMapper from UseCases to Services

**Goal:** Fix an inverted `Services → UseCases` dependency by relocating `OutlookEventImportMapper.cs` into `Features/Marketing/Services/`, updating its namespace, and removing the now-stale `using` in its consumer. Pure structural move — no behavior, signature, or visibility change (`internal static`, unchanged assembly-wide `InternalsVisibleTo`).

**Architecture / stack:** .NET 8, `Anela.Heblo.Application` project, Vertical Slice under `Features/Marketing/`. No DTOs involved, no API/contract surface touched (class is `internal static`).

---

### task: relocate-outlook-mapper-to-services

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs`

This class is `internal static` with unchanged method signatures (`HasChanges`, `ApplyChanges`, `BuildAction`) — read its current content first; do not guess or reconstruct it from memory.

- [ ] **Step 1: Read the current file**

Read `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` in full so you have its exact current byte content before moving it.

- [ ] **Step 2: Move the file with git, preserving history**

```bash
git mv backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs \
       backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs
```

- [ ] **Step 3: Edit the moved file's namespace and using directives**

In the newly moved `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`, make exactly these two textual changes to the content you read in Step 1 (do not touch anything else — no reformatting, no reordering of members):

1. Change the namespace declaration line from:
   ```csharp
   namespace Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
   ```
   to:
   ```csharp
   namespace Anela.Heblo.Application.Features.Marketing.Services;
   ```

2. Delete the line:
   ```csharp
   using Anela.Heblo.Application.Features.Marketing.Services;
   ```
   This `using` was needed only to reach `SyncActor` in `Services` from the old `UseCases.ImportFromOutlook` namespace. Once the file's own namespace is `...Marketing.Services`, this becomes a same-namespace no-op `using` and must be removed — leaving it in would cause a CS0105 "duplicate using" or unnecessary-using warning depending on project analyzer settings.

Do not change any other `using` lines, the class declaration (`internal static`), or any method body — `HasChanges`, `ApplyChanges`, `BuildAction` keep their exact existing signatures.

- [ ] **Step 4: Verify the old path is gone and the new file compiles in isolation of syntax errors**

Run: `git status`
Expected: shows the file as renamed (`renamed:` old path → new path) with modifications, not as a separate delete + untracked add. If `git mv` in Step 2 was followed immediately by edits, `git status` may show it as a plain rename with modified content — that is correct.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs \
        backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs
git commit -m "$(cat <<'EOF'
Move OutlookEventImportMapper from UseCases to Services

Fixes an inverted Services -> UseCases dependency. Pure structural
move: namespace changed to Anela.Heblo.Application.Features.Marketing.Services,
self-referential using removed. No behavior change.
EOF
)"
```

---

### task: fix-consumer-using-and-validate

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs:8` (remove one `using` line)

This task depends on `relocate-outlook-mapper-to-services` having already been completed (the mapper must already live in `Features/Marketing/Services/` with namespace `Anela.Heblo.Application.Features.Marketing.Services`) — if that task has not run yet, do it first.

- [ ] **Step 1: Read the current file**

Read `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` in full. Confirm line 8 (or wherever it currently sits — line numbers may have shifted slightly since the review) reads:
```csharp
using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
```

- [ ] **Step 2: Remove the stale using line**

Delete that exact line from the file's `using` block. Do not touch anything else in the file — the 3 call sites to `OutlookEventImportMapper.HasChanges` / `.ApplyChanges` / `.BuildAction` (originally at lines 131, 150, 166) need no edit, since the mapper now lives in the same namespace (`Anela.Heblo.Application.Features.Marketing.Services`) as `MarketingCalendarSyncService` itself and is resolved without any `using`.

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds with 0 errors. If it reports `OutlookEventImportMapper` as not found, the relocate task (Step 3, namespace edit) was not completed correctly — verify the moved file's namespace first.

- [ ] **Step 4: Format**

Run: `dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes`
Expected: no formatting violations reported (or if it auto-fixes, re-run `git diff` to confirm only whitespace/using-order changes, nothing semantic).

- [ ] **Step 5: Run the Marketing regression suite**

Run: `dotnet test --filter "FullyQualifiedName~ImportFromOutlookHandlerTests|FullyQualifiedName~MarketingCalendarSyncServiceTests|FullyQualifiedName~OutlookCalendarSyncServiceTests|FullyQualifiedName~OutlookCalendarSyncServiceTokenTests"`
Expected: all tests in `ImportFromOutlookHandlerTests`, `MarketingCalendarSyncServiceTests`, `OutlookCalendarSyncServiceTests`, and `OutlookCalendarSyncServiceTokenTests` pass, 0 failures. No test file changes are expected or required — per spec FR-5, a full-repo grep confirmed no test references the mapper's old namespace.

- [ ] **Step 6: Full solution build sanity check**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, confirming no other project references the old `UseCases.ImportFromOutlook` namespace for this type.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs
git commit -m "$(cat <<'EOF'
Remove stale using after OutlookEventImportMapper relocation

MarketingCalendarSyncService no longer needs to import
Features.Marketing.UseCases.ImportFromOutlook now that
OutlookEventImportMapper lives in Features.Marketing.Services.
EOF
)"
```

---

**Self-review against spec:**
- FR-1 (move file) → covered by `relocate-outlook-mapper-to-services` Step 2.
- FR-2 (namespace change, signatures/visibility unchanged) → covered by Step 3 of that task.
- FR-3 (remove stale `using` in `MarketingCalendarSyncService.cs`, 3 call sites untouched) → covered by `fix-consumer-using-and-validate` Steps 1–2.
- FR-4 (`ImportFromOutlookHandler.cs` and all other files unchanged) → no task touches it; only the two named files are modified anywhere in this plan.
- FR-5 (no test file changes, regression suite passes) → covered by Step 5 of the second task; no test file is created, deleted, or modified by either task.
