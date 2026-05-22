# Relocate Photobank Request-Body DTOs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move four request-body DTO classes out of `PhotobankController.cs` (in the API project) into the Application project's Photobank `Contracts/` folder, restoring the documented "API project never owns DTOs" boundary with zero behavioral change.

**Architecture:** This is a pure structural relocation. The four `…Body` classes are cut verbatim from the bottom of the controller file and pasted into four new one-type-per-file `.cs` files under `Application/Features/Photobank/Contracts/`, each using the existing `Anela.Heblo.Application.Features.Photobank.Contracts` namespace (block-scoped, `class`, mutable setters). The controller gains a single `using` so its `[FromBody]` parameters still resolve. The API project already references the Application project, so dependency direction (API → Application) is preserved. NSwag derives TypeScript type names from the C# **type name**, not its CLR namespace, so the generated client output stays byte-for-byte identical.

**Tech Stack:** .NET 8, ASP.NET Core MVC controllers, NSwag OpenAPI client generation, xUnit (existing tests).

---

## Context an engineer needs before starting

- **Verified source:** The four classes live at `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs:425–446`, inside the controller's namespace but as siblings of the controller class (after the controller's closing brace at line 423). Line 447 is the namespace's closing brace.
- **Target folder:** `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/` already exists and holds `IndexRootDto.cs`, `PhotoDto.cs`, `TagDto.cs`, `TagRuleDto.cs`. All use **block-scoped** namespace `Anela.Heblo.Application.Features.Photobank.Contracts`, declare `public class`, and use no collection `using` directives.
- **`ImplicitUsings` is `enable`d** in `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — confirmed. So `List<string>` / `List<int>` compile without an explicit `using System.Collections.Generic;`. Do **not** add one (it would diverge from the sibling files).
- **No name collisions:** grep confirms no other `CreateTagBody` / `AddPhotoTagBody` / `BulkAddPhotoTagBody` / `BulkAddPhotoTagByIdsBody` class exists anywhere in `backend/src`.
- **Controller usings (lines 1–15):** Line 5 is `using Anela.Heblo.Application.Features.Photobank.Services;`. The new `…Photobank.Contracts` using sorts alphabetically **before** `…Photobank.Services`, so insert it immediately before line 5.
- **The four classes, exactly as they exist today (copy verbatim — same names, properties, accessors, initializers):**

```csharp
public class AddPhotoTagBody
{
    public string TagName { get; set; } = null!;
}

public class CreateTagBody
{
    public string Name { get; set; } = string.Empty;
}

public class BulkAddPhotoTagBody
{
    public List<string>? Tags { get; set; }
    public string? Search { get; set; }
    public string TagName { get; set; } = null!;
}

public class BulkAddPhotoTagByIdsBody
{
    public List<int> PhotoIds { get; set; } = [];
    public string TagName { get; set; } = null!;
}
```

- **Why no TDD test-first cycle here:** This task introduces no new behavior — it relocates type declarations. The verification gate is "the project still compiles, the formatter is clean, the existing tests still pass, and the regenerated TypeScript client diff is empty." Each task below ends with the relevant verification command and its expected output. Do not invent new unit tests for moved types; that is explicitly out of scope per the spec.

---

## File Structure

| File | Responsibility | Action |
|------|----------------|--------|
| `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddPhotoTagBody.cs` | Request body for `POST /api/photobank/photos/{id}/tags` | Create |
| `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/CreateTagBody.cs` | Request body for `POST /api/photobank/tags` | Create |
| `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagBody.cs` | Request body for `POST /api/photobank/photos/bulk-tag` | Create |
| `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagByIdsBody.cs` | Request body for `POST /api/photobank/photos/tag-by-ids` | Create |
| `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` | Photobank HTTP endpoints | Modify: add 1 `using`, delete lines 425–446 |

---

## Task 1: Create the four Contracts files

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddPhotoTagBody.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/CreateTagBody.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagBody.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagByIdsBody.cs`

- [ ] **Step 1: Create `AddPhotoTagBody.cs`**

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddPhotoTagBody
    {
        public string TagName { get; set; } = null!;
    }
}
```

- [ ] **Step 2: Create `CreateTagBody.cs`**

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class CreateTagBody
    {
        public string Name { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 3: Create `BulkAddPhotoTagBody.cs`**

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class BulkAddPhotoTagBody
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public string TagName { get; set; } = null!;
    }
}
```

- [ ] **Step 4: Create `BulkAddPhotoTagByIdsBody.cs`**

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class BulkAddPhotoTagByIdsBody
    {
        public List<int> PhotoIds { get; set; } = [];
        public string TagName { get; set; } = null!;
    }
}
```

- [ ] **Step 5: Verify the Application project compiles with the new files**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors. (Confirms `List<>` resolves via `ImplicitUsings` and the namespace/class declarations are valid. At this point the four classes exist in BOTH the Application project and the controller — that is expected and still compiles because they are in different namespaces; the duplicate is removed in Task 2.)

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddPhotoTagBody.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/CreateTagBody.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagBody.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagByIdsBody.cs
git commit -m "refactor: add Photobank request-body DTOs to Application Contracts"
```

---

