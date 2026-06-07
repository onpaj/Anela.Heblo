# Consistent Invalid-Input Handling in `GetExpeditionListsByDateHandler` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `GetExpeditionListsByDateHandler` return a failed response (`Success = false`, `ErrorCode = InvalidFormat`) on a malformed `Date` input so the controller surfaces HTTP `400 Bad Request` instead of a misleading `200 OK` with an empty list.

**Architecture:** Adopt the canonical error pattern used by 35+ handlers across the codebase: populate `BaseResponse.Success`, `BaseResponse.ErrorCode`, and `BaseResponse.Params` on the existing `GetExpeditionListsByDateResponse`, then route the controller action through `BaseApiController.HandleResponse<T>()` which maps `ErrorCodes.InvalidFormat` to `400` via its `[HttpStatusCode]` attribute. No new DTO fields, no `Fail(string)` factory, no controller-level `if (!Success)` branch.

**Tech Stack:** .NET 8, MediatR, ASP.NET Core MVC controllers, xUnit + Moq for unit tests.

**Why canonical over "mirror siblings":** The two sibling handlers (`Download`, `Reprint`) carry a local `ErrorMessage` + `Fail(string)` outlier that the rest of the codebase does not use. The arch-review (`artifacts/feat-arch-review-expeditionlistarchive-invali/arch-review.r1.md` §"Specification Amendments") amended the spec to direct this fix at the canonical pattern instead of extending the outlier. Sibling migration is out of scope.

---

## File Structure

**Modify (3 files):**

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — replace the invalid-date early return (lines 21-24) with a canonical failed response.
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` — change `return Ok(response);` in `GetByDate` (line 38) to `return HandleResponse(response);`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` — add `Handle_ReturnsFailure_WhenDateIsInvalid` test that asserts the failure response and verifies `IBlobStorageService` is never called.

**Do NOT modify:**

- `GetExpeditionListsByDateResponse.cs` — no new property, no `Fail` factory. The DTO already inherits everything required from `BaseResponse`.
- `BaseResponse.cs` — already exposes `Success`, `ErrorCode`, `Params`.
- `ErrorCodes.cs` — `InvalidFormat` already exists with `[HttpStatusCode(BadRequest)]` (line 18-19).
- The two sibling handlers/responses (`Download`, `Reprint`) — explicitly out of scope per the spec amendments.

**Reference (read but do not edit):**

- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:29-60` — `HandleResponse<T>` mapping logic.
- `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs` — fields populated on failure.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:18-19` — `InvalidFormat` with `BadRequest` mapping.

---

