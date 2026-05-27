# Relocate UpdatePurchaseOrderRequestValidator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `UpdatePurchaseOrderRequestValidator.cs` from the `CreatePurchaseOrder` use case folder to the `UpdatePurchaseOrder` use case folder and correct its namespace, restoring Vertical Slice co-location with zero behavior change.

**Architecture:** Pure structural refactor inside `Anela.Heblo.Application.Features.Purchase`. Use `git mv` to preserve file history. Update the namespace declaration and drop the now-redundant `using` directive. DI registration in `PurchaseModule.cs` already imports both namespaces and binds by type, so no DI changes are required.

**Tech Stack:** .NET 8, C#, FluentValidation, MediatR. Build with `dotnet build`, format with `dotnet format`, tests with `dotnet test`.

---

## File Structure

**Move (via `git mv`):**
- From: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`
- To: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`

**Modify (in place after move):**
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs` — namespace + remove redundant `using`.

**Unchanged (verify only):**
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — already imports both `CreatePurchaseOrder` and `UpdatePurchaseOrder` namespaces (lines 3–4) and registers the validator by type (line 23). No edit required.

No tests reference the validator class by name (verified via grep), and FluentValidation registration is explicit (type-based), so no test or DI updates are needed.

---

## Task 1: Move the file via git mv

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs` → `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`

- [ ] **Step 1: Confirm source file exists and target slot is free**

Run:

```bash
ls backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
ls backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/
```

Expected:
- First command lists the file (exit 0).
- Second command lists `UpdatePurchaseOrderHandler.cs`, `UpdatePurchaseOrderRequest.cs`, `UpdatePurchaseOrderResponse.cs` — and does NOT list `UpdatePurchaseOrderRequestValidator.cs`.

If the target already has the file, stop — the move has already happened, jump to Task 2 verification.

- [ ] **Step 2: Move the file using git mv (preserves history)**

Run:

```bash
git mv backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
```

Expected: no output. `git status` should now show a single rename:

```bash
git status
```

Expected output (relevant lines):

```
renamed:    backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs -> backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
```

If `git status` shows a delete + add instead of a rename, that is acceptable too — git rename detection is heuristic and the next task's small edit may push it over the similarity threshold either way.

- [ ] **Step 3: Verify the build still compiles at this moment (sanity check)**

The build will fail at this point because the namespace inside the file is still `CreatePurchaseOrder` but the file now lives under `UpdatePurchaseOrder`. The C# compiler does not care about file location vs. namespace; however, the redundant `using` self-import (`using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;` inside a file that itself declares `namespace ... CreatePurchaseOrder`) still resolves. We expect this to BUILD because namespaces are decoupled from filesystem layout in C#.

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds (zero errors). Warnings count should be unchanged from baseline.

Rationale for this checkpoint: if build fails here, something unexpected is wrong (e.g., partial classes, source generators) — diagnose before changing the namespace.

---

## Task 2: Fix namespace declaration and redundant using

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`

- [ ] **Step 1: Inspect the current file contents**

Read the moved file. Expected current top of file:

```csharp
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;

public class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    // …unchanged…
}

public class UpdatePurchaseOrderLineRequestValidator : AbstractValidator<UpdatePurchaseOrderLineRequest>
{
    // …unchanged…
}
```

- [ ] **Step 2: Replace the using + namespace header**

Using the Edit tool, perform these two changes in `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`:

**Edit A — remove the redundant self-import and correct the namespace.**

Replace this block (exact match of the first 4 lines):

```csharp
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
```

With:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
```

No other line in the file changes. All validation rules, the `BeAReasonableDate` helper, and the `UpdatePurchaseOrderLineRequestValidator` class remain byte-for-byte identical.

- [ ] **Step 3: Verify the file shape after edit**

Run:

```bash
head -n 6 backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
```

Expected output:

```
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;

public class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
```

