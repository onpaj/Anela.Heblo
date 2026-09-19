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

