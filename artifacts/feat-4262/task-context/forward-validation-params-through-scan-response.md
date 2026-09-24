### task: forward-validation-params-through-scan-response

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs:117-118`
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`

Depends on: `map-validation-exception-to-error-code`.

- [ ] **Step 1: Write the failing test**

Append to `ScanPackingOrderHandlerTests.cs`, directly after the existing `Handle_WhenShipmentCreationServiceFails_ReturnsMappedErrorCode` theory:

```csharp
[Fact]
public async Task Handle_WhenShipmentCreationServiceFailsWithParams_ForwardsParamsUnchanged()
{
    var order = EligibleOrder(("P001", 1, 400));

    _orderClient
        .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
        .ReturnsAsync(order);

    _shipmentClient
        .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);

    var validationParams = new Dictionary<string, string> { ["ShoptetMessage"] = "Invalid recipient of order, missing fields: city, zip." };
    _shipmentCreationService
        .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ShipmentCreationResult
        {
            IsSuccess = false,
            ErrorCode = ErrorCodes.ShipmentValidationFailed,
            Params = validationParams,
        });

    var response = await CreateHandler().Handle(
        new ScanPackingOrderRequest { OrderCode = "0001234" },
        CancellationToken.None);

    response.Success.Should().BeFalse();
    response.ErrorCode.Should().Be(ErrorCodes.ShipmentValidationFailed);
    response.Params.Should().BeEquivalentTo(validationParams);
}
```

Also add `ErrorCodes.ShipmentValidationFailed` to the existing `[InlineData(...)]` list on `Handle_WhenShipmentCreationServiceFails_ReturnsMappedErrorCode` (it currently covers `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`, `PackingUserNotEligible` — the new code belongs in that same "whatever error code comes back is surfaced unchanged" contract):

```csharp
    [Theory]
    [InlineData(ErrorCodes.ShipmentCarrierNotResolved)]
    [InlineData(ErrorCodes.ShipmentCreationFailed)]
    [InlineData(ErrorCodes.PackingUserNotEligible)]
    [InlineData(ErrorCodes.ShipmentValidationFailed)]
    public async Task Handle_WhenShipmentCreationServiceFails_ReturnsMappedErrorCode(ErrorCodes errorCode)
```

- [ ] **Step 2: Run the tests to verify the new one fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"`
Expected: `Handle_WhenShipmentCreationServiceFailsWithParams_ForwardsParamsUnchanged` FAILS (`response.Params` is null today because `ScanPackingOrderHandler` calls `new ScanPackingOrderResponse(result.ErrorCode!.Value)`, which never passes `Params`). The extended `[InlineData]` case PASSES already (it only checks `ErrorCode`, which already flows through).

- [ ] **Step 3: Add an optional `Params` parameter to `ScanPackingOrderResponse`'s error constructor**

In `ScanPackingOrderResponse.cs`, replace:

```csharp
    public ScanPackingOrderResponse(ErrorCodes errorCode) : base(errorCode) { }
```

with:

```csharp
    public ScanPackingOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
```

(Optional parameter with a default — every existing call site that passes only an `ErrorCodes` value keeps compiling unchanged.)

- [ ] **Step 4: Forward `Params` in the handler**

In `ScanPackingOrderHandler.cs`, replace:

```csharp
        var result = await _shipmentCreationService.CreateAndPersistAsync(
            order, request.NumberOfPackages, request.PackingUserId, ct);
        if (!result.IsSuccess)
            return new ScanPackingOrderResponse(result.ErrorCode!.Value);
```

with:

```csharp
        var result = await _shipmentCreationService.CreateAndPersistAsync(
            order, request.NumberOfPackages, request.PackingUserId, ct);
        if (!result.IsSuccess)
            return new ScanPackingOrderResponse(result.ErrorCode!.Value, result.Params);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"`
Expected: PASS (both the new test and the extended theory, plus every other pre-existing test in this file).

- [ ] **Step 6: Run the broader Packaging test suite to catch any incidental regression**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Packaging"`
Expected: PASS (covers `ScanPackingOrderHandlerPackagePersistenceTests` and any `ResetOrderShipment*` tests too — `ResetOrderShipmentHandler`/`ResetOrderShipmentResponse` are intentionally left untouched by this plan, per the spec's Out of Scope, and must keep passing unmodified).

- [ ] **Step 7: Full backend build + format check**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes`
Expected: build succeeds; formatting clean (or run `dotnet format` without `--verify-no-changes` and re-check if it reports changes).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs
git commit -m "feat(packaging): forward ShipmentValidationFailed Params through ScanOrder response"
```

---

