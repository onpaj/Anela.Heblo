# Implementation Plan: Move MarketingImportResult to Contracts/ (MarketingInvoices)

## Context for every task below

Repo root: `/home/user/worktrees/feature-4144-Arch-Review-Marketinginvoices-Marketingimportresul` (a git worktree of `onpaj/Anela.Heblo`, .NET 8 + React Clean Architecture monorepo). All paths below are relative to this repo root unless stated otherwise. The solution file is `Anela.Heblo.sln` at the repo root.

This is a tiny, backend-only, zero-behavior-change refactor. It moves one internal DTO class (`MarketingImportResult`) from the root of the `MarketingInvoices` feature folder into that feature's existing `Contracts/` subfolder, updates its namespace to match, and removes one now-dead `using` line from a test file. No logic, method signatures, or public API contracts change.

Current (pre-change) state, verified in the repo:

- File `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs` contains exactly:
  ```csharp
  namespace Anela.Heblo.Application.Features.MarketingInvoices;

  public class MarketingImportResult
  {
      public int Imported { get; set; }
      public int Skipped { get; set; }
      public int Failed { get; set; }
  }
  ```
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/` already exists and already contains `IMarketingTransactionSource.cs` and `MarketingTransaction.cs`.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs` and `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` both already start with `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` — this line already exists in both files and must NOT be touched. Neither file needs any edit at all for this change.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` already starts with `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` and `using Anela.Heblo.Application.Features.MarketingInvoices.Services;`. It only ever consumes the import-result value via an implicitly-typed (`var`) local, never naming `MarketingImportResult` in source, so it needs no edit at all.
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` currently begins (lines 1-9):
  ```csharp
  using Anela.Heblo.Application.Features.MarketingInvoices;
  using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
  using Anela.Heblo.Application.Features.MarketingInvoices.Services;
  using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
  using Microsoft.Extensions.Logging.Abstractions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.MarketingInvoices;
  ```
  Line 1 (`using Anela.Heblo.Application.Features.MarketingInvoices;`) exists only to resolve `MarketingImportResult` from the feature-root namespace today. Line 2 (`using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;`) is already present and will resolve the relocated type once the move is done, so no addition is needed — only the removal of line 1.
  The file also contains, further down, one usage: `new MarketingImportResult { Imported = 1, Skipped = 0, Failed = 0 }` (inside a `Setup(...).ReturnsAsync(...)` call) — this line does not change.
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` does not reference `MarketingImportResult` by name anywhere and must NOT be edited.
- A repo-wide search for `MarketingImportResult` across all `*.cs` files returns exactly the 5 files named above (1 definition + 3 production/test consumers + the one test file needing an edit) — there are no other consumers anywhere in the repo.

Full list of files this change touches (nothing else):
1. `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs` → moved (git mv) to `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs`, with its namespace line changed.
2. `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` → one `using` line removed.

Validation commands used below come from this repo's `CLAUDE.md`: for backend changes, run `dotnet build` and `dotnet format`, and run the tests affected by the change (here, `ImportMarketingInvoicesHandlerTests`) rather than assuming full-suite is required.

---

### task: move-marketing-import-result-to-contracts

**Goal:** Relocate `MarketingImportResult.cs` into the `MarketingInvoices` feature's `Contracts/` folder, update its namespace, and remove the now-dead `using` line in the handler test file. This single task covers spec requirements FR-1, FR-2, and FR-3. (FR-4 and FR-5 are verified by the build/test step in the next task — they require no code edits of their own, only confirmation nothing else changed.)

**Context:** See "Context for every task below" above for full background — this task is self-contained using that context; you do not need to re-derive anything, just perform the steps below exactly.

**Step 1 — Move the file with `git mv` (preserves history):**

From the repo root (`/home/user/worktrees/feature-4144-Arch-Review-Marketinginvoices-Marketingimportresul`), run:

```bash
git mv backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs
```

**Verify:** Run `git status --short` and confirm it reports a renamed file, e.g. a line starting with `R  ` referencing both the old and new path. Also confirm with `ls backend/src/Anela.Heblo.Application/Features/MarketingInvoices/` that `MarketingImportResult.cs` is no longer listed there, and `ls backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/` now lists `MarketingImportResult.cs` alongside the existing `IMarketingTransactionSource.cs` and `MarketingTransaction.cs`.

**Step 2 — Update the namespace in the moved file:**

Open `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs`. Its full current content (identical to before the move, since `git mv` does not alter file contents) is:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

Change only the first line, from:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices;
```

to:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
```

The full file content after this edit must be exactly:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

Do not change anything else in the file — the class name, property names, property types, and access modifiers stay byte-for-byte identical.

**Verify:** Run `cat backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs` and confirm the output matches exactly the "full file content after this edit" block above.

**Step 3 — Remove the now-dead `using` line in the test file:**

Open `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`. Its first 9 lines currently read:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices;
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;
```

Delete only the first line, `using Anela.Heblo.Application.Features.MarketingInvoices;`, so the top of the file becomes:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;
```

Do not touch any other line in this file — in particular, leave the existing usage `new MarketingImportResult { Imported = 1, Skipped = 0, Failed = 0 }` further down in the file completely unchanged; it will resolve correctly via the remaining `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` line.

**Verify:** Run `head -n 9 backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` and confirm the output matches exactly the 9-line block above (now starting with the `Contracts` using, no blank line inserted, no other change).

**Step 4 — Confirm no other files were touched:**

Run `git status --short` from the repo root. Confirm the only changes reported are:
- a rename/move entry for `MarketingImportResult.cs` (old path → new path under `Contracts/`)
- a modification to `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`

(There may also be a pre-existing unrelated modification to `artifacts/feat-4144/state.json` from earlier pipeline stages — that is expected and out of scope for this change; do not revert it.)

If any file other than these two appears modified, stop and re-check — this change must not touch `IMarketingInvoiceImportService.cs`, `MarketingInvoiceImportService.cs`, `ImportMarketingInvoicesHandler.cs`, `MarketingInvoiceImportServiceTests.cs`, `MarketingInvoicesModule.cs`, or any other `.cs` file.

---

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
