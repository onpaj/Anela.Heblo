# LogisticsController User-ID Resolution Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the dead user-ID GUID resolution block from `LogisticsController` (two action methods), remove the unused `UserId` property from both gift-package request DTOs, and clean up the frontend call site and Jest mocks that still send the stripped field.

**Architecture:** This is a delete-and-simplify refactor — not extract-and-relocate. Exploration confirmed `request.UserId` is read by zero handlers, services, or persistence code; the actual audit field on `GiftPackageManufactureLog.CreatedBy` is a string already populated from `ICurrentUserService.GetCurrentUser().Name` inside `GiftPackageManufactureService`. The controller's GUID parsing block, the sentinel `00000000-0000-0000-0000-000000000001`, the controller's `ICurrentUserService` dependency, and the DTO `UserId` properties are all dead code. After the refactor, both controller actions become one-line `_mediator.Send` calls matching the shape of every other action in the controller, and the sentinel literal is removed from the codebase entirely.

**Tech Stack:** .NET 8 (C#), MediatR, ASP.NET Core MVC controllers, NSwag for OpenAPI client generation, React + TypeScript frontend, Jest for frontend unit tests, xUnit + FluentAssertions + Moq for backend tests.

---

## File Structure

### Backend — modified

- `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs`
  - Drop `ICurrentUserService` field, constructor parameter, and the `using Anela.Heblo.Domain.Features.Users;` directive.
  - Replace the two action bodies (`CreateGiftPackageManufacture`, `DisassembleGiftPackage`) with the one-liner shape used by every other action.

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureRequest.cs`
  - Remove `public Guid UserId { get; set; }`.

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageRequest.cs`
  - Remove `public Guid UserId { get; set; }`.

### Frontend — modified

- `frontend/src/components/pages/GiftPackageManufacturing/index.tsx`
  - Remove the `userId` field (and its trailing comment) from the `CreateGiftPackageManufactureRequest` object literal at line 79.

- `frontend/src/api/hooks/__tests__/useGiftPackageManufacturing.test.ts`
  - Remove `userId: "user123",` from the four **request** literals at lines 310, 333, 371, 396. Leave the `userId` line inside `mockManufactureResponse.data` (line 72) untouched — it is a response mock whose loose shape is pre-existing and unrelated to this refactor's scope.

### Backend — auto-regenerated (no manual edit)

- `frontend/src/api/generated/api-client.ts`
  - The `CreateGiftPackageManufactureRequest` and `DisassembleGiftPackageRequest` TypeScript classes regenerate without the `userId` property on the next `dotnet build`.

### Files NOT touched (intentional)

- `CreateGiftPackageManufactureHandler.cs`, `DisassembleGiftPackageHandler.cs` — never referenced `request.UserId`; no change.
- `GiftPackageManufactureService.cs` — already injects `ICurrentUserService` and uses `.Name` for `CreatedBy`; no change.
- `GiftPackageManufactureModule.cs`, `LogisticsModule.cs` — no DI registration changes (no new helper class).
- `GiftPackageManufactureServiceTests.cs` — covers `ICurrentUserService` mocking and asserts `CreatedBy` already; no change.
- `DashboardController.cs` — identical dead-code pattern but explicitly out of scope per the spec.

---

## Pre-flight

Before starting Task 1, verify the worktree branch is correct and the build baseline is green.

- [ ] **Pre-flight Step 1: Confirm working directory and branch**

Run:

```bash
pwd
git status
git rev-parse --abbrev-ref HEAD
```

Expected: working directory ends in `.worktrees/feat-arch-review-logistics-user-id-resolution`, working tree clean, branch is `feat-arch-review-logistics-user-id-resolution`.

- [ ] **Pre-flight Step 2: Baseline backend build**

Run:

```bash
cd backend && dotnet build
```

Expected: build succeeds with 0 errors. Note any pre-existing warnings — they are not this refactor's concern.

- [ ] **Pre-flight Step 3: Baseline backend tests for affected project**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics" --nologo
```

Expected: all Logistics-related tests pass. Record the count of passing tests so the post-change run can be compared.

- [ ] **Pre-flight Step 4: Baseline frontend build and tests**

Run:

```bash
cd frontend && npm run build
cd frontend && npm test -- --testPathPattern="useGiftPackageManufacturing" --watchAll=false
```

Expected: frontend builds with 0 errors; the gift-package manufacturing test suite passes.

---

## Task 1: Strip user-ID resolution from `LogisticsController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs`

This task removes the dead resolution block and the `ICurrentUserService` dependency. After this task the backend will still build because the DTO `UserId` properties remain — they simply stop being written.

- [ ] **Step 1: Replace the `CreateGiftPackageManufacture` action body**

Open `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs`. Replace the entire `CreateGiftPackageManufacture` block (originally lines 71–94 inclusive — the doc-comment summary, the `[HttpPost]` attribute, and the method body) with the version below. Keep the doc-comment summary text identical.

```csharp
    /// <summary>
    /// Execute gift package manufacturing process
    /// </summary>
    [HttpPost("gift-packages/manufacture")]
    public async Task<ActionResult<CreateGiftPackageManufactureResponse>> CreateGiftPackageManufacture(
        [FromBody] CreateGiftPackageManufactureRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 2: Replace the `DisassembleGiftPackage` action body**

In the same file, replace the entire `DisassembleGiftPackage` method (originally lines 96–119 — the `[HttpPost]` attribute, the doc-comment summary, and the method body) with:

```csharp
    /// <summary>
    /// Disassemble gift package back to individual components
    /// </summary>
    [HttpPost("gift-packages/disassemble")]
    public async Task<ActionResult<DisassembleGiftPackageResponse>> DisassembleGiftPackage(
        [FromBody] DisassembleGiftPackageRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 3: Remove the `ICurrentUserService` field and constructor parameter**

Replace the field-and-constructor block (originally lines 19–26) with the field-and-constructor block below — `IMediator` is the only dependency.

```csharp
    private readonly IMediator _mediator;

    public LogisticsController(IMediator mediator)
    {
        _mediator = mediator;
    }
```

- [ ] **Step 4: Remove the now-unused `using` directive**

Delete the line:

```csharp
using Anela.Heblo.Domain.Features.Users;
```

(originally line 7). All other `using` directives stay.

- [ ] **Step 5: Verify the controller has zero references to the removed symbols**

Use Grep across the file to confirm none of the following strings remain:

```
ICurrentUserService
_currentUserService
Guid.TryParse
00000000-0000-0000-0000-000000000001
Anela.Heblo.Domain.Features.Users
```

Expected: zero matches in `LogisticsController.cs` for every string.

- [ ] **Step 6: Build the backend**

Run:

```bash
cd backend && dotnet build
```

Expected: build succeeds with 0 errors. The DTOs still expose `UserId` so the build is not affected by the next two tasks yet.

- [ ] **Step 7: Format the modified C# file**

Run:

```bash
cd backend && dotnet format --include src/Anela.Heblo.API/Controllers/LogisticsController.cs
```

Expected: no diagnostics. The file ends up clean.

- [ ] **Step 8: Run Logistics tests**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics" --nologo
```

Expected: same passing count as pre-flight Step 3. No new failures.

- [ ] **Step 9: Commit the controller change**

```bash
git add backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs
git commit -m "refactor(logistics): drop dead user-id resolution from controller actions

Remove the GUID parse + sentinel-fallback block from
CreateGiftPackageManufacture and DisassembleGiftPackage. The resolved
GUID was assigned to request.UserId, which no handler or service ever
read. Drop the now-unused ICurrentUserService dependency and the
sentinel literal 00000000-0000-0000-0000-000000000001 along with it.
The audit string on GiftPackageManufactureLog.CreatedBy is still
populated by GiftPackageManufactureService via its existing
ICurrentUserService dependency."
```

---

## Task 2: Remove `UserId` from `CreateGiftPackageManufactureRequest`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureRequest.cs`

- [ ] **Step 1: Confirm no backend reader exists**

Run:

```bash
cd backend && grep -rn "CreateGiftPackageManufactureRequest" src/ test/ | grep -v "\.UserId" || true
grep -rn "request\.UserId" backend/src/Anela.Heblo.Application/Features/Logistics/ || true
```

Expected: zero matches in the second grep. (First grep just inventories existing usages — none should reference `.UserId` after the controller change.)

- [ ] **Step 2: Edit the request DTO**

Replace the file contents with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

public class CreateGiftPackageManufactureRequest : IRequest<CreateGiftPackageManufactureResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
    public bool AllowStockOverride { get; set; }
}
```

- [ ] **Step 3: Build the backend**

Run:

```bash
cd backend && dotnet build
```

Expected: build succeeds. The OpenAPI client regenerates as part of the build; the regenerated `frontend/src/api/generated/api-client.ts` no longer has a `userId` field on `CreateGiftPackageManufactureRequest`.

- [ ] **Step 4: Confirm regenerated client lost the field**

Run:

```bash
grep -n "class CreateGiftPackageManufactureRequest" -A 30 frontend/src/api/generated/api-client.ts
```

Expected: the regenerated class body does not include a `userId` property or its `init`/`toJSON` lines. (If it still does, the build didn't regenerate the client — re-run `dotnet build`.)

- [ ] **Step 5: Format**

Run:

```bash
cd backend && dotnet format --include src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureRequest.cs
```

Expected: no diagnostics.

---

## Task 3: Remove `UserId` from `DisassembleGiftPackageRequest`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageRequest.cs`

- [ ] **Step 1: Edit the request DTO**

Replace the file contents with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;

public class DisassembleGiftPackageRequest : IRequest<DisassembleGiftPackageResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
}
```

- [ ] **Step 2: Build the backend**

Run:

```bash
cd backend && dotnet build
```

Expected: build succeeds. The OpenAPI client regenerates again; the regenerated `DisassembleGiftPackageRequest` class drops the `userId` property.

- [ ] **Step 3: Confirm the regenerated client lost the field**

Run:

```bash
grep -n "class DisassembleGiftPackageRequest" -A 30 frontend/src/api/generated/api-client.ts
```

Expected: the regenerated class body does not include a `userId` property.

- [ ] **Step 4: Format**

Run:

```bash
cd backend && dotnet format --include src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageRequest.cs
```

Expected: no diagnostics.

- [ ] **Step 5: Run the full Logistics test suite**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics" --nologo
```

