### task: verify-and-commit-marketing-import-result-move

**Goal:** Build the backend, format it, run the specific affected test class to confirm the move compiles and passes, then commit the change. This covers spec requirements FR-4 and FR-5 (verifying no other consumer files needed edits and no behavior changed) and closes out the task.

**Context:** This task assumes Step 1-4 of the `move-marketing-import-result-to-contracts` task have already been completed in this same worktree: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs` has been git-moved to `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs` with its namespace changed to `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`, and the `using Anela.Heblo.Application.Features.MarketingInvoices;` line has been removed from `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`. If you are picking this task up independently, first read those two files to confirm that state matches before proceeding; if it does not, go back and perform the previous task's steps first.

Repo root for all commands: `/home/user/worktrees/feature-4144-Arch-Review-Marketinginvoices-Marketingimportresul`. Solution file: `Anela.Heblo.sln`.

**Step 1 — Build the backend:**

```bash
dotnet build Anela.Heblo.sln
```

**Verify:** The command must exit with code 0 and its output must end with `Build succeeded.` and report `0 Error(s)`. This confirms:
- The moved `MarketingImportResult.cs` compiles under its new namespace and new path.
- `IMarketingInvoiceImportService.cs` and `MarketingInvoiceImportService.cs` (in `Services/`) compile unchanged — their existing `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` already resolves the relocated type, confirming FR-4 for those two files.
- `ImportMarketingInvoicesHandler.cs` compiles unchanged — confirming FR-4 for that file.
- The test project itself builds, meaning `ImportMarketingInvoicesHandlerTests.cs` compiles with the `using` line removed and `new MarketingImportResult { ... }` still resolving via the remaining `Contracts` using — confirming FR-3.

If the build fails, read the compiler error carefully: if it names a missing type or namespace in a file you did not expect to touch, that means an additional consumer exists beyond what this plan accounted for — stop and re-run a repo-wide search (`grep -rn "MarketingImportResult" --include="*.cs" .`) to find it, rather than guessing a fix.

**Step 2 — Run `dotnet format` and confirm it makes no further changes:**

```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

**Verify:** This command must exit with code 0, meaning the codebase is already correctly formatted after the edits (in particular, no unused-using warning should fire since the dead `using` was removed in the previous task, and the moved file's single-line namespace declaration follows the same file-scoped-namespace style already used by its new siblings in `Contracts/`).

If this command instead reports formatting violations, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply the fixes, then run `git diff` and confirm the only changes it introduces are pure formatting/whitespace in the two files this task already touched — if `dotnet format` touches any other file, revert those unrelated changes with `git checkout -- <file>` before proceeding, since this task must not introduce unrelated formatting churn.

**Step 3 — Run the affected test class:**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportMarketingInvoicesHandlerTests"
```

**Verify:** The command must exit with code 0 and its summary line must report all tests in `ImportMarketingInvoicesHandlerTests` passed (0 failed). This directly confirms FR-3's acceptance criterion that the file "still compiles and `new MarketingImportResult { ... }` still resolves via the existing `Contracts` using," and confirms FR-5 (no behavior change) for this handler's test coverage — the test asserts on `Imported`/`Skipped`/`Failed` values produced through the exact same code path as before the move.

**Step 4 — Confirm the diff is exactly the expected shape (FR-5's acceptance criterion):**

```bash
git status --short
git diff --stat
```

**Verify:** `git diff --stat` (plus the rename shown by `git status`) must show changes touching only:
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs` → `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs` (rename, 1 line changed: the namespace)
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` (1 line removed)
- optionally the pre-existing `artifacts/feat-4144/state.json` change from earlier pipeline stages, which is unrelated to this code change and must be left as-is

If anything else appears, stop and investigate before committing — do not commit unrelated changes.

**Step 5 — Stage and commit the change:**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs
git add backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs
git commit -m "$(cat <<'EOF'
Move MarketingImportResult into MarketingInvoices/Contracts

Relocates MarketingImportResult.cs from the MarketingInvoices feature
root into its Contracts/ folder to match the module's complex-feature
filesystem convention, alongside its siblings MarketingTransaction and
IMarketingTransactionSource. Updates the namespace to
Anela.Heblo.Application.Features.MarketingInvoices.Contracts and drops
the now-unused using in ImportMarketingInvoicesHandlerTests. No
behavior, signature, or API contract changes.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Aaocfetes8tGpA9sr39V4q
EOF
)"
```

Note: `git add` on a path that no longer exists on disk (the old `MarketingImportResult.cs` location, already handled by the earlier `git mv`) is a harmless no-op if `git mv` already staged the rename — including both add commands above is safe either way and guarantees the rename is staged correctly regardless of whether `git mv` or manual edits were used.

**Verify:** Run `git log -1 --stat` and confirm the new commit exists, its message matches the one above, and its stat output lists exactly the same rename + one-file-modified shape confirmed in Step 4 (plus the pre-existing `state.json` change only if that was already staged separately and is not part of this commit — do not include it in this commit if it was not already staged before Step 5; if `git status --short` before this step showed `state.json` as a separate unstaged modification, leave it unstaged and do not `git add` it here).

---

## Self-Review

Mapping of spec functional requirements to tasks:

- **FR-1 (Relocate `MarketingImportResult.cs` to `Contracts/`)** — covered by `move-marketing-import-result-to-contracts`, Step 1 (git mv) and its verification (file no longer at old path, exists at new path). Byte-for-byte class body preservation is covered by Step 2's exact before/after file-content blocks (only the namespace line differs).
- **FR-2 (Update the namespace declaration)** — covered by `move-marketing-import-result-to-contracts`, Step 2, with the exact old and new namespace lines given verbatim and the full resulting file content given verbatim for verification.
- **FR-3 (Remove the now-unused `using` in the test file)** — covered by `move-marketing-import-result-to-contracts`, Step 3, with the exact before/after 9-line header given verbatim; further confirmed by `verify-and-commit-marketing-import-result-move` Step 1 (build succeeds) and Step 3 (test passes, confirming `new MarketingImportResult { ... }` still resolves).
- **FR-4 (No `using` changes required in production consumer files)** — covered by `move-marketing-import-result-to-contracts` Step 4 (git status check that only the two expected files changed) and by `verify-and-commit-marketing-import-result-move` Step 1 (a successful full-solution build proves `IMarketingInvoiceImportService.cs`, `MarketingInvoiceImportService.cs`, and `ImportMarketingInvoicesHandler.cs` compile unchanged against the moved/renamespaced type) and Step 4 (diff-shape check confirming those three files show zero diff).
- **FR-5 (No behavior change)** — covered by `verify-and-commit-marketing-import-result-move` Step 3 (existing handler test still passes unmodified in its assertions) and Step 4 (diff limited to exactly the file move + 1-line namespace change + 1-line using removal, per `git diff --stat`).

No placeholders remain in either task: every step names the exact file path, the exact command to run, the exact before/after code content, and an exact, checkable verification output. Both tasks are self-contained — each restates the relevant file paths and code inline rather than referring back to "the context above" for anything load-bearing to execution.
