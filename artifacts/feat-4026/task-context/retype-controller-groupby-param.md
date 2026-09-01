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