Expected: same passing count as pre-flight Step 3. The `GiftPackageManufactureServiceTests` continue to assert `CreatedBy == userId` against the mocked `ICurrentUserService.Name`; nothing in the test inputs references `request.UserId`.

- [ ] **Step 6: Commit the DTO and regenerated-client changes**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageRequest.cs \
        frontend/src/api/generated/api-client.ts
git commit -m "refactor(logistics): drop unused UserId from gift-package request DTOs

CreateGiftPackageManufactureRequest.UserId and
DisassembleGiftPackageRequest.UserId were written only by the
controller and read by nothing. Removing them shrinks the request
contract and propagates to the regenerated TypeScript OpenAPI client."
```

---

## Task 4: Strip `userId` from the frontend page

**Files:**
- Modify: `frontend/src/components/pages/GiftPackageManufacturing/index.tsx`

- [ ] **Step 1: Baseline — confirm the file currently fails to type-check**

Run:

```bash
cd frontend && npx tsc --noEmit
```

Expected: TypeScript fails on `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` around line 79, with an error such as `Object literal may only specify known properties, and 'userId' does not exist in type '...'`. (If it does not fail, Task 2 or Task 3's `dotnet build` did not actually regenerate the client — go back and re-run the backend build.)

- [ ] **Step 2: Remove the `userId` field**

In `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` at the `handleManufacture` request object (lines 75–80), delete the `userId` line **and** the trailing comma on the previous line so the literal stays valid. The result must look exactly like:

```typescript
      const request = new CreateGiftPackageManufactureRequest({
        giftPackageCode: selectedPackage.code,
        quantity: quantity,
        allowStockOverride: false // TODO: This could be made configurable via UI
      });