## Task 2: Remove the inline classes from the controller and import the Contracts namespace

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` (add `using` near line 5; delete lines 425–446)

- [ ] **Step 1: Add the Contracts using directive**

Insert this line immediately **before** the existing `using Anela.Heblo.Application.Features.Photobank.Services;` (currently line 5), so the Photobank usings stay alphabetically ordered:

```csharp
using Anela.Heblo.Application.Features.Photobank.Contracts;
```

The top of the usings block should then read:

```csharp
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
```

- [ ] **Step 2: Delete the four inline class declarations**

Remove the entire block at lines 425–446 (the four `public class …Body { … }` declarations that sit between the controller's closing brace at line 423 and the namespace's closing brace at line 447). After deletion, the bottom of the file is the controller's closing brace followed directly by the namespace's closing brace:

```csharp
            return new FileStreamResult(rawThumbnail.Content, rawThumbnail.ContentType);
        }
    }
}
```

Do not touch any controller logic, attributes, routes, action signatures, or status-code declarations. The `[FromBody]` parameters (`CreateTagBody`, `AddPhotoTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`) now resolve via the new `using`.

- [ ] **Step 3: Verify the full backend solution compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (Confirms the controller resolves the relocated types and there is no longer a duplicate definition.)

- [ ] **Step 4: Check whether the controller's `using System.Collections.Generic;` (line 1) became unused**

Run: `dotnet format --verify-no-changes backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

- If it reports the line-1 `using System.Collections.Generic;` (or any other) as an unused/unsorted import in `PhotobankController.cs`, run `dotnet format` to let the analyzer remove/reorder it, then re-run the verify command until clean. Removing a genuinely-unused using is in scope (it is a direct consequence of this change).
- If it reports no changes, leave the file as-is. Do not manually delete the using on a guess — only act on what the analyzer flags. Keep the change surgical.

Expected (after any format pass): `dotnet format --verify-no-changes` reports no remaining changes.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs
git commit -m "refactor: move Photobank request-body DTOs out of controller"
```

---

## Task 3: Verify API and generated-client compatibility (FR-3)

**Files:**
- Verify only: `frontend/src/api/generated/api-client.ts` (auto-generated on build — do not hand-edit)

- [ ] **Step 1: Confirm the working tree is clean before regenerating**

Run: `git status --porcelain`
Expected: empty output (Tasks 1 and 2 are committed). A clean tree makes the next diff check unambiguous.

- [ ] **Step 2: Regenerate the OpenAPI clients via a build**

The TypeScript client is regenerated as part of the backend/build pipeline (NSwag runs on build). Run the project's standard build so generation runs:

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

> If `dotnet build` alone does not trigger client regeneration in this repo, consult `docs/development/api-client-generation.md` for the exact regeneration command and run that instead. The goal of this task is: the generator has run against the new code state.

- [ ] **Step 3: Confirm the generated client is byte-for-byte unchanged**

Run: `git diff --exit-code frontend/src/api/generated/api-client.ts`
Expected: **empty diff**, command exits 0. This is the FR-3 proof — NSwag names types by C# type name, not namespace, so `CreateTagBody`, `AddPhotoTagBody`, `BulkAddPhotoTagBody`, and `BulkAddPhotoTagByIdsBody` must emit identically to before the move.

> If the diff is NOT empty: STOP. Inspect what changed. A non-empty diff means a property name, nullability, or type ordering drifted during the move — re-check the four Contracts files against the verbatim source in the Context section above. Do not commit a changed client as "expected."

- [ ] **Step 4: Run the existing Photobank backend tests**

Run: `dotnet test --filter "FullyQualifiedName~Photobank"`
Expected: All tests pass. (Covers `PhotobankControllerThumbnailTests` and any other Photobank-scoped tests; confirms no regression in the controller's behavior.)

- [ ] **Step 5: Commit (only if the build produced any legitimately-regenerated, intentional client output)**

In the expected case Step 3's diff is empty and there is nothing new to commit. If the build produced unrelated generated-artifact churn that the repo normally commits, review it carefully first; otherwise there is no commit for this task.

```bash
git status   # expect clean; only commit if there is intended, reviewed output
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (move four DTOs into Contracts, verbatim, as `class`, block-scoped namespace, one file each) → Task 1, Steps 1–4. ✓
- FR-2 (delete inline declarations, add `using`, signatures unchanged) → Task 2, Steps 1–2. ✓
- FR-3 (HTTP contract + generated client identical) → Task 3, Steps 2–3. ✓
- NFR-3 (one-type-per-file, follows convention) → Task 1 creates four separate files. ✓
- NFR-4 (zero breaking changes) → Task 3 git-diff-empty gate. ✓
- Out-of-scope items (no validators, no renames, no record conversion, no other endpoints) → plan touches only the four classes + one `using` + one deletion. ✓

**2. Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N" placeholders. Every code step shows complete code; every verification step shows the exact command and expected output. ✓

**3. Type consistency:** Class names, property names, types, accessors, and initializers in Task 1 match the verbatim source in the Context section and are referenced consistently in Tasks 2–3 (`CreateTagBody`, `AddPhotoTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`). The namespace `Anela.Heblo.Application.Features.Photobank.Contracts` is identical across all four files and the controller `using`. ✓
