### task: backend-handler
Use typed dates in the handler (FR-2). Depends on backend-request-validator.
- `CreatePurchaseOrderHandler.cs` (~lines 54-55): replace the parse calls with `DateTime.SpecifyKind(request.OrderDate.Date, DateTimeKind.Utc)` and the nullable equivalent for `ExpectedDeliveryDate`. Remove the `Common.Extensions` using if unused.
- `Common/Extensions/DateExtensions.cs`: grep `ToUtcDateTime`/`ToUtcDateTimeOrNull`; remove the string overloads only if now unused, keep the `DateOnly` overload.
- Update `CreatePurchaseOrderHandlerTests.cs` and `Controllers/PurchaseOrdersControllerTests.cs` to supply `DateTime` values; add a test that a Local-kind input with a time component persists as date-only, Kind Utc.
- Grep all `CreatePurchaseOrderRequest` usages (incl. MCP tools, `PurchaseModule.cs`) and fix compile errors.
- Verify: `dotnet build`, `dotnet format`, Purchase tests pass.

