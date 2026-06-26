# Fix PhotobankController.UpdateRule Body Contract — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the misleading `[FromBody] UpdateRuleRequest` on `PUT /api/photobank/settings/rules/{id}` with a dedicated `UpdateRuleBody` HTTP contract so the OpenAPI schema (and regenerated TypeScript client) no longer advertises an `id` body field that the controller already overrides from the route.

**Architecture:** Pure HTTP-contract cleanup, behavior-preserving. The MediatR command (`UpdateRuleRequest`) and its validator stay unchanged; the controller maps `(route id, body)` → command, exactly the same way as today. The change brings `UpdateRule` in line with every other write endpoint in the Photobank module (`AddPhotoTagBody`, `CreateTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`).

**Tech Stack:** .NET 8 / ASP.NET Core MVC controllers, MediatR, FluentValidation, NSwag-generated TypeScript client (regenerated on `npm run build`).

---

## File Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs` — new public HTTP body contract (4 fields, no `Id`).

**Modify:**
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` (lines 299–313) — change `[FromBody]` parameter type and the right-hand side of the `new UpdateRuleRequest { … }` initializer. Keep all attributes, `HandleResponse(response)`, and the `_mediator` field name as-is.

**Regenerated (build-time, do not hand-edit):**
- `frontend/src/api/generated/api-client.ts` — NSwag overwrites this on `npm run build`. Must be staged and committed alongside the C# changes.

**No changes:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/UpdateRule/UpdateRuleRequest.cs` — MediatR command keeps `Id`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Validators/UpdateRuleRequestValidator.cs` (or wherever it lives) — validates the post-construction command; route-supplied `Id` still satisfies `GreaterThan(0)`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/UpdateRule/UpdateRuleHandler.cs` and `UpdateRuleResponse.cs`.
- No frontend hand-written caller exists (verified in arch-review). FR-4 is therefore a no-op at the `.ts`/`.tsx` layer.

## Testing Strategy

No new tests are added for this change:
- No `UpdateRule*Tests.cs` files exist today under `backend/test/Anela.Heblo.Tests/Features/Photobank/` (verified in arch-review).
- The change is type-level only — it does not alter runtime behavior, validation, response shape, or status codes. Adding a unit test that asserts "body class has these four properties" or "controller maps body fields to command fields" would be reflection-based and brittle, with no value beyond what the compiler already enforces.
- FR-5 ("existing tests pass without modification") is therefore trivially satisfied — there are none to update.
- The OpenAPI regeneration step is the verifiable check that the contract change worked: the generated TypeScript `updateRule` body type must no longer contain `id`.

---

## Task 1: Add the `UpdateRuleBody` HTTP contract class

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs`

**Context for the engineer:**
The `Contracts/` folder under the Photobank module holds public HTTP body DTOs (one per write endpoint). All existing files use:
- Block-style namespace `Anela.Heblo.Application.Features.Photobank.Contracts`
- `public class` (never `record` — see CLAUDE.md project rule and `docs/architecture/development_guidelines.md`: OpenAPI generators mishandle record parameter order)
- `= null!;` initializer for non-nullable required strings (matches `AddPhotoTagBody`, `BulkAddPhotoTagBody`)

This new class mirrors `UpdateRuleRequest` minus the `Id` field. The four properties below come from the MediatR command verbatim (verified in `UpdateRuleRequest.cs:7-11`).

- [ ] **Step 1: Create `UpdateRuleBody.cs` with the exact contents below**

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class UpdateRuleBody
    {
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
```

- [ ] **Step 2: Verify the file compiles**

