# Remove unused `ICurrentUserService` dependency from `GridLayoutsController` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the dead `ICurrentUserService` constructor parameter, field, and unused `using` directive from `GridLayoutsController.cs`, so its declared dependencies match its actual behavior.

**Architecture:** Pure removal in a single MVC controller. Identity resolution stays where it already lives — inside `GetGridLayoutHandler`, `SaveGridLayoutHandler`, and `ResetGridLayoutHandler`, which each inject `ICurrentUserService` themselves. The controller continues to be a thin HTTP-to-mediator adapter guarded by `[Authorize]`. No interface, contract, DI registration, handler, or HTTP surface changes.

**Tech Stack:** .NET 8, ASP.NET Core MVC, MediatR, xUnit (existing handler tests), `dotnet build`, `dotnet format`, `dotnet test`.

---

## File Structure

**Files modified (exactly one):**
- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — controller class. Drop the `ICurrentUserService` constructor parameter, the `_currentUserService` field, the constructor assignment, and the now-unused `using Anela.Heblo.Domain.Features.Users;` directive. Action methods (`Get`, `Save`, `Reset`) and class-level attributes (`[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]`) remain untouched.

**Files NOT modified (called out so the executor does not drift):**
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs`
- Any file under `backend/src/Anela.Heblo.Domain/Features/Users/`.
- DI registration code (`Program.cs`, module composition).
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/` — three existing **handler** tests (`GetGridLayoutHandlerTests.cs`, `SaveGridLayoutHandlerTests.cs`, `ResetGridLayoutHandlerTests.cs`). They are not controller tests and must keep passing as-is.

**No new files. No new tests.** Per spec §Testing, no controller tests exist (`grep` confirmed: `GridLayoutsController` is only referenced in its own source file), and a pure removal of unused code does not introduce new behavior worth testing. Validation is performed via the standard build/format/test gates, not by adding new assertions.

---

## Task 1: Remove the unused `ICurrentUserService` dependency from `GridLayoutsController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs`

**Reference — current file contents (lines 1–53, before edit):**

```csharp
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GridLayoutsController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public GridLayoutsController(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    [HttpGet("{gridKey}")]
    public async Task<ActionResult<GridLayoutDto?>> Get(string gridKey)
    {
        var request = new GetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        return Ok(response.Layout);
    }

    [HttpPut("{gridKey}")]
    public async Task<ActionResult> Save(string gridKey, [FromBody] SaveGridLayoutRequest body)
    {
        body.GridKey = gridKey;
        var response = await _mediator.Send(body);
        if (!response.Success)
            return StatusCode(500, response);
        return Ok();
    }

    [HttpDelete("{gridKey}")]
    public async Task<ActionResult> Reset(string gridKey)
    {
        var request = new ResetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        if (!response.Success)
            return StatusCode(500, response);
        return Ok();
    }
}
```

- [ ] **Step 1: Verify baseline build is green**

Run from the worktree root (`/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-users-gridlayoutscontroller-`):

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)` (or the count the executor sees pre-change — record it so any new warning introduced by the edit is detectable).

If the baseline already has warnings, note the exact count. The post-edit count must not increase.

- [ ] **Step 2: Remove the unused `using` directive (line 5)**

Use the `Edit` tool on `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` to delete the line:

```csharp
using Anela.Heblo.Domain.Features.Users;
```

Exact `Edit`:
- `old_string`:
  ```
  using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
  using Anela.Heblo.Domain.Features.Users;
  using MediatR;
  ```
- `new_string`:
  ```
  using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
  using MediatR;
  ```

Rationale: that `using` only exists to resolve `ICurrentUserService`. After the field removal it would be flagged by analyzers / `dotnet format` as unused. Removing it in the same step keeps the file in one consistent state.

- [ ] **Step 3: Remove the `_currentUserService` field and update the constructor**

Use the `Edit` tool on the same file to collapse the field, constructor parameter, and constructor assignment in a single replacement.

Exact `Edit`:
- `old_string`:
  ```csharp
      private readonly IMediator _mediator;
      private readonly ICurrentUserService _currentUserService;

      public GridLayoutsController(IMediator mediator, ICurrentUserService currentUserService)
      {
          _mediator = mediator;
          _currentUserService = currentUserService;
      }
  ```
- `new_string`:
  ```csharp
      private readonly IMediator _mediator;

      public GridLayoutsController(IMediator mediator)
      {
          _mediator = mediator;
      }
  ```

Do not touch the three action methods, the class attributes, the namespace declaration, or the class declaration.

- [ ] **Step 4: Re-read the file and confirm the post-edit shape**

Read `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` end-to-end. It must now look exactly like:

```csharp
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GridLayoutsController : BaseApiController
{
    private readonly IMediator _mediator;

    public GridLayoutsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{gridKey}")]
    public async Task<ActionResult<GridLayoutDto?>> Get(string gridKey)
    {
        var request = new GetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        return Ok(response.Layout);
    }

    [HttpPut("{gridKey}")]
    public async Task<ActionResult> Save(string gridKey, [FromBody] SaveGridLayoutRequest body)
    {
        body.GridKey = gridKey;
        var response = await _mediator.Send(body);
        if (!response.Success)
            return StatusCode(500, response);
        return Ok();
    }

    [HttpDelete("{gridKey}")]
    public async Task<ActionResult> Reset(string gridKey)
    {
        var request = new ResetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        if (!response.Success)
            return StatusCode(500, response);
        return Ok();
    }
}
```

Checklist while reading:
- `using Anela.Heblo.Domain.Features.Users;` is **gone**.
- `_currentUserService` does **not** appear anywhere in the file (use `Grep` `_currentUserService` against this single file if unsure — expect zero matches).
- Constructor signature is `public GridLayoutsController(IMediator mediator)` — single parameter.
- `[Authorize]`, `[ApiController]`, and `[Route("api/[controller]")]` are still on the class.
- All three action methods are byte-for-byte unchanged.

If any item fails, fix it with `Edit` before continuing.

- [ ] **Step 5: Confirm no other file still references the controller with two args**

Run:

```bash
grep -rn "new GridLayoutsController(" backend/ || echo "no direct construction sites"
grep -rn "_currentUserService" backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs || echo "field fully removed"
```

Expected:
- First command prints `no direct construction sites` (controllers are activated by the framework, not constructed by hand — pre-verified by the arch-review).
- Second command prints `field fully removed`.

If either expectation fails, stop and re-read the file. Either there is a hidden construction site (rare — investigate before continuing) or the edit was not applied cleanly.

- [ ] **Step 6: Build the solution and verify zero new warnings**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected:
- `Build succeeded.` with `0 Error(s)`.
- Warning count is `<=` the baseline from Step 1. Specifically, no new warning of category `CS8019` ("Unnecessary using directive"), `IDE0005`, or anything mentioning `Anela.Heblo.Domain.Features.Users` / `ICurrentUserService`.

If build fails:
- A "type or namespace `ICurrentUserService` could not be found" error means the field or constructor parameter was only partially removed — re-open the file and finish the removal.
- A new `CS8019` / `IDE0005` warning means the `using` was not removed — re-apply Step 2.

- [ ] **Step 7: Run `dotnet format` and verify no diff**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code `0`, no files reported as needing formatting.

If the command reports formatting differences:
1. Run `dotnet format backend/Anela.Heblo.sln` to apply them.
2. `git diff backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — only whitespace / using-ordering changes inside this file are acceptable.
3. Re-run `dotnet format backend/Anela.Heblo.sln --verify-no-changes` until it passes.