```

Do **not** touch the `handleEnqueueManufacture` literal beneath it — it already does not set `userId`.

- [ ] **Step 3: Type-check the frontend**

Run:

```bash
cd frontend && npx tsc --noEmit
```

Expected: the `userId` error on `GiftPackageManufacturing/index.tsx` is gone. Other unrelated errors are not introduced by this change — if any appear in the test file at `useGiftPackageManufacturing.test.ts`, they are expected and Task 5 addresses them.

---

## Task 5: Strip `userId` from frontend Jest mocks

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useGiftPackageManufacturing.test.ts`

- [ ] **Step 1: Remove `userId` from the four request literals**

Open `frontend/src/api/hooks/__tests__/useGiftPackageManufacturing.test.ts`. There are four request literals that pass to `useCreateGiftPackageManufacture().mutateAsync(...)`. Delete the `userId: "user123",` line from each:

  - Inside the `"should create gift package manufacture successfully"` test (around line 306–311).
  - Inside the `"should handle manufacture creation errors"` test (around line 329–334).
  - Inside the `"should invalidate available gift packages cache on success"` test (around line 367–372).
  - Inside the `"should handle different stock override scenarios"` test (around line 392–397).

The resulting literal in each case must look like:

```typescript
      const manufactureRequest = {
        giftPackageCode: "SET001",
        quantity: 5,
        allowStockOverride: false,
      };
```