Run from repo root:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: build succeeds with 0 errors. (The class is not yet referenced by anything; this confirms it parses and matches project nullable settings.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs
git commit -m "feat(photobank): add UpdateRuleBody HTTP contract"
```

---

## Task 2: Switch `PhotobankController.UpdateRule` to bind `UpdateRuleBody`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs:299-313`

**Context for the engineer:**

`PhotobankController.cs` already has `using Anela.Heblo.Application.Features.Photobank.Contracts;` at the top (line 4), so no new `using` directive is needed.

The current method (verified verbatim from the file, lines 299–313):

```csharp
public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
    int id,
    [FromBody] UpdateRuleRequest request,
    CancellationToken cancellationToken = default)
{
    var command = new UpdateRuleRequest
    {
        Id = id,
        PathPattern = request.PathPattern,
        TagName = request.TagName,
        IsActive = request.IsActive,
        SortOrder = request.SortOrder,
    };
    var response = await _mediator.Send(command, cancellationToken);
    return HandleResponse(response);
}
```

Only two things change:
1. `[FromBody] UpdateRuleRequest request` → `[FromBody] UpdateRuleBody body`
2. The four `request.XYZ` field reads on the right-hand side become `body.XYZ`.

Everything else — the `[HttpPut]`, the three `[ProducesResponseType]` attributes, `[FeatureAuthorize(Feature.Marketing_Photobank, AccessLevel.Admin)]`, the `_mediator.Send` call, and the `return HandleResponse(response);` line — stays exactly as-is. **Do not change `HandleResponse(response)` to `Ok(...)`** — the spec sample shows `Ok` but the project's shared error/success envelope is `HandleResponse`, and changing it would silently shift error status codes (see arch-review Risk #1 and Decision #3).

- [ ] **Step 1: Replace the method body**

Open `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` and replace lines 299–313 with:

```csharp
public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
    int id,
    [FromBody] UpdateRuleBody body,
    CancellationToken cancellationToken = default)
{
    var command = new UpdateRuleRequest
    {
        Id = id,
        PathPattern = body.PathPattern,
        TagName = body.TagName,
        IsActive = body.IsActive,
        SortOrder = body.SortOrder,
    };
    var response = await _mediator.Send(command, cancellationToken);
    return HandleResponse(response);
}
```

Leave lines 291–298 (the XML doc comment and the four attribute lines) and lines after 313 untouched.

- [ ] **Step 2: Confirm `AddRule` immediately above was NOT modified**

Re-open the file and check lines 280–289. They should still show `[FromBody] AddRuleRequest request` (the same anti-pattern, explicitly out of scope for this PR per the spec's "Out of Scope" section and arch-review Risk #2). If `AddRule` was accidentally touched, revert that portion.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs
git commit -m "refactor(photobank): bind UpdateRule body via UpdateRuleBody contract"
```

---

## Task 3: Validate the backend (build + format)

**Files:** none modified in this task (validation gate per CLAUDE.md "Validation before completion").

**Context for the engineer:**
The project requires both `dotnet build` and `dotnet format` to be clean before a backend change is considered done. `dotnet format` may rewrite the new file if its style drifts from the rest of the solution (e.g. trailing newline, brace style). Any rewrites it makes must be committed.

- [ ] **Step 1: Build the full backend solution**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` with 0 Error(s). Warnings are acceptable only if they already existed on `main` for unrelated files — there should be no new warnings traced to `UpdateRuleBody.cs` or `PhotobankController.cs`.

- [ ] **Step 2: Run the formatter**

```bash
dotnet format backend/Anela.Heblo.sln
```
Expected: exit code 0. The formatter is silent when nothing changes; if it rewrites either touched file, `git status` will show modifications.

- [ ] **Step 3: Stage and commit any formatter changes (only if `git status` is dirty)**

```bash
git status
# If UpdateRuleBody.cs or PhotobankController.cs show as modified:
git add backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs \
        backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs
git commit -m "style: dotnet format"
```
If `git status` is clean, skip the commit — no changes to stage.

- [ ] **Step 4: Run the Photobank test project to confirm nothing regressed**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank" --no-build
```
Expected: all tests pass. (No `UpdateRule`-specific tests exist; this gate only catches accidental collateral damage to other Photobank tests.)

---

## Task 4: Regenerate the TypeScript client and verify the new body schema

**Files:**
- Regenerated: `frontend/src/api/generated/api-client.ts`

**Context for the engineer:**
The TypeScript client is generated by NSwag during `npm run build` per `docs/development/api-client-generation.md`. The backend Swagger document is consumed and the `.ts` file is overwritten. After the controller change, the generated `updateRule` method's body parameter type must:
- No longer contain an `id` field
- Contain exactly `pathPattern: string`, `tagName: string`, `isActive: boolean`, `sortOrder: number`

If a hand-written frontend caller had ever passed `id` in the body, the regenerated client would compile-break it. The arch-review confirmed no hand-written caller exists (a wider grep for `updateRule` and `photobank_UpdateRule` found references only inside `api-client.ts` itself), so no `.ts`/`.tsx` edits should be needed.

- [ ] **Step 1: Run the frontend build (regenerates `api-client.ts`)**

```bash
cd frontend
npm run build
```
Expected: build succeeds. The build step internally runs the OpenAPI generator before tsc/webpack; `api-client.ts` will be overwritten.

- [ ] **Step 2: Confirm the regenerated client matches the new schema**

```bash
git diff frontend/src/api/generated/api-client.ts | head -80
```

The diff must show, in the section defining the `updateRule` body type (search for `UpdateRuleBody` or the inline body interface used by `updateRule`):
- Removed: a `id: number` or `id?: number` property
- Added (or already present): exactly the four properties `pathPattern`, `tagName`, `isActive`, `sortOrder`

If `git diff` shows no change to `api-client.ts`, the regeneration step did not run — investigate by checking that the OpenAPI generation script is actually wired into `npm run build` (see `docs/development/api-client-generation.md`). Do not proceed until the diff confirms the schema change.

- [ ] **Step 3: Run the linter**

```bash
npm run lint
```
Expected: clean exit. The regenerated file is generator-managed and typically excluded from lint, but the lint pass also covers any frontend file that imports from `api-client.ts` and might break if the body type changed unexpectedly.

- [ ] **Step 4: Build one more time to confirm tsc is fully green after lint**

```bash
npm run build
```
Expected: succeeds with no TypeScript errors. (This catches the edge case where step 1's build cached a stale tsc state.)

- [ ] **Step 5: Commit the regenerated client**

```bash
cd ..
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(frontend): regenerate api-client for UpdateRuleBody"
```

If `git status` shows additional frontend files modified, stop and investigate — only `api-client.ts` should change in this task.

---

## Task 5: Final verification before declaring done

**Files:** none modified.

**Context for the engineer:**
This task is the project's CLAUDE.md "Validation before completion" gate, run end-to-end one final time to catch any cross-cutting issue the per-task gates missed.

- [ ] **Step 1: Verify the full git log shows only the expected commits**

```bash
git log --oneline main..HEAD
```

Expected output (order may differ if Task 3 Step 3 produced a formatter commit, which is optional):
- `chore(frontend): regenerate api-client for UpdateRuleBody`
- (optional) `style: dotnet format`
- `refactor(photobank): bind UpdateRule body via UpdateRuleBody contract`
- `feat(photobank): add UpdateRuleBody HTTP contract`

If there are extra commits touching anything else (other controllers, other modules, deletion of `Id` from `UpdateRuleRequest`, validator edits, or unrelated frontend files), revert them — the spec explicitly scopes this PR to the four files above.

- [ ] **Step 2: Verify no unintended file changes**

```bash
git diff --stat main..HEAD
```

Expected: exactly three files changed (or four if formatter touched anything beyond `UpdateRuleBody.cs`):
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs` (new)
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs`
- `frontend/src/api/generated/api-client.ts`

If `UpdateRuleRequest.cs`, `UpdateRuleHandler.cs`, `UpdateRuleResponse.cs`, the validator file, or any other controller appears in the diff, revert the unintended change. `UpdateRuleRequest.Id` MUST still exist on the MediatR command (out of scope per spec).

- [ ] **Step 3: Run the full backend build + frontend build one final time**

```bash
dotnet build backend/Anela.Heblo.sln
cd frontend && npm run build && npm run lint && cd ..
```
Expected: all four commands succeed.

- [ ] **Step 4: Verify the OpenAPI generator output the new body schema correctly**

Open `frontend/src/api/generated/api-client.ts` and search for `updateRule`. Inspect the method signature and the body type it accepts. Confirm:
- The body type contains `pathPattern`, `tagName`, `isActive`, `sortOrder`.
- The body type does NOT contain `id`.
- The route `id` is still a separate method parameter (not inside the body).

This is the single most important behavioral check of the entire PR — it is the user-visible artifact of the contract cleanup.

---

## Definition of Done

All boxes above are checked AND:

- [ ] `dotnet build backend/Anela.Heblo.sln` succeeds.
- [ ] `dotnet format backend/Anela.Heblo.sln` reports no pending changes.
- [ ] `cd frontend && npm run build && npm run lint` succeeds.
- [ ] Photobank backend tests pass (`dotnet test … --filter "FullyQualifiedName~Photobank"`).
- [ ] `git diff main..HEAD` shows only the three (or four with formatter) files listed in Task 5 Step 2.
- [ ] The regenerated `api-client.ts` no longer advertises an `id` field on the `updateRule` body type.
- [ ] `UpdateRuleRequest` (MediatR command) is unchanged — `Id` still present.
- [ ] `AddRule` (the sibling anti-pattern at lines 280–289) is NOT modified — out of scope for this PR.
