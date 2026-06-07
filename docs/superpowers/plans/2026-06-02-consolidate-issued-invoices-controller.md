# Consolidate Issued Invoice HTTP Surface onto a Single Controller — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the duplicate `IssuedInvoicesController` (`/api/IssuedInvoices`) and repoint three frontend hooks to the existing clean `InvoicesController` (`/api/invoices`), leaving exactly one HTTP surface for issued-invoice list/detail/stats.

**Architecture:** Pure refactor. Backend = file deletion. Frontend = three URL string replacements (one must additionally append `?withDetails=true` to preserve sync-history loading semantics). TypeScript API client is regenerated from the post-deletion OpenAPI spec. A single new xUnit test pins the empty-id validation guarantee that the deleted controller previously enforced in-process.

**Tech Stack:** .NET 8, ASP.NET Core MVC controllers, MediatR, xUnit + FluentAssertions + Moq, React + TanStack Query, NSwag-generated TypeScript client (Fetch template).

---

## Background and key references

Read these before starting:

- `artifacts/feat-arch-review-invoices-issuedinvoicescontr/spec.r1.md` — feature spec (FR-1 … FR-6).
- `artifacts/feat-arch-review-invoices-issuedinvoicescontr/arch-review.r1.md` — architectural rationale and the **critical** correction to FR-4: detail call must include `?withDetails=true` or the detail modal silently loses its sync history section.
- `docs/architecture/development_guidelines.md` — "Forbidden Practices" → no business logic in controllers.
- `docs/development/api-client-generation.md` — NSwag regeneration commands and the (out-of-scope) anti-pattern note about `(apiClient as any).http.fetch`.

## File Structure

**Deleted:**
- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs` — the legacy bloated controller.

**Modified:**
- `frontend/src/api/hooks/useIssuedInvoices.ts` — two URL literals: `/api/IssuedInvoices` → `/api/invoices` (list), and `/api/IssuedInvoices/{id}` → `/api/invoices/{id}?withDetails=true` (detail).
- `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts` — one URL literal: `/api/IssuedInvoices/sync-stats` → `/api/invoices/stats` (both path prefix and last segment change).
- `frontend/src/api/generated/api-client.ts` — **regenerated** by NSwag, not hand-edited. After regeneration, the `issuedInvoices_GetList`, `issuedInvoices_GetDetail`, and `issuedInvoices_GetSyncStats` methods will be gone.

**Added:**
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — new xUnit test class that pins the empty-id validation behaviour. The handler already implements the guard (`GetIssuedInvoiceDetailHandler.cs:33-41`); this test ensures nobody silently regresses it now that the duplicate controller-level guard is gone.

**Unchanged (do not touch):**
- `backend/src/Anela.Heblo.API/Controllers/InvoicesController.cs` — three relevant action methods stay byte-identical.
- All MediatR requests/responses/handlers, `IIssuedInvoiceRepository`, AutoMapper profile.
- `frontend/src/components/customer/IssuedInvoiceDetailModal.tsx` and `frontend/src/pages/customer/IssuedInvoicesPage.tsx` — consumers of the hooks; the hooks' public surface is preserved.

## Pre-flight check

- [ ] **Step 1: Verify worktree and tooling**

Run from the worktree root:

```bash
pwd
# Expected: ends with /.worktrees/feat-arch-review-invoices-issuedinvoicescontr
git status
# Expected: working tree clean (or only the plan file modified)
dotnet --version
# Expected: 8.x
dotnet tool restore
# Expected: "Tool 'nswag.consolesharedhost' (version 'X') was restored." (or "Tools have already been restored.")
```

- [ ] **Step 2: Baseline backend build to catch unrelated failures**

```bash
dotnet build backend/src/Anela.Heblo.API
```

Expected: `Build succeeded. 0 Error(s)`. Any failure here is pre-existing — stop and surface it; do not start the refactor on top of a broken baseline.

---

### Task 1: Add unit test pinning empty-id validation in `GetIssuedInvoiceDetailHandler`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`

**Why this task exists:** FR-3 requires verifying that the handler still returns `ErrorCode = ValidationError` for empty IDs after we remove the duplicate controller-level guard. The handler already implements the check (see `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs:33-41`), so the test should pass immediately when written against the current code — it acts as a regression pin, not a driver of new code. We still follow the TDD discipline of writing the test first and observing the result before touching anything else.

- [ ] **Step 1: Write the failing test**

Create the file with this exact content (matches the style of the existing `GetIssuedInvoicesListHandlerPaginationTests.cs` in the same folder — xUnit + FluentAssertions + Moq):

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

