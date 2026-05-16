# Relocate ChangeTransportBoxState Request/Response Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `ChangeTransportBoxStateRequest.cs` and `ChangeTransportBoxStateResponse.cs` from `Features/Logistics/UseCases/` into the `ChangeTransportBoxState/` subfolder (matching every other use case in the module), update the namespace declaration in both files, and fix `using` directives in the four consuming files so the solution still builds and tests still pass.

**Architecture:** Pure structural refactor. No logic, contract, serialization, MediatR, DI, OpenAPI, or HTTP-surface changes. Use `git mv` to preserve history. The handler at `UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` already lives in the target namespace and is intentionally left untouched — C# resolves the relocated DTOs implicitly via the shared namespace.

**Tech Stack:** .NET 8, C# 12 file-scoped namespaces, MediatR, xUnit, FluentAssertions, NSwag (OpenAPI generator), `dotnet format`.

---

## File Structure

**Moved (file body untouched except the `namespace` line — one-line edit):**

| From | To |
|------|----|
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateRequest.cs` | `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs` |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateResponse.cs` | `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs` |

**Edited (using-directive only):**

| File | Edit |
|------|------|
| `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` | Replace bare `UseCases` using with `UseCases.ChangeTransportBoxState` using. |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs` | Remove bare `UseCases` using (sub-namespace already imported). |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` | Remove bare `UseCases` using (sub-namespace already imported). |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` | Remove bare `UseCases` using (sub-namespace already imported). |

**Untouched (handler already in target namespace):**

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

---

## Task 1: Verify baseline (clean build + tests pass before any change)

**Why:** Confirms the worktree starts in a known-good state so any later failure is attributable to this refactor.

**Files:** None edited.

- [ ] **Step 1: Build the backend solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded` with `0 Error(s)`. Note the warning count — it must not increase after the refactor.

- [ ] **Step 2: Run the test project that covers these DTOs**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: All tests pass (no failures, no skips related to ChangeTransportBoxState).

- [ ] **Step 3: Confirm `dotnet format` is clean on the baseline**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code `0` with no output. (If non-zero, stop and report — refactor must not start from a dirty formatting state.)

- [ ] **Step 4: Confirm working tree is clean**

Run:

```bash
git status --short
```

Expected: empty output. No commit at this step — this task only validates the baseline.

---

## Task 2: Move `ChangeTransportBoxStateRequest.cs` with `git mv`

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateRequest.cs` → `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs`

- [ ] **Step 1: Move the file with `git mv`**

Run:

```bash
git mv \
  backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateRequest.cs \
  backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs
```

Expected: no output, exit code `0`.

- [ ] **Step 2: Verify the move is staged as a rename**

Run:

```bash
git status
```

Expected: under "Changes to be committed", a single line `renamed: ...UseCases/ChangeTransportBoxStateRequest.cs -> ...UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs`. No "deleted" + "new file" pair.

- [ ] **Step 3: Update the namespace in the moved file**

The file currently reads (line 4):

```csharp
namespace Anela.Heblo.Application.Features.Logistics.UseCases;
```

Edit `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs` to:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
```

Leave every other line (using directives, class declaration, properties) byte-identical.

The full expected file content after the edit:

```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ChangeTransportBoxStateRequest : IRequest<ChangeTransportBoxStateResponse>
{
    public int BoxId { get; set; }
    public TransportBoxState NewState { get; set; }
    public string? Description { get; set; }
    public string? BoxCode { get; set; }
    public string? Location { get; set; }
}
```

- [ ] **Step 4: Verify the diff is minimal (rename + one-line namespace edit)**

Run:

```bash
git diff --staged --stat
```

Expected: one renamed file with a tiny diff size. Then:

```bash
git diff --staged backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs
```

Expected: a single hunk changing the namespace from `...UseCases;` to `...UseCases.ChangeTransportBoxState;`. Nothing else.

- [ ] **Step 5: Do NOT build yet** — the controller still has the old `using` and would fail. Proceed straight to Task 3 to keep the staged changeset small but cohesive.

No commit in this task — Task 2 + Task 3 are committed together at the end of Task 3 since the build is only green when both moves and their namespace updates are done.

---

## Task 3: Move `ChangeTransportBoxStateResponse.cs` with `git mv`

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateResponse.cs` → `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs`

- [ ] **Step 1: Move the file with `git mv`**

Run:

```bash
git mv \
  backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxStateResponse.cs \
  backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs
```

Expected: no output, exit code `0`.

- [ ] **Step 2: Verify the move is staged as a rename**

Run:

```bash
git status
```

Expected: a second `renamed: ...` line for the response file, in addition to the request rename from Task 2.

- [ ] **Step 3: Update the namespace in the moved file**

Edit `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs` so its line-4 namespace becomes the sub-namespace.

The full expected file content after the edit:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ChangeTransportBoxStateResponse : BaseResponse
{
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}
```