(Adjust `quantity` and `allowStockOverride` per the original literal — keep all other fields exactly as they were.)

- [ ] **Step 2: Leave the response mock untouched**

Do **not** edit `mockManufactureResponse` at lines 67–85. Its `userId: "user123",` line sits inside the response payload mock; that mock has a pre-existing loose shape that does not match the real `CreateGiftPackageManufactureResponse` (the real response is `{ manufacture: GiftPackageManufactureDto }`), so removing `userId` from it changes nothing observable and the mock is consumed as `any` by `mockResolvedValueOnce`. Leaving it preserves the surgical-change rule.

- [ ] **Step 3: Type-check**

Run:

```bash
cd frontend && npx tsc --noEmit
```

Expected: zero errors related to `userId` or to the gift-package files.

- [ ] **Step 4: Run the affected test file**

Run:

```bash
cd frontend && npm test -- --testPathPattern="useGiftPackageManufacturing" --watchAll=false
```

Expected: all tests in `useGiftPackageManufacturing.test.ts` pass. No assertions need updating because none of them inspected `userId`.

- [ ] **Step 5: Frontend build**

Run:

```bash
cd frontend && npm run build
```

Expected: build succeeds with 0 errors.

- [ ] **Step 6: Frontend lint**

Run:

```bash
cd frontend && npm run lint
```

Expected: no new lint warnings or errors on the two modified frontend files (`GiftPackageManufacturing/index.tsx`, `useGiftPackageManufacturing.test.ts`). Pre-existing warnings elsewhere are out of scope.

- [ ] **Step 7: Commit the frontend cleanup**

```bash
git add frontend/src/components/pages/GiftPackageManufacturing/index.tsx \
        frontend/src/api/hooks/__tests__/useGiftPackageManufacturing.test.ts
git commit -m "refactor(logistics): drop userId from gift-package request call site and mocks

The regenerated OpenAPI TypeScript client no longer carries userId on
CreateGiftPackageManufactureRequest. Strip the matching field from the
page's request literal and from the four Jest request literals that
exercise useCreateGiftPackageManufacture. The mockManufactureResponse
literal is left alone — its userId line is in the response payload and
is unrelated to this refactor."
```

