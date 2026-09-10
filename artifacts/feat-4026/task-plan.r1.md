# Typed GroupBy for PackingMaterials Daily Consumption Breakdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the raw `string` `GetDailyConsumptionBreakdownRequest.GroupBy` (and its
three-way duplicated `HashSet<string>`/switch/controller-default validation) with a typed
`ConsumptionGroupBy` enum, letting ASP.NET Core's native model binding validate it.

**Architecture:** Add a new `ConsumptionGroupBy` enum (`Material`/`Product`/`Order`) to
`Application/Features/PackingMaterials/Contracts/`. Change
`GetDailyConsumptionBreakdownRequest.GroupBy` and the controller's `[FromQuery] groupBy`
parameter to that enum type. Remove the handler's `ValidGroupByValues` HashSet guard and
switch the dispatch `switch` from string arms to enum arms. Response DTO's `GroupBy` field
stays `string`, now populated via `.ToString()`. See
`artifacts/feat-4026/arch-review.r1.md` (Decisions 1–4) for the rationale behind every
choice below — this plan does not re-litigate them, only implements them.

**Tech Stack:** .NET 8, ASP.NET Core (MVC controllers), MediatR, xUnit.

---

### task: add-consumption-groupby-enum

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public enum ConsumptionGroupBy
{
    Material,
    Product,
    Order
}
```

- [ ] **Step 2: Build to confirm the new file compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: `Build succeeded.` — the enum has no dependencies and nothing references it yet,
so this step only confirms the file itself is syntactically valid and in the right
namespace/assembly.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs
git commit -m "feat(packing-materials): add ConsumptionGroupBy enum"
```

---

### task: retype-request-groupby

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownRequest.cs`

Current content (for reference — this is the whole file):

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownRequest : IRequest<GetDailyConsumptionBreakdownResponse>
{
    public DateOnly Date { get; set; }
    public string GroupBy { get; set; } = "material";
}
```

- [ ] **Step 1: Replace the file's contents**

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownRequest : IRequest<GetDailyConsumptionBreakdownResponse>
{
    public DateOnly Date { get; set; }
    public ConsumptionGroupBy GroupBy { get; set; } = ConsumptionGroupBy.Material;
}
```

- [ ] **Step 2: Build (expect failures — this is expected at this point in the plan)**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: **FAIL**. `GetDailyConsumptionBreakdownHandler.cs` still compares
`request.GroupBy` (now `ConsumptionGroupBy`) against `string` values (`ValidGroupByValues.Contains`,
`request.GroupBy.ToLowerInvariant()`, and the response's `GroupBy = request.GroupBy`
assignment against a `string`-typed response field) — all now type errors. This is
expected; the next task (`retype-handler-groupby-dispatch`) fixes the handler. Do not treat
this failure as a problem to solve in this task — just confirm the errors are exactly the
ones described (in `GetDailyConsumptionBreakdownHandler.cs`), not something unrelated.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownRequest.cs
git commit -m "feat(packing-materials): retype GetDailyConsumptionBreakdownRequest.GroupBy to ConsumptionGroupBy"
```

(A commit with a known, expected-to-fail intermediate build is acceptable here because the
fix lands in the very next task and both land in the same PR before merge — this mirrors
the plan's TDD-style task boundaries rather than "always green" per commit. If your
workflow requires every commit to build, squash this task's commit with the next one
instead of pushing it standalone.)

---

### task: retype-handler-groupby-dispatch

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs`

- [ ] **Step 1: Remove the `ValidGroupByValues` field**

Delete these lines (currently lines 11–14):

```csharp
    private static readonly HashSet<string> ValidGroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "material", "product", "order"
    };
```

- [ ] **Step 2: Remove the runtime validation block at the top of `Handle`**

Delete this block (currently the first statements inside `Handle`, before the `try`):

```csharp
        if (!ValidGroupByValues.Contains(request.GroupBy))
        {
            return new GetDailyConsumptionBreakdownResponse
            {
                Success = false,
                Error = $"Invalid GroupBy value '{request.GroupBy}'. Must be one of: material, product, order.",
                Date = request.Date,
                GroupBy = request.GroupBy
            };
        }

```

It is no longer reachable: an invalid `groupBy` query value now fails ASP.NET Core model
binding at the controller boundary before `Handle` is ever called (see
`retype-controller-groupby-param` task and `arch-review.r1.md` Decision 3).

- [ ] **Step 3: Fix the three remaining `GroupBy` reads/assignments inside `Handle`**

Change the empty-consumptions early return from:

```csharp
            if (consumptions.Count == 0)
                return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy };
```

to:

```csharp
            if (consumptions.Count == 0)
                return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy.ToString() };
```

Change the dispatch switch from:

```csharp
            var groups = request.GroupBy.ToLowerInvariant() switch
            {
                "material" => BuildGroupByMaterial(consumptions, materials),
                "product" => BuildGroupByProduct(consumptions, materials),
                "order" => BuildGroupByOrder(consumptions, materials),
                _ => throw new InvalidOperationException($"Unhandled GroupBy value: {request.GroupBy}")
            };