## Task 1: Add failing handler test for invalid date

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs`

The existing test class already wires up an `IBlobStorageService` mock and instantiates the handler. We add one test covering the invalid-date branch. It asserts the canonical failure shape **and** that the storage call is never made.

- [ ] **Step 1.1: Add `using` for `Anela.Heblo.Application.Shared` to the test file (top of file)**

Open `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` and ensure these `using`s are present at the top (add the missing one):

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

The new line is `using Anela.Heblo.Application.Shared;` (needed for `ErrorCodes`). The rest already exist.

- [ ] **Step 1.2: Append the invalid-date test to the test class**

Add this test method as a new `[Fact]` inside the `GetExpeditionListsByDateHandlerTests` class, just below the existing `Handle_FiltersPdfFilesOnly` test:

```csharp
[Theory]
[InlineData("not-a-date")]
[InlineData("2026/03/25")]
[InlineData("25-03-2026")]
[InlineData("")]
[InlineData(null)]
public async Task Handle_ReturnsFailure_WhenDateIsInvalid(string? invalidDate)
{
    // Arrange
    var request = new GetExpeditionListsByDateRequest { Date = invalidDate ?? string.Empty };

    // Act
    var result = await _handler.Handle(request, default);

    // Assert
    Assert.NotNull(result);
    Assert.False(result.Success);
    Assert.Equal(ErrorCodes.InvalidFormat, result.ErrorCode);
    Assert.NotNull(result.Params);
    Assert.Equal("Date", result.Params!["Field"]);
    Assert.Equal("yyyy-MM-dd", result.Params!["ExpectedFormat"]);
    Assert.Empty(result.Items);

    _blobStorageServiceMock.Verify(
        s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

Notes:
- `[Theory]` with multiple inputs covers FR-2's three cases (`"not-a-date"`, `null`, empty) in one test method.
- `request.Date` is `string` (not nullable) per `GetExpeditionListsByDateRequest.cs:7`, so we coalesce `null` → `string.Empty` before assigning. `TryParseExact` still rejects empty.
- We assert exact `Params` keys/values to lock the contract that the controller (and downstream frontend) sees. If the implementer chooses different keys, this test will catch the drift.

- [ ] **Step 1.3: Run the new test and verify it FAILS**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.Handle_ReturnsFailure_WhenDateIsInvalid" \
  --nologo --verbosity minimal
```

Expected: the test runs but FAILS — current handler returns `Success = true` and `ErrorCode = null` (it just builds `new GetExpeditionListsByDateResponse { Items = new List<ExpeditionListItemDto>() }`). At least the `Assert.False(result.Success)` assertion fails. Do not proceed until you observe this red.

- [ ] **Step 1.4: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
git commit -m "test: add failing test for invalid-date branch in GetExpeditionListsByDateHandler"
```

---

## Task 2: Make the handler return a canonical failed response on invalid date

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs:21-24`

- [ ] **Step 2.1: Add the `using` for `Anela.Heblo.Application.Shared`**

The handler file currently has:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;
```

Add `using Anela.Heblo.Application.Shared;` so the handler can reference `ErrorCodes`. The final `using` block should be:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;
```

- [ ] **Step 2.2: Replace the invalid-date early return**

In `GetExpeditionListsByDateHandler.Handle`, replace this block (lines 21-24 of the current file):

```csharp
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return new GetExpeditionListsByDateResponse { Items = new List<ExpeditionListItemDto>() };
}
```

with:

```csharp
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return new GetExpeditionListsByDateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.InvalidFormat,
        Params = new Dictionary<string, string>
        {
            { "Field", "Date" },
            { "ExpectedFormat", "yyyy-MM-dd" }
        }
    };
}
```

Rationale:
- `Success`, `ErrorCode`, and `Params` are all inherited from `BaseResponse` — no DTO changes needed.
- The `Params` keys (`Field`, `ExpectedFormat`) deliberately do **not** include the raw `request.Date` value (per arch-review §"Decision 4" and spec NFR-2: do not echo user input).
- `Items` is left at its default (empty list initializer on the DTO).

Do not log anything from the invalid-input branch — `BaseApiController.HandleResponse<T>` already logs a warning on failed responses (`BaseApiController.cs:39-41`), so adding handler-level logging would duplicate the warning.

- [ ] **Step 2.3: Run the previously-failing test and verify it PASSES**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.Handle_ReturnsFailure_WhenDateIsInvalid" \
  --nologo --verbosity minimal
```

Expected: PASS — all five `[InlineData]` cases pass.

- [ ] **Step 2.4: Run the full handler test class to confirm valid-date tests still pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests" \
  --nologo --verbosity minimal
```

Expected: PASS — `Handle_ReturnsItemsForDate`, `Handle_FiltersPdfFilesOnly`, and the new `Handle_ReturnsFailure_WhenDateIsInvalid` (5 inline cases) all pass. Total ~7 passing tests.

- [ ] **Step 2.5: Commit the handler change**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs
git commit -m "fix: return canonical InvalidFormat failure on bad date in GetExpeditionListsByDateHandler"
```

---

## Task 3: Switch controller action to `HandleResponse<T>`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs:33-39`

The handler now produces a correct failure response, but `GetByDate` still wraps it in `Ok(response)` which forces HTTP `200`. Route it through `HandleResponse` so `ErrorCodes.InvalidFormat` (carrying `[HttpStatusCode(BadRequest)]`) maps to `400`.

- [ ] **Step 3.1: Change `Ok(response)` to `HandleResponse(response)` in `GetByDate`**

Find this block in `ExpeditionListArchiveController.cs` (lines 33-39):