---

## Task 6: Final verification

This task verifies the full backend + frontend tree builds cleanly after all earlier commits and that no stale references remain.

- [ ] **Step 1: Full backend build + format check**

Run:

```bash
cd backend && dotnet build
cd backend && dotnet format --verify-no-changes
```

Expected: build succeeds; `dotnet format --verify-no-changes` exits 0.

- [ ] **Step 2: Full backend test suite for affected slice**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics" --nologo
```

Expected: identical passing count to pre-flight Step 3.

- [ ] **Step 3: Full frontend build + lint + tests for affected slice**

Run:

```bash
cd frontend && npm run build
cd frontend && npm run lint
cd frontend && npm test -- --testPathPattern="useGiftPackageManufacturing|GiftPackageManufacturing" --watchAll=false
```

Expected: build, lint, and tests all succeed.

- [ ] **Step 4: Repo-wide sweep for stale references**

Run each grep below and confirm the expected outcome:

```bash
grep -rn "00000000-0000-0000-0000-000000000001" backend/src frontend/src || true
```

Expected: zero matches (the sentinel literal lived only inside `LogisticsController.cs`, and we removed both copies). Matches inside test files at `backend/test/` or `docs/` are pre-existing and out of scope.

```bash
grep -rn "request\.UserId" backend/src/Anela.Heblo.Application/Features/Logistics/ backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs || true
```

Expected: zero matches.

```bash
grep -n "userId" frontend/src/components/pages/GiftPackageManufacturing/index.tsx || true
```

Expected: zero matches.

- [ ] **Step 5: Sanity-check controller shape**

Run:

```bash
grep -c "_mediator.Send" backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs
grep -c "ICurrentUserService" backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs
```

Expected: the first command returns the number of action methods that dispatch via MediatR (currently 6 — every action uses MediatR after this refactor); the second returns `0`.

---

## Task 7: Memory entry + branch hand-off

This is housekeeping so future-Claude knows the pattern and rationale.

- [ ] **Step 1: Drop a decision memo**

Create `memory/decisions/logistics-no-userid-resolver.md` with the contents below.

```markdown
# Logistics: no UserId helper, no sentinel GUID

**Date:** 2026-05-17
**Context:** `LogisticsController.CreateGiftPackageManufacture` and `DisassembleGiftPackage` historically parsed `ICurrentUserService.GetCurrentUser().Id` to a GUID and fell back to the sentinel `00000000-0000-0000-0000-000000000001`. The resolved GUID was stamped onto `request.UserId`.

**Discovery:** `request.UserId` was read by no handler, service, repository, or persisted column. The only audit field on `GiftPackageManufactureLog` is `CreatedBy` (string), already populated by `GiftPackageManufactureService` via its existing `ICurrentUserService.GetCurrentUser().Name` call.

**Decision:** Delete the resolution and the DTO `UserId` properties outright. Do not extract a `UserIdResolver` helper; there is no consumer.

**If a real GUID audit column is later required:**
- Add it to `GiftPackageManufactureLog` via migration + domain change.
- Populate it inside `GiftPackageManufactureService` (next to the existing `_currentUserService` usage), not in a handler or a shared resolver.
- Use the existing `TransportBox` pattern: `Guid.TryParse(user.Id, out var userId) ? userId : null` — nullable, no sentinel. See `CreateNewTransportBoxHandler.cs:43` and `OpenOrResumeBoxByCodeHandler.cs:72`.

