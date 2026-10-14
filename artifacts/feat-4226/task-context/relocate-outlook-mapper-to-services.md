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

