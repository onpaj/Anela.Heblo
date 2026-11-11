### task: verify-build-format-and-openapi-client

**Context:** Final validation pass per this repo's standard checklist (`dotnet build` + `dotnet format`, all touched tests passing, OpenAPI client regenerated and diffed). This task runs a full-solution build, applies formatting, re-runs the full FileStorage/Catalog test surface, and regenerates the frontend TypeScript client to confirm FR-4's acceptance criterion — the public HTTP contract and generated client are unaffected by the namespace move.

**Step 1 — full solution build.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with `0 Error(s)` across every project in the solution (production and test).

**Step 2 — apply code formatting.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet format Anela.Heblo.sln
git status
```
Expected: `dotnet format` completes without error. If it rewrites any of the files touched in this feature (e.g. reordering a `using`), `git status` will show them as modified — stage and include them in this task's commit (Step 6). If it reports no changes, proceed with no extra files to stage.

**Step 3 — run the full FileStorage and Catalog job test surface (broader net than the previous task's targeted filter, to catch any indirect regression).**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.FileStorage|FullyQualifiedName~ProductExportDownloadJob"
```
Expected: `Passed!` summary line with `Failed: 0`.

**Step 4 — regenerate the OpenAPI TypeScript client and confirm no shape change.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
git diff --stat frontend/src/api/generated/api-client.ts
git diff frontend/src/api/generated/api-client.ts | grep -i "DownloadFromUrl" || true
```
Expected: either no diff at all in `api-client.ts` (most likely, since NSwag emits bare class/interface names like `DownloadFromUrlRequest`/`DownloadFromUrlResponse` with no embedded C# namespace — confirmed by inspecting the pre-existing generated file), or, if NSwag happens to regenerate unrelated cosmetic ordering/whitespace elsewhere in the file, no `DownloadFromUrl`-related line in the diff should show a route, field, or type-shape change — only, at most, a no-op regeneration. If the diff shows any `DownloadFromUrl` route, field, or shape change, STOP and investigate before proceeding (this would violate spec FR-4/NFR-3, which require the public HTTP contract to be unaffected).

**Step 5 — final full production build to confirm everything still links together.**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with `0 Error(s)`.

**Step 6 — commit any formatting or regenerated-client changes (only if Steps 2 or 4 produced a diff).**

```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
git status
```

If `git status` shows no changes, skip straight to reporting completion — there is nothing to commit for this task.

If `git status` shows changes (e.g. `dotnet format` adjusted formatting, or the OpenAPI regeneration touched `api-client.ts`):
```bash
cd /home/user/worktrees/feature-4232-Arch-Review-Filestorage-Downloadfromurlrequest-Res
git add -A
git status
git commit -m "$(cat <<'EOF'
chore(filestorage): apply formatting and regenerate OpenAPI client

Run dotnet format and regenerate the frontend TypeScript client after
the DownloadFromUrl Contracts/ namespace move, confirming no public
HTTP contract or generated-client shape change.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HNbCvxMMdxF1a9PZZP265T
EOF
)"
```

---

## Requirements coverage check (self-review)

- **FR-1** (create `Contracts/`, move + rename namespace of the two DTOs, handler stays put): `create-filestorage-contracts-folder`, Steps 1–4.
- **FR-2** (update in-module references — handler, validator, module): `create-filestorage-contracts-folder`, Steps 4–6.
- **FR-3** (update Catalog's `ProductExportDownloadJob.cs`): `create-filestorage-contracts-folder`, Step 8.
- **FR-4** (update `FileStorageController.cs`; regenerate/diff the OpenAPI TS client): `create-filestorage-contracts-folder` Step 7, and `verify-build-format-and-openapi-client` Step 4.
- **FR-5** (update the five existing test files, all previously-passing tests still pass): `update-tests-for-contracts-namespace`, Steps 1–8.
- **NFR-1/NFR-2** (no performance/security impact): satisfied by construction — every step is a `using`/namespace-only change with no logic touched; nothing further to implement.
- **NFR-3** (source-breaking but deploy-safe; all in-repo callers covered in the same change set): satisfied — every caller found by the grep audit above is covered by one of the three tasks; no compatibility shim is introduced, per the spec's explicit Out-of-Scope note.
- **Out-of-Scope items** (handler's internal logic, `FileStorageOptions.cs`/`FileDownloadOptions.cs`, `DownloadResilienceService.cs`/`IDownloadResilienceService.cs`, validator rules, controller routes/behavior, HTTP JSON shape, compatibility shim) — none of these are touched by any task above; confirmed by the diffs being `using`/`namespace` lines only.