Do not let `dotnet format` rewrite unrelated files. If it touches files outside `GridLayoutsController.cs`, revert those (`git checkout -- <path>`) — the surgical-changes rule from `CLAUDE.md` applies.

- [ ] **Step 8: Run the backend test suite**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests pass. In particular, the existing handler tests under `backend/test/Anela.Heblo.Tests/Features/GridLayouts/` (`GetGridLayoutHandlerTests`, `SaveGridLayoutHandlerTests`, `ResetGridLayoutHandlerTests`) must remain green — they exercise handler behavior and do not touch the controller, so this edit cannot affect them.

If any test fails:
- If the failure is in a `GridLayouts*HandlerTests` file or any test that constructs `GridLayoutsController` directly, stop and investigate — the spec asserts no such construction sites exist, so a failure here means the assumption is wrong and the edit must be reconsidered.
- If the failure is unrelated to GridLayouts and was already failing on `main`, capture it and confirm with the user that it predates this change. Do not "fix" unrelated test failures inside this PR.

- [ ] **Step 9: Sanity-check the diff scope**

Run:

```bash
git status
git diff --stat
git diff backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs
```

Expected `git diff --stat` output: exactly one file changed — `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — with roughly `-5` / `+0` line counts (one `using`, one field, one constructor-parameter rewrite that collapses to a single-parameter form, one assignment).

Expected `git diff` content: only the four removals described above; no whitespace churn elsewhere; no edits to action methods or class attributes.

If the diff includes anything outside this file, revert the extra files: `git checkout -- <path>`.

- [ ] **Step 10: Commit**

Run:

```bash
git add backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs
git commit -m "refactor: remove unused ICurrentUserService from GridLayoutsController

The controller declared and assigned ICurrentUserService but never read it.
Identity resolution lives in Get/Save/ResetGridLayoutHandler, which each
inject ICurrentUserService themselves. Removing the dead constructor
parameter, field, and the now-unused using directive aligns the controller's
declared dependencies with its actual behavior. HTTP contract, [Authorize]
attribute, DI registration, and handler signatures are unchanged."
```

Expected: a single commit touching exactly one file.

---

## Spec Coverage Check

| Spec requirement | Where it's covered |
|---|---|
| FR-1: Constructor takes only `IMediator` | Step 3 (constructor rewrite) + Step 4 (post-edit verification) |
| FR-2: `_currentUserService` field removed | Step 3 + Step 4 + Step 5 (grep confirmation) |
| FR-3: `using Anela.Heblo.Domain.Features.Users;` removed | Step 2 + Step 4 + Step 6 (no `CS8019`/`IDE0005`) |
| FR-4: Action method behavior preserved | Step 4 (full-file re-read against expected shape) + Step 9 (diff scope check) |
| FR-5: Handlers untouched | "Files NOT modified" list + Step 9 diff stat (one file only) |
| NFR-1: Performance | No code touched that affects perf; implicit |
| NFR-2: Security — `[Authorize]` retained, handler identity reads retained | Step 4 (attribute still present) + "Files NOT modified" |
| NFR-3: Compatibility — routes, verbs, DTOs, DI unchanged | Step 4 (action methods byte-equal) + Step 5 (no construction sites) |
| NFR-4: Validation gates (`dotnet build`, `dotnet format`, `dotnet test`) | Steps 6, 7, 8 |
| Out of Scope: handlers, other controllers, frontend, DI registration, new tests | "Files NOT modified" + Step 9 diff-scope guard |
