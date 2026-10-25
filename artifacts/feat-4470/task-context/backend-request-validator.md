### task: backend-request-validator
Change the request DTO and validator to typed dates (FR-1, FR-3).
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderRequest.cs`: `OrderDate` -> `DateTime` (drop `[Required]`), `ExpectedDeliveryDate` -> `DateTime?`. Stays a class.
- `CreatePurchaseOrderRequestValidator.cs`: OrderDate `NotEqual(default(DateTime))` "Order date is required"; `Must(d => d.Date <= DateTime.UtcNow.Date.AddDays(30))` "Order date cannot be more than 30 days in the future"; ExpectedDeliveryDate when `HasValue` must be `>= OrderDate.Date`, "Expected delivery date must be on or after the order date". Delete `BeAValidDate`, `NotBeTooFarInFuture`, `BeAfterOrEqualToOrderDate`.
- Update `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderRequestValidatorTests.cs` to typed inputs; drop "must be a valid date" cases; add a default-OrderDate case.
- Verify: validator tests pass. (Handler will not compile until the next task; build the Application project only, or complete both before building the full solution.)

