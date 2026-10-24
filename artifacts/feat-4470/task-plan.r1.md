# Task Plan: Type CreatePurchaseOrderRequest date fields as DateTime

Build and test commands: BE `dotnet build` + `dotnet format` + `dotnet test --filter "FullyQualifiedName~Purchase"`; FE `npm run build` + `npm run lint` + relevant jest tests. Do not alter `UpdatePurchaseOrderRequest` or the `PurchaseOrder` entity.

### task: backend-request-validator
Change the request DTO and validator to typed dates (FR-1, FR-3).
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderRequest.cs`: `OrderDate` -> `DateTime` (drop `[Required]`), `ExpectedDeliveryDate` -> `DateTime?`. Stays a class.
- `CreatePurchaseOrderRequestValidator.cs`: OrderDate `NotEqual(default(DateTime))` "Order date is required"; `Must(d => d.Date <= DateTime.UtcNow.Date.AddDays(30))` "Order date cannot be more than 30 days in the future"; ExpectedDeliveryDate when `HasValue` must be `>= OrderDate.Date`, "Expected delivery date must be on or after the order date". Delete `BeAValidDate`, `NotBeTooFarInFuture`, `BeAfterOrEqualToOrderDate`.
- Update `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderRequestValidatorTests.cs` to typed inputs; drop "must be a valid date" cases; add a default-OrderDate case.
- Verify: validator tests pass. (Handler will not compile until the next task; build the Application project only, or complete both before building the full solution.)

### task: backend-handler
Use typed dates in the handler (FR-2). Depends on backend-request-validator.
- `CreatePurchaseOrderHandler.cs` (~lines 54-55): replace the parse calls with `DateTime.SpecifyKind(request.OrderDate.Date, DateTimeKind.Utc)` and the nullable equivalent for `ExpectedDeliveryDate`. Remove the `Common.Extensions` using if unused.
- `Common/Extensions/DateExtensions.cs`: grep `ToUtcDateTime`/`ToUtcDateTimeOrNull`; remove the string overloads only if now unused, keep the `DateOnly` overload.
- Update `CreatePurchaseOrderHandlerTests.cs` and `Controllers/PurchaseOrdersControllerTests.cs` to supply `DateTime` values; add a test that a Local-kind input with a time component persists as date-only, Kind Utc.
- Grep all `CreatePurchaseOrderRequest` usages (incl. MCP tools, `PurchaseModule.cs`) and fix compile errors.
- Verify: `dotnet build`, `dotnet format`, Purchase tests pass.

### task: frontend-client-helper
Regenerate the client and fix the create mapping (FR-4). Depends on backend-handler.
- Build the backend/regenerate the OpenAPI TS client per `docs/development/api-client-generation.md`; commit only the expected diff in `frontend/src/api/generated/api-client.ts` (`orderDate: Date`, `expectedDeliveryDate?: Date`).
- `frontend/src/components/purchase-orders/form/PurchaseOrderHelpers.tsx`: build `Date` from the form `YYYY-MM-DD` as UTC midnight (`new Date(value + "T00:00:00Z")`); fix any call site in `frontend/src/api/hooks/usePurchaseOrders.ts` if types break.
- Add a FE unit test confirming the serialized calendar date equals the input date.
- Verify: `npm run build`, `npm run lint`, related jest tests pass.

### task: process-docs-check
Run `python3 scripts/process-docs/check.py index`; if a `docs/processes/` doc owns Purchase order creation, bump `verified_at` (or note the validation change) in the same PR. Verify the check passes.