Also confirm `DateTime` is still resolvable. The project uses implicit usings (no explicit `using System;` was present before the move and it still isn't). If `dotnet build` later complains about `DateTime`, add `using System;` as the first using directive — but expect this NOT to be needed.

---

## Task 3: Verify build, format, and tests

**Files:** None (verification only)

- [ ] **Step 1: Run dotnet build**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded` with `0 Error(s)` and warning count unchanged from baseline.

If errors appear:
- `CS0246` / `The type or namespace name 'UpdatePurchaseOrderRequest' could not be found` → the moved file is missing access to the request DTO. Check that `UpdatePurchaseOrderRequest.cs` actually declares `namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;`. If yes, the moved validator is already in that namespace and should see it directly with no `using`. If no, the request DTO is in a different namespace — add the appropriate `using` to the validator.
- `CS0234` / `does not exist in the namespace` referencing `CreatePurchaseOrder.UpdatePurchaseOrderRequestValidator` → some caller (not expected, but possible) referenced the old fully-qualified name. Grep for it: `git grep "CreatePurchaseOrder.UpdatePurchaseOrderRequestValidator"`. If found, replace the namespace prefix. Should not happen per arch-review.

- [ ] **Step 2: Run dotnet format on the touched file**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs --verify-no-changes
```

Expected: exit code 0, no output, no diff to apply.

If `--verify-no-changes` fails:

```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
git diff backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
```

Inspect the diff. Only whitespace/using-ordering changes are acceptable. If `dotnet format` strips additional `using` directives, that means they were already unused — accept the change.

- [ ] **Step 3: Run the affected test projects**

The Purchase feature's tests live under `backend/test/`. Run them to confirm no regression:

```bash
dotnet test backend/Anela.Heblo.sln --no-build --filter "FullyQualifiedName~Purchase"
```

Expected: all tests pass. Exit code 0.

If the filter matches zero tests, fall back to the full suite to be safe:

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests pass (same pass count as baseline before this change).

- [ ] **Step 4: Confirm no stray references to the old location**

Run:

```bash
git grep -n "CreatePurchaseOrder.UpdatePurchaseOrderRequestValidator" -- backend/
git grep -n "UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator" -- backend/
```

Expected: both commands return no matches (exit code 1, no output).

- [ ] **Step 5: Confirm DI registration still resolves (smoke check via grep)**

Run:

```bash
git grep -n "IValidator<UpdatePurchaseOrderRequest>" -- backend/src/
```

Expected output (relevant line):

```
backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs:23:        services.AddScoped<IValidator<UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>();
```

This confirms `PurchaseModule.cs` still references the validator by type, and because the file imports the `UpdatePurchaseOrder` namespace at line 4, type resolution continues to work without code changes.

---

## Task 4: Commit the change

**Files:** None (git operations only)

- [ ] **Step 1: Stage the rename**

Run:

```bash
git status
```

Expected output (relevant lines):

```
Changes to be committed:
  renamed:    backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs -> backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
```

If the rename appears as a separate delete + add (git's rename detection didn't pick it up), still acceptable — the next commit will record both changes atomically.

If unstaged changes remain (e.g., the namespace edit wasn't staged with `git mv`), stage them:

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
```

- [ ] **Step 2: Inspect the rename diff one more time**

Run:

```bash
git diff --cached -M backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/
```

Expected diff (with rename detection):

```diff
diff --git a/backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs b/backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
similarity index 9X%
rename from backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
rename to backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs
@@ -1,4 +1,3 @@
-using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
 using FluentValidation;

-namespace Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
+namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
```

If you see other changes, abort and investigate — this change must remain surgical.

- [ ] **Step 3: Commit**

Run:

```bash
git commit -m "$(cat <<'EOF'
refactor: relocate UpdatePurchaseOrderRequestValidator to UpdatePurchaseOrder use case

Move UpdatePurchaseOrderRequestValidator.cs from CreatePurchaseOrder/ to
UpdatePurchaseOrder/ and correct its namespace. Restores Vertical Slice
co-location with the rest of the UpdatePurchaseOrder use case. No behavior
change — DI registration in PurchaseModule.cs already imports both namespaces
and binds by type, so no other edits are needed.
EOF
)"
```

Expected: commit succeeds. Hook output (if any) should remain green.

- [ ] **Step 4: Final verification**

Run:

```bash
git log -1 --stat
ls backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/
ls backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/
```

Expected:
- `git log -1 --stat` shows the rename.
- `CreatePurchaseOrder/` lists only its own files (no `UpdatePurchaseOrderRequestValidator.cs`).
- `UpdatePurchaseOrder/` includes `UpdatePurchaseOrderRequestValidator.cs` alongside its handler/request/response.

---

## Spec Coverage Check

- **FR-1 (Relocate file):** Task 1 step 2 (`git mv`), verified in Task 4 step 4.
- **FR-2 (Update namespace):** Task 2 step 2 (Edit A). Redundant `using` removed in the same edit.
- **FR-3 (Preserve registration and behavior):** No code change required; verified in Task 3 step 5 (DI registration grep) and Task 3 step 3 (tests).
- **FR-4 (No regressions):** Task 3 steps 1–4 (`dotnet build`, `dotnet format`, `dotnet test`, stray-reference grep).
- **NFR-1/2/3/4 (Performance / Security / Maintainability / Backwards compatibility):** No code paths altered. The structural change satisfies the maintainability NFR by definition (correct Vertical Slice placement).
- **Arch amendment 1 (explicit DI, not assembly scanning):** Honored — we leave `PurchaseModule.cs` untouched and verify the explicit registration line continues to compile and resolve.
- **Arch amendment 2 (keep `using FluentValidation;`, drop only the self-namespace `using`):** Honored exactly in Task 2 step 2.
- **Arch amendment 3 (preserve git history via `git mv`):** Honored in Task 1 step 2.