Every line other than the namespace declaration must be byte-identical to the original.

- [ ] **Step 4: Verify the response-file diff is minimal**

Run:

```bash
git diff --staged backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs
```

Expected: a single hunk changing only the namespace line.

- [ ] **Step 5: Confirm no stray files remain at the `UseCases/` root**

Run:

```bash
ls backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/
```

Expected: only subfolders (`AddItemToBox`, `ChangeTransportBoxState`, `CreateNewTransportBox`, `GetTransportBoxByCode`, `GetTransportBoxById`, `GetTransportBoxSummary`, `GetTransportBoxes`, `GiftPackageManufacture`, `OpenOrResumeBoxByCode`, `RemoveItemFromBox`, `UpdateTransportBoxDescription`). No `.cs` files at the top level.

- [ ] **Step 6: Confirm both files now sit inside the use case subfolder**

Run:

```bash
ls backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/
```

Expected exactly three files:

```
ChangeTransportBoxStateHandler.cs
ChangeTransportBoxStateRequest.cs
ChangeTransportBoxStateResponse.cs
```

- [ ] **Step 7: Do NOT build or commit yet** — `TransportBoxController.cs` still imports the old namespace and will compile-error against the moved types. Proceed to Task 4.

---

## Task 4: Update `TransportBoxController.cs` `using` directive

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs:6`

- [ ] **Step 1: Replace the bare-`UseCases` `using` with the sub-namespace `using`**

Current line 6:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases;
```

After the edit, line 6 must read:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
```

This single edit:
- Removes the bare-`UseCases` import (which was only present for `ChangeTransportBoxStateRequest`/`Response`).
- Adds the new sub-namespace import.
- Keeps the using list in roughly alphabetical order — `...UseCases.ChangeTransportBoxState` sorts before `...UseCases.CreateNewTransportBox` (already on the next line) and after no other `UseCases.*` import, so just replacing line 6 keeps order correct.

Expected `using` block (lines 1–15) after the edit:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;
using Anela.Heblo.Application.Features.Logistics.UseCases.CreateNewTransportBox;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxes;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxSummary;
using Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;
using Anela.Heblo.Application.Features.Logistics.UseCases.UpdateTransportBoxDescription;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
```

Note: the existing file does not strictly sort all using directives alphabetically (e.g. `AddItemToBox` appears after `ChangeTransportBoxState` would in alphabetical order), but we are matching the file's existing minimal-edit convention — replace line 6 in place, do not reorder the rest.

- [ ] **Step 2: Build the backend solution to confirm controller resolves the DTOs**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded` with `0 Error(s)`. Warning count must equal the baseline from Task 1 Step 1. If any `CS0246` or `CS0234` error mentions `ChangeTransportBoxState`, the namespace or `using` change is wrong — fix before moving on.

- [ ] **Step 3: Do NOT commit yet** — three test files still carry orphan `using` directives that `dotnet format --verify-no-changes` will flag. Proceed to Task 5.

---

## Task 5: Remove orphan `using` directive from `TransportBoxControllerTests.cs`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs:2`

- [ ] **Step 1: Delete the bare-`UseCases` `using`**

Current lines 1–5:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
using Anela.Heblo.Application.Shared;
```

After the edit, line 2 (`using Anela.Heblo.Application.Features.Logistics.UseCases;`) is removed entirely. The remaining `using` block must be:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
using Anela.Heblo.Application.Shared;
```

No other line in the file changes.

- [ ] **Step 2: Verify the diff is exactly one deleted line**

Run:

```bash
git diff backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs
```

Expected: a single hunk with one `-` line (`using Anela.Heblo.Application.Features.Logistics.UseCases;`) and no `+` lines.

---

## Task 6: Remove orphan `using` directive from `ChangeTransportBoxStateHandlerTests.cs`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs:3`

- [ ] **Step 1: Delete the bare-`UseCases` `using`**

Current lines 1–6:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
```

After the edit, line 3 (`using Anela.Heblo.Application.Features.Logistics.UseCases;`) is removed entirely. The remaining `using` block must be:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
```

No other line in the file changes.

- [ ] **Step 2: Verify the diff is exactly one deleted line**

Run:

```bash
git diff backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs
```

Expected: a single hunk with one `-` line and no `+` lines.

---

## Task 7: Remove orphan `using` directive from `TransportBoxUniquenessTests.cs`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs:3`

- [ ] **Step 1: Delete the bare-`UseCases` `using`**

Current lines 1–5:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
```

After the edit, line 3 (`using Anela.Heblo.Application.Features.Logistics.UseCases;`) is removed entirely. The remaining `using` block must be:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
```

No other line in the file changes.

- [ ] **Step 2: Verify the diff is exactly one deleted line**

Run:

```bash
git diff backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs
```

