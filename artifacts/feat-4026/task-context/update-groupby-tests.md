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
