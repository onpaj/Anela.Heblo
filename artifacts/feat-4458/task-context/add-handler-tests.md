### task: add-handler-tests
**Files:** `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs`

- [ ] Add test `Handle_ProductMultiPhase_DoesNotMutateRequestAndForwardsSemiproductCode`: catalog returns `ProductKind.Product` snapshot; `_manufactureClientMock.GetManufactureTemplateAsync` returns MultiPhase template with a SemiProduct ingredient `SEMI001`; capture request passed to service via `Callback`/`It.Is`; assert original request `ProductCode`=="PROD001" and `ManufactureType` null, and forwarded `ProductCode`=="SEMI001", `ManufactureType`==MultiPhase.
- [ ] Add test for Product + SinglePhase: forwarded ProductCode unchanged, ManufactureType SinglePhase, original request's ManufactureType still null.
- [ ] Add test for MultiPhase without SemiProduct ingredient: forwarded ProductCode is original.
- [ ] Add a reflection-based test asserting that every public property except `ProductCode` and `ManufactureType` is equal between original and forwarded request (set non-default values on all fields first).
- [ ] Verify existing tests still pass: `cd backend && dotnet test test/Anela.Heblo.Tests --filter CalculateBatchPlanHandlerTests`.

