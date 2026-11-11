### task: backend-available-transitions
Files:
- Modify `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrder/GetManufactureOrderResponse.cs`
- Modify `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrder/GetManufactureOrderHandler.cs`
- Modify `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOrderHandlerTests.cs`

Steps:
1. Add failing tests in `GetManufactureOrderHandlerTests`: a `[Theory]` over each `ManufactureOrderState` asserting `AvailableTransitions` equals the expected legal set (Draft: Planned,Cancelled; Planned: Draft,SemiProductManufactured,Completed,Cancelled; SemiProductManufactured: Planned,Completed,Cancelled; Completed: SemiProductManufactured,Planned,Cancelled; Cancelled: empty). Build the order with `InitializeState`; mock the mapper as existing tests do. Add a test that a not-found response has an empty list.
2. Add `public List<ManufactureOrderState> AvailableTransitions { get; set; } = new();` to `GetManufactureOrderResponse` (add `using Anela.Heblo.Domain.Features.Manufacture;`).
3. In the handler set `AvailableTransitions = Enum.GetValues<ManufactureOrderState>().Where(s => s != order.State && order.CanTransitionTo(s)).OrderBy(s => (int)s).ToList()`.
4. Verify: `dotnet test backend --filter GetManufactureOrderHandlerTests`, `dotnet build`, `dotnet format`.

