### task: map-validation-exception-to-error-code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs:80-89`
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs`

Depends on: `add-validation-exception-and-error-code`.

**Decision, fixed here (do not deviate):** the `Params` key carrying the Shoptet message is `"ShoptetMessage"`. This exact string must also be used verbatim in the `surface-validation-message-in-frontend` task — it is the one cross-stack "schema" contract called out in the design doc.

- [ ] **Step 1: Write the failing test**

Append to `ShipmentCreationServiceTests.cs`, directly after the existing `CreateAndPersistAsync_CreateShipmentThrows_ReturnsShipmentCreationFailed` test:

```csharp
[Fact]
public async Task CreateAndPersistAsync_CreateShipmentThrowsValidationException_ReturnsShipmentValidationFailedWithMessage()
{
    var order = EligibleOrder(("P001", 1, 500));
    _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
        .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
    _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new ShoptetShipmentValidationException(
            "0001234", "shipment-validation-failed", "Invalid recipient of order, missing fields: city, zip.", "data.orderCode"));

    var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

    result.IsSuccess.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.ShipmentValidationFailed);
    result.Params.Should().NotBeNull();
    result.Params!["ShoptetMessage"].Should().Be("Invalid recipient of order, missing fields: city, zip.");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShipmentCreationServiceTests"`
Expected: FAIL to compile (`ShipmentCreationResult.Params` doesn't exist yet) — this is expected at this point in TDD; proceed to Step 3.

- [ ] **Step 3: Add `Params` to `ShipmentCreationResult`**

```csharp
// backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs
public class ShipmentCreationResult
{
    public bool IsSuccess { get; init; }

    /// <summary>Set when IsSuccess == false.</summary>
    public ErrorCodes? ErrorCode { get; init; }

    /// <summary>
    /// Set when IsSuccess == false and the failure carries additional detail for the caller
    /// (currently only ShipmentValidationFailed, carrying the Shoptet message under "ShoptetMessage").
    /// Null for every other failure — existing callers that don't read it are unaffected.
    /// </summary>
    public Dictionary<string, string>? Params { get; init; }

    public Guid ShipmentGuid { get; init; }

    public string CarrierCode { get; init; } = null!;

    public string? CarrierName { get; init; }

    /// <summary>
    /// Exactly `numberOfPackages` entries: filtered to this shipment's GUID, padded with
    /// null-fields entries where Shoptet hasn't generated a label yet.
    /// </summary>
    public IReadOnlyList<ShipmentLabel> Labels { get; init; } = [];
}
```

- [ ] **Step 4: Add the catch block in `ShipmentCreationService.CreateAndPersistAsync`**

Replace:

```csharp
        CreatedShipment createdShipment;
        try
        {
            createdShipment = await _shipmentClient.CreateShipmentAsync(command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", order.Code);
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCreationFailed };
        }
```

with:

```csharp
        CreatedShipment createdShipment;
        try
        {
            createdShipment = await _shipmentClient.CreateShipmentAsync(command, ct);
        }
        catch (ShoptetShipmentValidationException vex)
        {
            // Permanent, non-retryable: Shoptet rejected the recipient/shipment data outright.
            // Logged at Warning (not Error) — this is an expected, actionable data-quality
            // condition once this mapping exists, not an infrastructure anomaly. Still visible
            // in Application Insights (NFR-2).
            _logger.LogWarning(vex,
                "Shoptet rejected shipment for order {OrderCode}: {ShoptetMessage}", order.Code, vex.Message);
            return new ShipmentCreationResult
            {
                IsSuccess = false,
                ErrorCode = ErrorCodes.ShipmentValidationFailed,
                Params = new Dictionary<string, string> { ["ShoptetMessage"] = vex.Message },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", order.Code);
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCreationFailed };
        }
```

(`ShoptetShipmentValidationException` is in `Anela.Heblo.Application.Features.ShipmentLabels`, which `ShipmentCreationService.cs` already imports at the top of the file — line 2 — so no new `using` is needed.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShipmentCreationServiceTests"`
Expected: PASS (new test, plus every pre-existing test in this file, including `CreateAndPersistAsync_CreateShipmentThrows_ReturnsShipmentCreationFailed` — confirms the existing catch-all path is untouched).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs
git commit -m "feat(packaging): map ShoptetShipmentValidationException to ShipmentValidationFailed with message"
```

---