```csharp
[HttpGet("{date}")]
public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
{
    var request = new GetExpeditionListsByDateRequest { Date = date };
    var response = await _mediator.Send(request);
    return Ok(response);
}
```

Replace it with:

```csharp
[HttpGet("{date}")]
public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
{
    var request = new GetExpeditionListsByDateRequest { Date = date };
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

Only the final return statement changes. Do not touch the other actions (`GetDates`, `Download`, `Reprint`) — their migration to the canonical pattern is out of scope per the spec amendments.

`HandleResponse<T>` lives on the `BaseApiController` that this controller already inherits from (`ExpeditionListArchiveController : BaseApiController`, line 14), so no new `using` directive is needed.

- [ ] **Step 3.2: Build the API project to confirm no compile errors**

```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo --verbosity minimal
```

Expected: `Build succeeded` with `0 Error(s)`. Warnings are acceptable if pre-existing.

- [ ] **Step 3.3: Commit the controller change**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs
git commit -m "fix: route GetByDate through HandleResponse so invalid date yields 400"
```

---

## Task 4: Verify the full test suite and formatting

This step is the global gate from `CLAUDE.md` §"Validation before completion": `dotnet build` + `dotnet format` + tests touched by the change must pass.

- [ ] **Step 4.1: Run all tests in the `ExpeditionListArchive` namespace**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ExpeditionListArchive" \
  --nologo --verbosity minimal
```

Expected: all `ExpeditionListArchive` test classes pass — `GetExpeditionListsByDateHandlerTests`, `GetExpeditionDatesHandlerTests`, `DownloadExpeditionListHandlerTests`, `ReprintExpeditionListHandlerTests`. No new failures in any sibling test.

- [ ] **Step 4.2: Run the full backend test suite**

```bash
cd backend
dotnet test --nologo --verbosity minimal
```

Expected: green. If any unrelated test fails, that's a pre-existing condition — note it in the PR description but do not fix it inside this change (per `CLAUDE.md` §"Surgical changes").

- [ ] **Step 4.3: Build the full backend solution**

```bash
cd backend
dotnet build --nologo --verbosity minimal
```

Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 4.4: Run `dotnet format` and stage any formatting fixes**

```bash
cd backend
dotnet format --verbosity diagnostic
```

If `dotnet format` modifies the three files this plan touched, review the diff (`git diff`) and stage the changes:

```bash
git add -u backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs \
          backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs \
          backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
