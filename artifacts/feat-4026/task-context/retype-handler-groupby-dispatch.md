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