/// <summary>
/// Pins validation behavior in GetIssuedInvoiceDetailHandler that was previously
/// duplicated in the now-deleted IssuedInvoicesController.
/// </summary>
public class GetIssuedInvoiceDetailHandlerTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repositoryMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetIssuedInvoiceDetailHandler _handler;

    public GetIssuedInvoiceDetailHandlerTests()
    {
        _handler = new GetIssuedInvoiceDetailHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            Mock.Of<ILogger<GetIssuedInvoiceDetailHandler>>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError(string? invoiceId)
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = invoiceId!,
            WithDetails = true
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        response.Invoice.Should().BeNull();
        _repositoryMock.VerifyNoOtherCalls();
    }
}
```

- [ ] **Step 2: Run the test to confirm it actually passes against the existing handler**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests"
```

Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0` (3 = the three `[InlineData]` rows).

If it fails: the handler is not actually validating empty IDs the way the arch review claims. Stop and re-read `GetIssuedInvoiceDetailHandler.cs:29-41` before continuing — do not "fix" the handler to satisfy the test, since the spec explicitly forbids adding new controller-side validation and the handler-side guard is supposed to already exist.

- [ ] **Step 3: Format and stage the new test file**

```bash
dotnet format backend/test/Anela.Heblo.Tests --include backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs
```

- [ ] **Step 4: Commit**

```bash
git commit -m "test: pin empty-id validation in GetIssuedInvoiceDetailHandler"
```

---

### Task 2: Delete `IssuedInvoicesController.cs`

**Files:**
- Delete: `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs`

**Why this task exists:** FR-1 and FR-2. Removing the file eliminates the duplicate route surface and the project-rule violations (controller-level validation, error-code-to-HTTP-status mapping, try/catch blocks). `InvoicesController.cs` already exposes the same three operations correctly via `/api/invoices/*` and stays untouched.

- [ ] **Step 1: Delete the file**

```bash
git rm backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs
```

- [ ] **Step 2: Verify no backend code referenced it**

```bash
grep -rn "IssuedInvoicesController\|api/IssuedInvoices" backend/ || echo "OK: no references"
```

Expected: `OK: no references`. The arch review confirmed this; the grep is a safety net.

- [ ] **Step 3: Build the backend**

```bash
dotnet build backend/src/Anela.Heblo.API
```

Expected: `Build succeeded. 0 Error(s)`.

If it fails: read the error carefully. A genuine reference to the deleted controller (unlikely but possible) means you need to investigate — do **not** add the controller back. The right answer is to delete the dependency that referenced it, after confirming with the reviewer.

- [ ] **Step 4: Re-run the full backend test suite (still green after deletion)**

```bash
dotnet test backend/test/Anela.Heblo.Tests --no-build
```

Expected: all tests pass, including the one added in Task 1. FR-6 acceptance criterion: no test references `IssuedInvoicesController` or `/api/IssuedInvoices`, so the suite must remain green with zero tests deleted.

- [ ] **Step 5: Run formatter and commit**

```bash
dotnet format backend/src/Anela.Heblo.API
git add -u backend/
git commit -m "refactor: delete duplicate IssuedInvoicesController"
```

---

### Task 3: Repoint `useIssuedInvoices.ts` to `/api/invoices` (list + detail)

**Files:**
- Modify: `frontend/src/api/hooks/useIssuedInvoices.ts:120` (list URL) and `:148` (detail URL)

**Why this task exists:** FR-4 — list hook needs the new path. The detail hook additionally needs `?withDetails=true` appended; the legacy controller hardcoded `WithDetails=true`, but the surviving `InvoicesController.GetInvoiceDetail` defaults the query parameter to `false`. Without `?withDetails=true`, `IssuedInvoiceDetailModal.tsx:315,322` will receive an empty `syncHistory` array and silently stop rendering the "Sync History" section. This is the single highest-risk substitution in the whole refactor.

- [ ] **Step 1: Replace the list URL**

In `frontend/src/api/hooks/useIssuedInvoices.ts`, change line ~120 from:

```typescript
      const url = `/api/IssuedInvoices?${params.toString()}`;
```

to:

```typescript
      const url = `/api/invoices?${params.toString()}`;
```

- [ ] **Step 2: Replace the detail URL and append `?withDetails=true`**

In the same file, change line ~148 from:

```typescript
      const url = `/api/IssuedInvoices/${encodeURIComponent(invoiceId)}`;
```

to:

```typescript
      const url = `/api/invoices/${encodeURIComponent(invoiceId)}?withDetails=true`;
```

- [ ] **Step 3: Verify both edits**

```bash
grep -n "/api/" frontend/src/api/hooks/useIssuedInvoices.ts
```

Expected output (line numbers may differ slightly):

```
120:      const url = `/api/invoices?${params.toString()}`;
148:      const url = `/api/invoices/${encodeURIComponent(invoiceId)}?withDetails=true`;
```

No occurrence of `IssuedInvoices` should remain in this file.

- [ ] **Step 4: Stage but do not commit yet** (combined commit with Task 4)

```bash
git add frontend/src/api/hooks/useIssuedInvoices.ts
```

---

### Task 4: Repoint `useIssuedInvoiceSyncStats.ts` to `/api/invoices/stats`

**Files:**
- Modify: `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts:37`

**Why this task exists:** FR-4. The legacy route was `/api/IssuedInvoices/sync-stats`; the surviving route exposed by `InvoicesController.GetSyncStats` is `/api/invoices/stats` (note the path-segment rename from `sync-stats` to `stats`, not just the controller-name change). Easy to miss because it's two changes in one short URL.

- [ ] **Step 1: Replace the URL**

In `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts`, change line ~37 from:

```typescript
      const url = `/api/IssuedInvoices/sync-stats?${searchParams.toString()}`;
```

to:

```typescript
      const url = `/api/invoices/stats?${searchParams.toString()}`;
```

**Do not** touch the internal query-cache key on line 25 (`[...QUERY_KEYS.issuedInvoices, 'sync-stats', ...]`) — it is a cache key, not a URL, and the spec requires the hook's public surface to stay unchanged. Renaming it would be a "surgical improvement" the spec forbids.

- [ ] **Step 2: Verify the edit and confirm no legacy URL remains**

```bash
grep -n "/api/" frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts
grep -rn "/api/IssuedInvoices" frontend/src || echo "OK: no legacy URLs remain"
```

Expected:

```
37:      const url = `/api/invoices/stats?${searchParams.toString()}`;
OK: no legacy URLs remain
```

- [ ] **Step 3: Stage and commit both hook changes together**

```bash
git add frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts
git commit -m "fix(frontend): repoint issued-invoice hooks to /api/invoices"
```

---

### Task 5: Regenerate the OpenAPI TypeScript client

**Files:**
- Regenerated: `frontend/src/api/generated/api-client.ts`

**Why this task exists:** FR-5. With the legacy controller gone, the NSwag spec no longer contains the three `issuedInvoices_*` methods. Regeneration removes them from the generated TypeScript client and keeps the file consistent with the live API surface. The MSBuild target `GenerateFrontendClientManual` is defined in `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj:92-100`.

- [ ] **Step 1: Confirm backend builds clean (the regenerator can silently emit a stale client otherwise)**

```bash
dotnet build backend/src/Anela.Heblo.API
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2: Run the manual regeneration target**

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Expected: no error, and the file `frontend/src/api/generated/api-client.ts` is rewritten (check `git status` — it should appear as modified).

- [ ] **Step 3: Verify the legacy methods are gone**

```bash
grep -n "issuedInvoices_\|/api/IssuedInvoices" frontend/src/api/generated/api-client.ts || echo "OK: no legacy methods or URLs in generated client"
```

Expected: `OK: no legacy methods or URLs in generated client`.

If any `issuedInvoices_` method or `/api/IssuedInvoices` string survived, the backend build was stale or the NSwag config picked up a cached spec. Stop, run `dotnet build backend/src/Anela.Heblo.API` again, then re-run Step 2 — do not hand-edit the generated file.

- [ ] **Step 4: Verify the three new methods exist on the `Invoices` client surface**

```bash
grep -nE "invoices_GetInvoicesList|invoices_GetInvoiceDetail|invoices_GetSyncStats" frontend/src/api/generated/api-client.ts
```

Expected: three matches, one per method.

- [ ] **Step 5: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI TypeScript client"
```

---

### Task 6: Frontend type-check, lint, and build

**Why this task exists:** Validates that the hook edits and the regenerated client compile and lint cleanly together. Fast feedback before manual smoke testing.

- [ ] **Step 1: Type-check and lint**

```bash
cd frontend
npm run lint
```

Expected: zero errors (warnings unrelated to changed files are acceptable — do not fix them; the spec forbids surgical improvements).

- [ ] **Step 2: Production build**

```bash
npm run build
```

Expected: `Compiled successfully.` (or `Compiled with warnings.` if warnings are unrelated to our changes).

If the build fails with a TypeScript error in the regenerated client, that points to a backend/NSwag mismatch — go back to Task 5 Step 3 and re-verify the regeneration, then re-run.

- [ ] **Step 3: Final sweep for any missed legacy URL strings**

```bash
cd ..
grep -rn "/api/IssuedInvoices" frontend/src || echo "OK: no /api/IssuedInvoices strings in frontend/src"
```

Expected: `OK: no /api/IssuedInvoices strings in frontend/src`. (Acceptance criterion of FR-4.)

---

### Task 7: Manual smoke test — list, detail (with sync history), and stats

**Why this task exists:** Highest-risk regression — `IssuedInvoiceDetailModal` silently rendering an empty "Sync History" section because the new route defaulted `withDetails=false`. Automated tests do not cover the modal/hook composition, so this is a required manual verification step.

- [ ] **Step 1: Start the backend**

```bash
cd backend/src/Anela.Heblo.API
dotnet run
```

Expected: Kestrel listening on `https://localhost:5001`.

- [ ] **Step 2: In a second terminal, start the frontend**

```bash
cd frontend
npm start
```

Expected: dev server on `http://localhost:3000`. `npm start`'s `prebuild` re-runs NSwag — confirm it picked up your regenerated client (no new diff in `frontend/src/api/generated/api-client.ts`; if there is, commit it as an amendment to Task 5).

- [ ] **Step 3: Verify the list loads**

Open `http://localhost:3000` and navigate to **Customer → Issued Invoices** (the route rendered by `frontend/src/pages/customer/IssuedInvoicesPage.tsx`). Confirm the table populates. Open browser DevTools → Network and confirm the list request URL is `/api/invoices?...` and returns HTTP 200.

- [ ] **Step 4: Verify the stats card loads**

On the same page, confirm the sync-stats card at the top renders numeric counts (not "Failed to load sync stats"). Network tab: request URL is `/api/invoices/stats?...`, HTTP 200.

- [ ] **Step 5: Verify the detail modal — sync history must render**

Click any invoice row. The detail modal opens.

- Network tab: confirm the request URL is **exactly** `/api/invoices/<id>?withDetails=true`. If the `?withDetails=true` is missing, Task 3 Step 2 was not saved — go back and fix.
- Scroll the modal. Confirm a "Sync History" section appears and shows the count of sync attempts. If the section is missing for an invoice that previously had history, the `withDetails=true` query parameter is not reaching the handler — investigate before declaring success.

- [ ] **Step 6: Confirm `/api/IssuedInvoices*` returns 404**

In DevTools console (or curl):

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://localhost:5001/api/IssuedInvoices
```

Expected: `404`. (Acceptance for FR-1 — legacy route is gone.)

- [ ] **Step 7: Confirm Swagger no longer lists the legacy routes**

Open `https://localhost:5001/swagger`. Search the page for `IssuedInvoices`. Expected: zero matches. The three `/api/invoices/*` routes should still be listed under the **Invoices** tag.

- [ ] **Step 8: Stop both servers**

`Ctrl+C` in each terminal.

---

### Task 8: Final verification and PR-readiness checks

**Why this task exists:** Combines the project's CLAUDE.md "Validation before completion" gate with the FR-level acceptance criteria into a single before-PR checklist.

- [ ] **Step 1: Re-run the full backend build, format, and tests**

```bash
dotnet build backend/src/Anela.Heblo.API
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests --no-build
```

Expected: build green, formatter reports no changes, all tests pass. If `dotnet format --verify-no-changes` reports diffs, run `dotnet format backend/Anela.Heblo.sln`, commit the formatting fix with `style: dotnet format`, and re-run this step.

- [ ] **Step 2: Re-run frontend build + lint**

```bash
cd frontend
npm run build
npm run lint
cd ..
```

Expected: build succeeds, lint passes.

- [ ] **Step 3: Final grep sweep — no legacy strings anywhere in shipped code**

```bash
grep -rn "IssuedInvoicesController\|api/IssuedInvoices\|issuedInvoices_" backend/src frontend/src || echo "OK: clean"
```

Expected: `OK: clean`. (Hits inside `backend/test/` are acceptable only if they reference the handler/MediatR request names like `GetIssuedInvoiceDetailHandler`, never the controller or the legacy URL — verify by reading any match.)

- [ ] **Step 4: Review the diff**

```bash
git log --oneline origin/main..HEAD
git diff origin/main...HEAD --stat
```

Expected commits (in order):

1. `test: pin empty-id validation in GetIssuedInvoiceDetailHandler`
2. `refactor: delete duplicate IssuedInvoicesController`
3. `fix(frontend): repoint issued-invoice hooks to /api/invoices`
4. `chore: regenerate OpenAPI TypeScript client`

Expected files in the stat:

- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs` — **deleted**
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added
- `frontend/src/api/hooks/useIssuedInvoices.ts` — modified
- `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts` — modified
- `frontend/src/api/generated/api-client.ts` — modified (regenerated)

No other files should appear. If they do, the change drifted out of scope — investigate before pushing.

- [ ] **Step 5: Push the branch**

```bash
git push -u origin feat-arch-review-invoices-issuedinvoicescontr
```

- [ ] **Step 6: Open the PR with the follow-up notes called out in the arch review**

Use `gh pr create` with a body that mentions:

- Pre-existing, **out of scope** for this PR: neither controller carries `[Authorize]`; both surfaces are anonymous. Recommend a separate issue to add `[Authorize]` to `InvoicesController` matching the rest of the API project.
- Pre-existing, **out of scope** for this PR: both hooks still use the documented `(apiClient as any).http.fetch` / `(apiClient as any).baseUrl` anti-pattern (see `docs/development/api-client-generation.md`). Migrating to typed `apiClient.invoices_*` calls is a clean isolated follow-up.
- FR-6 note: no controller-level tests targeted the legacy `/api/IssuedInvoices*` routes (`grep` confirmed). The "migrate, don't drop" requirement is a no-op; the suite is unchanged on that axis.

```bash
gh pr create --title "refactor: consolidate issued-invoice HTTP surface onto /api/invoices" --body "$(cat <<'EOF'
## Summary
- Delete duplicate `IssuedInvoicesController` (`/api/IssuedInvoices`) — its three actions were already exposed cleanly by `InvoicesController` at `/api/invoices`.
- Repoint three frontend hooks: list and stats to the new paths, detail call now explicitly passes `?withDetails=true` to preserve sync-history loading (the legacy controller hardcoded this; the surviving controller defaults it to `false`).
- Regenerate `api-client.ts` — `issuedInvoices_*` methods are gone.
- Add a unit test pinning the empty-id `ValidationError` guarantee in `GetIssuedInvoiceDetailHandler`, which was previously duplicated in the deleted controller.

## Out of scope (follow-ups noted in arch review)
- Both `InvoicesController` and the deleted controller are anonymous (no `[Authorize]`). Pre-existing — file a separate issue.
- Hooks still use `(apiClient as any).http.fetch` (documented anti-pattern). Pre-existing — file a separate issue.
- FR-6 "migrate, don't drop" controller tests: no such tests existed; trivially satisfied.

## Test plan
- [x] `dotnet build backend/src/Anela.Heblo.API` — green
- [x] `dotnet format backend/Anela.Heblo.sln --verify-no-changes` — clean
- [x] `dotnet test backend/test/Anela.Heblo.Tests` — all pass, new `GetIssuedInvoiceDetailHandlerTests` covers empty/whitespace/null id
- [x] `npm run lint` + `npm run build` in `frontend/` — clean
- [x] Manual smoke: list, detail modal (sync history renders), and stats card all load against the new routes
- [x] `curl https://localhost:5001/api/IssuedInvoices` returns 404; Swagger no longer lists `IssuedInvoices` tag
EOF
)"
```

---

## Self-review notes (already applied)

- **Spec coverage check**: FR-1 → Task 2; FR-2 → Tasks 2 + 7 (controller untouched, single route per op in Swagger); FR-3 → Task 1; FR-4 → Tasks 3 + 4 (with the arch-review correction for `?withDetails=true`); FR-5 → Task 5; FR-6 → Task 2 Step 4 + Task 8 Step 3 (the no-op grep). All spec requirements have at least one task.
- **Placeholder scan**: no TBDs, no "add appropriate validation", every code change shows the exact code, every command shows expected output.
- **Type/method consistency**: `GenerateFrontendClientManual` target name matches `Anela.Heblo.API.csproj:92`; method names `invoices_GetInvoicesList` / `invoices_GetInvoiceDetail` / `invoices_GetSyncStats` match the existing entries in `frontend/src/api/generated/api-client.ts:3837-3944`; `issuedInvoices_GetList` / `_GetDetail` / `_GetSyncStats` match `:4097-4201` (to be removed). `ErrorCodes.ValidationError` matches `Anela.Heblo.Application.Shared.ErrorCodes`. Test framework choice (xUnit + FluentAssertions + Moq) matches the sibling `GetIssuedInvoicesListHandlerPaginationTests.cs`.