**Out of scope (still latent):** `DashboardController` has the same dead-code shape. Same delete-and-simplify treatment will apply when it is touched.
```

- [ ] **Step 2: Commit and push**

```bash
git add memory/decisions/logistics-no-userid-resolver.md
git commit -m "docs(memory): record decision to remove unused UserId resolver in Logistics"
git push -u origin feat-arch-review-logistics-user-id-resolution
```

Expected: push succeeds.

- [ ] **Step 3: Open the PR**

Run:

```bash
gh pr create --base main --title "refactor(logistics): remove dead user-id resolution from gift-package endpoints" --body "$(cat <<'EOF'
## Summary
- `LogisticsController.CreateGiftPackageManufacture` and `DisassembleGiftPackage` had identical GUID-parse + sentinel-fallback blocks that wrote `request.UserId`. Grep confirmed zero readers — the handlers, the service, and the persisted `GiftPackageManufactureLog.CreatedBy` (string) never consumed it. The audit string is and remains populated by `GiftPackageManufactureService` via its existing `ICurrentUserService.GetCurrentUser().Name` call.
- Removed the resolution block, the controller's `ICurrentUserService` dependency, the sentinel literal `00000000-0000-0000-0000-000000000001`, and the `UserId` properties from both request DTOs.
- The regenerated TypeScript OpenAPI client dropped `userId`; updated the page's request literal and four Jest request literals to match. Response-shape mock (`mockManufactureResponse`) left untouched — its loose shape is pre-existing technical debt.
- Out of scope: `DashboardController` exhibits the same pattern. Recorded the rationale in `memory/decisions/logistics-no-userid-resolver.md`.

## Test plan
- [x] `dotnet build` clean
- [x] `dotnet format --verify-no-changes` clean
- [x] Backend Logistics test suite passes (same count as before)
- [x] `npm run build` clean
- [x] `npm run lint` clean for modified files
- [x] `npm test` for `useGiftPackageManufacturing` passes
- [ ] Smoke on staging: `POST /api/logistics/gift-packages/manufacture` succeeds for an authenticated user; `CreatedBy` on the new manufacture log row equals the authenticated user's name (unchanged behavior).
- [ ] Smoke on staging: `POST /api/logistics/gift-packages/disassemble` succeeds for an authenticated user; `CreatedBy` on the new log row equals the authenticated user's name (unchanged behavior).
EOF
)"
```

Expected: a PR URL prints. Hand it back to the user.

---

## Spec / arch-review coverage map

| Spec / arch-review item | Covered by |
|---|---|
| FR-1 (controller actions become thin pass-throughs) | Task 1, Steps 1–5 |
| FR-2 (handlers resolve acting user's ID) | **Removed** by arch-review (Specification Amendments #1) — no task needed; existing service already does this with `Name`, no GUID consumer exists |
| FR-3 (shared resolution helper) | **Removed** by arch-review (Specification Amendments #2) — no task needed |
| FR-4 (remove `UserId` from request DTOs) | Tasks 2 & 3 (backend), Task 4 (frontend page), Task 5 (frontend mocks) |
| FR-5 (behavior preservation) | Pre-flight Steps 2–4 baseline + Task 3 Step 5 + Task 5 Step 4 + Task 6 (full re-verification) |
| NFR-1 (performance) | No new work — change is purely subtractive |
| NFR-2 (security) | `[Authorize]` on the controller untouched; `ICurrentUserService` contract untouched; sentinel removed, not relocated |
| NFR-3 (testability) | **Removed** by arch-review (Specification Amendments #5) — no new handler resolution to test |
| NFR-4 (sentinel literal exactly once) | Reduced to **zero** copies in production code (Task 6 Step 4 verifies) |
| Arch-review risk: frontend ships `userId` after client regenerates | Task 4 |
| Arch-review risk: Jest mocks still reference `userId` | Task 5 |
| Arch-review risk: reviewer asks "where is `UserIdResolver`?" | PR body in Task 7 Step 3 calls out grep evidence + design rationale |
| Arch-review risk: future audit-column requirement | Decision memo in Task 7 Step 1 |
| Out of scope: `DashboardController` same pattern | Explicitly noted in decision memo and PR body |