```

If `dotnet format` modifies *other* files (because the formatter detected pre-existing whitespace issues), **revert those** — they are outside the scope of this change:

```bash
git checkout -- <unrelated-file>
```

- [ ] **Step 4.5: Commit formatting fixes if any**

```bash
git diff --cached --quiet || git commit -m "chore: apply dotnet format to changes"
```

(This commit is a no-op if `dotnet format` made no changes to the staged files.)

---

## Task 5: Sanity-check the frontend consumer (read-only verification)

Per the arch-review's "Risks" table (Medium-severity risk), a frontend page that previously got `200 OK + empty items` may regress now that invalid dates yield `400`. This task is **investigation only** — no code edits — to confirm the frontend already handles `400` gracefully via its global error handling.

- [ ] **Step 5.1: Locate the frontend hook that calls this endpoint**

```bash
cd frontend
grep -r --include='*.ts' --include='*.tsx' -l "expedition-list-archive" src/
```

Expected: one or two hook/component files (likely under `src/api/hooks/` and `src/components/expedition*`). Identify the hook that calls `GET /api/expedition-list-archive/{date}`.

- [ ] **Step 5.2: Inspect the hook for `400`-handling**

Open the matching file(s) and confirm one of the following:
- The hook uses `apiClient` / TanStack Query and surfaces errors via the project's global error handler (typical pattern — toast on non-2xx response).
- Or the hook has its own `onError` / try-catch that doesn't crash on `400`.

This is a sanity check, not a code change. If the hook **assumes** `200` and unwraps `response.items` without checking `response.success`, **note it in the PR description** as a follow-up, but do not modify it inside this PR (out of scope per spec §"Out of scope: Frontend UX changes").

- [ ] **Step 5.3: Record the finding**

Add one line to the PR description draft (kept in your head or scratch space for the final PR creation): either "Frontend uses global error handler, `400` is rendered as a toast — no regression expected" or "Frontend assumes 200; file follow-up issue for invalid-date UX".

No commit here — this is reconnaissance.

---

## Self-Review (executed by plan author before saving)

**1. Spec coverage:**

| Spec section | Covered by |
|---|---|
| FR-1 (Fail factory) — amended away by arch-review §"Specification Amendments" point 1 | Not implemented (intentional; canonical pattern instead). |
| FR-2 (Handler returns failed response on invalid `Date`) | Task 2.2 — amended to `ErrorCode = InvalidFormat` + `Params` per arch-review point 2. |
| FR-3 (Controller surfaces failure as HTTP 400) | Task 3.1 — amended to `HandleResponse(response)` per arch-review point 3. |
| FR-4 (Tests cover both branches) | Task 1 — invalid-date `[Theory]` covers FR-2 cases incl. `null`/empty; existing `Handle_ReturnsItemsForDate` and `Handle_FiltersPdfFilesOnly` already cover the valid branch. Task 4.1 verifies both still pass. |
| NFR-1 (Performance) | Implicitly — the change shortcuts an already-cheap branch and avoids one storage call. No measurement task needed. |
| NFR-2 (Security: don't echo raw user input) | Task 2.2 — `Params` uses constants `Field=Date`, `ExpectedFormat=yyyy-MM-dd`. Raw `request.Date` is never echoed. |
| NFR-3 (Backwards compatibility) | Task 5 — read-only frontend sanity check. The shape change (`200 → 400` on bad input) is the intended bug fix. |
| NFR-4 (Consistency) | Achieved at the **codebase** level (canonical pattern), not the module level — call out in PR description per arch-review §"Risks". |

No gaps.

**2. Placeholder scan:**

Searched for "TBD", "TODO", "implement later", "fill in details", "add appropriate", "similar to". No matches. Every step has either a concrete command, a concrete code block, or a concrete file edit.

**3. Type consistency:**

- `ErrorCodes.InvalidFormat` — used in Task 1.2 (test assert), Task 2.2 (handler). Same enum value.
- `Params` keys: `"Field"`, `"ExpectedFormat"` — same in Task 1.2 (`result.Params!["Field"]`, `result.Params!["ExpectedFormat"]`) and Task 2.2 (dictionary initializer). Consistent.
- `HandleResponse(response)` — method exists on `BaseApiController` (verified at `BaseApiController.cs:29`). Controller already inherits.
- `GetExpeditionListsByDateRequest.Date` — `string` (not nullable), verified at `GetExpeditionListsByDateRequest.cs:7`. Task 1.2 coalesces `null` → `string.Empty` to satisfy the type.
- `_blobStorageServiceMock.Verify(...)` signature — `ListBlobsAsync(string, string, CancellationToken)` matches the call site in `GetExpeditionListsByDateHandler.cs:26`.

All consistent.

---

## Definition of Done

- [ ] `Handle_ReturnsFailure_WhenDateIsInvalid` test (all 5 `[InlineData]` cases) passes.
- [ ] Existing `Handle_ReturnsItemsForDate` and `Handle_FiltersPdfFilesOnly` tests still pass.
- [ ] Full backend test suite passes (`dotnet test` green).
- [ ] `dotnet build` succeeds with 0 errors.
- [ ] `dotnet format` produces no diff on the three modified files.
- [ ] Manual smoke check (optional — implementer's call): hitting `GET /api/expedition-list-archive/not-a-date` against a local backend returns HTTP `400` with body `{ "success": false, "errorCode": "InvalidFormat", "params": { "Field": "Date", "ExpectedFormat": "yyyy-MM-dd" }, "items": [] }`, while `GET /api/expedition-list-archive/2026-06-04` still returns `200`.
- [ ] No changes to `GetExpeditionListsByDateResponse.cs`, `BaseResponse.cs`, `ErrorCodes.cs`, or sibling handlers/controllers.