Expected: a single hunk with one `-` line and no `+` lines.

---

## Task 8: Verify full build, tests, format, and OpenAPI parity

**Why:** Single end-to-end gate that satisfies every acceptance criterion in `spec.r1.md` §NFR-4 and the arch-review's §"Specification Amendments" item 3.

**Files:** None edited.

- [ ] **Step 1: Full solution build**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded`, `0 Error(s)`, and warning count equal to the baseline from Task 1 Step 1.

- [ ] **Step 2: Full test pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: all tests pass (same totals as Task 1 Step 2). Pay particular attention to `ChangeTransportBoxStateHandlerTests`, `TransportBoxControllerTests`, and `TransportBoxUniquenessTests` — these must all be green.

- [ ] **Step 3: Format check — must report no changes**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code `0`, no output. If `dotnet format` reports diffs, the refactor is incomplete (the leftover orphan `using` from one of Tasks 5–7 was missed, or the namespace edit reformatted whitespace).

- [ ] **Step 4: OpenAPI client parity check**

The OpenAPI/NSwag generator runs as part of the Debug build PostBuild step and writes to `backend/src/Anela.Heblo.API.Client/` and `frontend/src/api/generated/`. Confirm the move did not perturb the generated artifacts.

Run:

```bash
git diff -- backend/src/Anela.Heblo.API.Client frontend/src/api/generated
```

Expected: empty output. NSwag identifies DTOs by simple class name, not by CLR namespace — `ChangeTransportBoxStateRequest`/`Response` and their property shapes are unchanged, so the spec and generated client must be byte-identical.

If diffs appear, stop and investigate before committing — this would mean the OpenAPI surface drifted.

- [ ] **Step 5: Sanity-check the final staged diff**

Run:

```bash
git status
git diff --staged --stat
```

Expected staged changes (after `git add` in Step 6):

```
 backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs                                          | 2 +-
 .../Features/Logistics/UseCases/{ => ChangeTransportBoxState}/ChangeTransportBoxStateRequest.cs            | 2 +-
 .../Features/Logistics/UseCases/{ => ChangeTransportBoxState}/ChangeTransportBoxStateResponse.cs           | 2 +-
 backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs                             | 1 -
 backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs         | 1 -
 backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs                 | 1 -
 6 files changed, 3 insertions(+), 6 deletions(-)
```

(The exact path-shortening output may vary; what matters is six files touched — two renames + four `using` edits — and zero changes outside that set.)

- [ ] **Step 6: Stage and commit**

Run:

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs \
  backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs \
  backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs \
  backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs

git commit -m "$(cat <<'EOF'
refactor: relocate ChangeTransportBoxState DTOs into use case subfolder

Move ChangeTransportBoxStateRequest and ChangeTransportBoxStateResponse
from Features/Logistics/UseCases/ into the ChangeTransportBoxState/
subfolder, matching the per-use-case layout used by every other use
case in the Logistics module. Update their namespaces to
...UseCases.ChangeTransportBoxState; remove the now-orphan
...UseCases using directives from the controller and three test files.

No logic, contract, MediatR, DI, or OpenAPI surface change — NSwag
identifies DTOs by class name, so the generated TypeScript client is
unchanged.
EOF
)"
```

Expected: commit succeeds. If a pre-commit hook fails (formatter, analyzer), fix the underlying issue, re-stage, and create a NEW commit (do not `--amend`).

- [ ] **Step 7: Confirm working tree is clean and history is sensible**

Run:

```bash
git status
git log -1 --stat
```

Expected: `git status` reports a clean working tree; `git log -1 --stat` shows the six files above with the renames presented as `R` entries (history preserved).

---

## Acceptance Criteria Recap

Mapped to `spec.r1.md`:

- **FR-1 (move request):** Task 2 — file now at `.../ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs`; absent from the parent; body byte-identical except namespace.
- **FR-2 (move response):** Task 3 — file now at `.../ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs`; absent from the parent; body byte-identical except namespace.
- **FR-3 (namespace declarations):** Task 2 Step 3 & Task 3 Step 3 — both files declare `namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;` (file-scoped, matching the handler).
- **FR-4 (references updated):** Tasks 4–7 — controller's `using` replaced with the sub-namespace; three test files' orphan `using` removed. Handler is intentionally untouched per arch-review Decision 3.
- **FR-5 (behavioural preservation):** Task 8 Steps 1–4 — build + tests + format check + OpenAPI/TS-client diff check, all expected to be clean and zero-diff.
- **NFR-1/2/3 (perf/security/maintainability):** No code touched beyond namespace + `using` directives. Folder layout now matches every sibling use case.
- **NFR-4 (build & tooling):** Task 8 Steps 1, 3, 4 — `dotnet build` clean, `dotnet format --verify-no-changes` exits 0, OpenAPI artifacts byte-identical.
