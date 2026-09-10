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