```

to:

```csharp
            var groups = request.GroupBy switch
            {
                ConsumptionGroupBy.Material => BuildGroupByMaterial(consumptions, materials),
                ConsumptionGroupBy.Product => BuildGroupByProduct(consumptions, materials),
                ConsumptionGroupBy.Order => BuildGroupByOrder(consumptions, materials),
                _ => throw new ArgumentOutOfRangeException(nameof(request.GroupBy), request.GroupBy, "Unhandled GroupBy value.")
            };
```

Change the success return from:

```csharp
            return new GetDailyConsumptionBreakdownResponse
            {
                Success = true,
                Date = request.Date,
                GroupBy = request.GroupBy,
                Groups = groups
            };
```

to:

```csharp
            return new GetDailyConsumptionBreakdownResponse
            {
                Success = true,
                Date = request.Date,
                GroupBy = request.GroupBy.ToString(),
                Groups = groups
            };
```

Change the catch block's error return from:

```csharp
            return new GetDailyConsumptionBreakdownResponse
            {
                Success = false,
                Error = "An unexpected error occurred while loading the breakdown.",
                Date = request.Date,
                GroupBy = request.GroupBy
            };
```

to:

```csharp
            return new GetDailyConsumptionBreakdownResponse
            {
                Success = false,
                Error = "An unexpected error occurred while loading the breakdown.",
                Date = request.Date,
                GroupBy = request.GroupBy.ToString()
            };
```

The three private methods `BuildGroupByMaterial`, `BuildGroupByProduct`,
`BuildGroupByOrder` are **not modified** — leave them exactly as they are (they operate on
`List<PackingMaterialConsumption>`/`List<PackingMaterial>`, never touch `GroupBy` directly).

- [ ] **Step 4: Build to confirm the Application project compiles clean**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: `Build succeeded.` with zero errors and zero new warnings.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs
git commit -m "feat(packing-materials): dispatch GetDailyConsumptionBreakdownHandler on ConsumptionGroupBy enum"
```

---

### task: retype-controller-groupby-param

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs:149-162`

- [ ] **Step 1: Change the action's parameter type and default**

Change (currently lines 149–152):

```csharp
    public async Task<ActionResult<GetDailyConsumptionBreakdownResponse>> GetDailyConsumptionBreakdown(
        [FromQuery] string? date,
        [FromQuery] string groupBy = "material",
        CancellationToken cancellationToken = default)
```

to:

```csharp
    public async Task<ActionResult<GetDailyConsumptionBreakdownResponse>> GetDailyConsumptionBreakdown(
        [FromQuery] string? date,
        [FromQuery] ConsumptionGroupBy groupBy = ConsumptionGroupBy.Material,
        CancellationToken cancellationToken = default)
```

The rest of the method body (date parsing, `new GetDailyConsumptionBreakdownRequest { Date
= parsedDate, GroupBy = groupBy }`, the `_mediator.Send` call, and the
`response.Success ? Ok(response) : BadRequest(...)` return) is **unchanged** — it already
just assigns `groupBy` straight into the request, which now type-checks automatically
since both sides are `ConsumptionGroupBy`.

No new `using` is needed: `PackingMaterialsController.cs` line 1 already has
`using Anela.Heblo.Application.Features.PackingMaterials.Contracts;`, which is where
`ConsumptionGroupBy` lives.

- [ ] **Step 2: Build the full backend solution**

Run: `dotnet build backend/Anela.Heblo.sln` (or the solution file's actual path/name if
different — check with `ls backend/*.sln` first)
Expected: `Build succeeded.` with zero errors. This is the first point in the plan where
the full solution (API + Application + Domain + Persistence + Tests) is built together —
confirm the test project also still compiles even though its own content hasn't been
updated yet (it will fail the *next* task's step, not this one, since C# type errors in
test files are compile errors, not build-succeeds-but-tests-fail — see the next task).

Note: if this step reports compile errors inside
`GetDailyConsumptionBreakdownHandlerTests.cs` (string literals like `GroupBy = "material"`
no longer assignable to the enum-typed property), that is expected and is what the next
task (`update-groupby-tests`) fixes — do not attempt to fix test files in this task.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs
git commit -m "feat(packing-materials): bind consumption breakdown groupBy query param as ConsumptionGroupBy"
```

---

### task: update-groupby-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs`

- [ ] **Step 1: Add the Contracts `using` for the enum**

Add to the `using` block at the top of the file (after the existing
`using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;`
line):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

- [ ] **Step 2: Replace the three valid-groupBy string literals with enum values**

In `GroupByMaterial_ReturnsGroupedByMaterialId`, change:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = "material" },
```

to:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Material },
```

In `GroupByOrder_ReturnsGroupedByInvoiceId`, change:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = "order" },
```

to:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Order },
```

In `GroupByProduct_ReturnsEmptyGroups_WhenProductCodeIsNull`, change:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = "product" },
```

to:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Product },
```

In `GroupByMaterial_ExcludesPerDayRowsFromDetails`, change:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = "material" },
```

to:

```csharp
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Material },
```

None of these four tests' assertions change — they still assert on `response.Groups`,
`response.Success`, etc., which are unaffected by the `GroupBy` type change.

- [ ] **Step 3: Replace `GroupBy_InvalidValue_ReturnsError` with an out-of-range-enum test**

This test asserted the now-removed `HashSet` runtime-validation branch, which is no longer
reachable: an invalid `groupBy` can no longer arrive as an arbitrary string (ASP.NET Core
model binding rejects it before the handler runs — see `arch-review.r1.md` Decision 3 /
Risk table). Replace the entire test method:

Delete:

```csharp
    [Fact]
    public async Task GroupBy_InvalidValue_ReturnsError()
    {
        // Arrange
        var repo = BuildRepo(Array.Empty<PackingMaterial>(), Array.Empty<PackingMaterialConsumption>());
        var handler = BuildHandler(repo);

        // Act
        var response = await handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = "invalid" },
            CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Contains("invalid", response.Error, StringComparison.OrdinalIgnoreCase);
    }
```

Replace with:

```csharp
    [Fact]
    public async Task GroupBy_OutOfRangeEnumValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange: an out-of-range enum value can only occur via an unchecked cast — ASP.NET Core's
        // model binder can never produce one for a real HTTP request, but the handler's switch must
        // still fail loudly (not silently) if it ever receives one, e.g. from a future internal caller.
        var repo = BuildRepo(Array.Empty<PackingMaterial>(), new[] { MakeConsumption(1, 5m, invoiceId: "INV-1") });
        var handler = BuildHandler(repo);
        var outOfRangeGroupBy = (ConsumptionGroupBy)99;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = outOfRangeGroupBy },
            CancellationToken.None));
    }
```

Note this new test needs at least one consumption row in the repo (unlike the old test,
which used an empty repo) — the handler's `if (consumptions.Count == 0) return ...` early
exit happens *before* the switch, so an empty-consumptions repo would return successfully
without ever reaching the switch, and the test would not actually exercise the discard arm.
`MakeConsumption` and `BuildRepo` are the existing private helpers already defined earlier
in this file — reuse them as shown, do not redefine them.

- [ ] **Step 4: Run the full PackingMaterials test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"`
Expected: All tests pass, including the four retyped tests and the new
`GroupBy_OutOfRangeEnumValue_ThrowsArgumentOutOfRangeException`. Zero failures.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs
git commit -m "test(packing-materials): update GetDailyConsumptionBreakdownHandler tests for ConsumptionGroupBy enum"
```

---

### task: final-validation

**Files:** none (verification only — no code changes in this task)

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln` (adjust the solution path if `ls backend/*.sln`
showed a different name in the earlier task)
Expected: `Build succeeded.` with zero errors, zero new warnings compared to before this
change.

- [ ] **Step 2: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting diffs reported. If it reports diffs, run
`dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then
re-run the verify command, then `git add -u backend && git commit -m "chore(packing-materials): dotnet format"`
if it made changes.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests pass — not just the `PackingMaterials`-filtered subset from the
previous task, to catch any unexpected ripple effect elsewhere in the solution (there
should be none; `ConsumptionGroupBy` and every file this plan touches are private to the
PackingMaterials module).

- [ ] **Step 4: Confirm no leftover references to the removed validation**

Run: `grep -rn "ValidGroupByValues" backend/`
Expected: no matches (confirms the removed HashSet field and its usages are fully gone,
not just renamed or partially removed).

- [ ] **Step 5: Manual sanity check of the enum's accepted query values (optional but recommended)**

If a local dev instance is available (`docs/development/setup.md`), start the API and hit:
- `GET /api/packing-materials/consumption?date=<any-valid-yyyy-MM-dd>&groupBy=Product` → `200 OK`
- `GET /api/packing-materials/consumption?date=<any-valid-yyyy-MM-dd>` (no `groupBy`) → `200 OK`, grouped by `Material` (the default)
- `GET /api/packing-materials/consumption?date=<any-valid-yyyy-MM-dd>&groupBy=bogus` → `400 Bad Request` with the framework's default `ValidationProblemDetails` body (not the old `{ "error": "Invalid GroupBy value..." }` shape — this is the intentional, reviewed change from `arch-review.r1.md` Decision 3)

This step does not need to be automated as a new integration test — the unit-level
coverage from `update-groupby-tests` plus this manual check is sufficient per
`arch-review.r1.md`'s Risk table, which accepts the framework-default error shape without
requiring new integration test coverage.

- [ ] **Step 6: Note the OpenAPI/frontend client regeneration (no action required)**

Per `docs/development/api-client-generation.md`, the TypeScript client
(`frontend/src/api/generated/api-client.ts`) regenerates automatically on the next
`npm run build` / `npm start` in `frontend/`. No manual frontend edits are needed by this
plan — confirmed in `spec.r1.md`'s Out of Scope section that no frontend call site
currently exists for this endpoint.
