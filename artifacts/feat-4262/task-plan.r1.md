# Surface Shoptet shipment-validation-failed as an actionable error on Packaging/ScanOrder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `POST /api/packaging/orders/{orderCode}/scan` return a distinct, actionable 422 (not a generic 503) when Shoptet rejects shipment creation with `shipment-validation-failed`, telling the operator what's wrong instead of "try again."

**Architecture:** `ShoptetShipmentClient.CreateShipmentAsync` parses the non-2xx response body and throws a new `ShoptetShipmentValidationException` (living in `Anela.Heblo.Application.Features.ShipmentLabels`, not the adapter project — see arch-review Decision 1/Prerequisites) only when it can positively match Shoptet's `errorCode: "shipment-validation-failed"`; every other failure keeps throwing the existing generic `HttpRequestException`. `ShipmentCreationService.CreateAndPersistAsync` catches the new exception type ahead of its existing catch-all and returns a new `ErrorCodes.ShipmentValidationFailed` (422) with the Shoptet message in `Params`, which `ScanPackingOrderHandler`/`ScanPackingOrderResponse`/`BaseApiController` already know how to carry through to the wire unchanged once `Params` is plumbed one hop further. The frontend adds one new curated message that reads that `Params` value.

**Tech Stack:** .NET 8 / MediatR / xUnit + FluentAssertions + Moq (backend), React / TanStack Query / Jest + Testing Library (frontend), NSwag (generated API client).

---

### task: add-validation-exception-and-error-code

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShoptetShipmentValidationException.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:388` (immediately after `ShipmentOrderWeightUnavailable = 2909,`)

This task adds the two purely-additive pieces (a new exception type, a new enum value) with no behavior wired up yet, so it's safe to commit standalone.

- [ ] **Step 1: Create the exception type**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShoptetShipmentValidationException.cs
namespace Anela.Heblo.Application.Features.ShipmentLabels;

/// <summary>
/// Thrown by IShipmentClient.CreateShipmentAsync implementations when the carrier API rejects
/// shipment creation with a permanent, non-retryable validation error (e.g. Shoptet's
/// "shipment-validation-failed" — recipient address missing required fields). Distinct from a
/// generic HttpRequestException, which still represents a transient/unclassified failure that
/// may succeed on retry. See docs/integrations/shoptet-api.md for the known Shoptet causes.
/// </summary>
public class ShoptetShipmentValidationException : Exception
{
    public string OrderCode { get; }

    /// <summary>The carrier's own error code, e.g. "shipment-validation-failed".</summary>
    public string ShoptetErrorCode { get; }

    /// <summary>The carrier's own "instance" field, e.g. "data.orderCode" — nullable because not every carrier error includes one.</summary>
    public string? Instance { get; }

    public ShoptetShipmentValidationException(string orderCode, string shoptetErrorCode, string message, string? instance)
        : base(message)
    {
        OrderCode = orderCode;
        ShoptetErrorCode = shoptetErrorCode;
        Instance = instance;
    }
}
```

- [ ] **Step 2: Add the new error code**

Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` and change:

```csharp
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentOrderWeightUnavailable = 2909,

    // Packaging module errors (30XX)
```

to:

```csharp
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentOrderWeightUnavailable = 2909,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentValidationFailed = 2910,

    // Packaging module errors (30XX)
```

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build`
Expected: build succeeds (no callers reference either new symbol yet, so nothing else changes).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShoptetShipmentValidationException.cs backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(packaging): add ShoptetShipmentValidationException and ShipmentValidationFailed error code"
```

---

### task: parse-shoptet-422-in-shipment-client

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs:172-195` (`CreateShipmentAsync`)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs`

Depends on: `add-validation-exception-and-error-code`.

- [ ] **Step 1: Write the failing tests**

Append to `ShoptetShipmentClientTests.cs` (reuses this file's existing `BuildClient`/`Json` helpers):

```csharp
[Fact]
public async Task CreateShipmentAsync_ShipmentValidationFailed_ThrowsShoptetShipmentValidationException()
{
    // Arrange — the exact body from the 2026-09-21 incident (order 126020133)
    var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
    {
        Content = new StringContent(
            """{"data":null,"errors":[{"errorCode":"shipment-validation-failed","message":"Invalid recipient of order, missing fields: city, zip.","instance":"data.orderCode"}]}""",
            Encoding.UTF8, "application/json"),
    });
    var command = new CreateShipmentCommand
    {
        OrderCode = "126020133",
        CarrierCode = "1",
        PackageCount = 1,
        Package = new ShipmentPackage { WidthCm = 30, HeightCm = 20, DepthCm = 15, WeightGrams = 500 },
    };

    // Act
    var act = () => client.CreateShipmentAsync(command);

    // Assert
    var ex = await act.Should().ThrowAsync<ShoptetShipmentValidationException>();
    ex.Which.OrderCode.Should().Be("126020133");
    ex.Which.ShoptetErrorCode.Should().Be("shipment-validation-failed");
    ex.Which.Message.Should().Be("Invalid recipient of order, missing fields: city, zip.");
    ex.Which.Instance.Should().Be("data.orderCode");
}

[Fact]
public async Task CreateShipmentAsync_OtherValidationErrorCode_ThrowsGenericHttpRequestException()
{
    // Arrange — a 422 with a *different* errorCode must NOT be treated as the permanent case
    var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
    {
        Content = new StringContent(
            """{"data":null,"errors":[{"errorCode":"invalid-request-data","message":"boom","instance":"integration-call"}]}""",
            Encoding.UTF8, "application/json"),
    });
    var command = new CreateShipmentCommand
    {
        OrderCode = "0001234",
        CarrierCode = "1",
        PackageCount = 1,
        Package = new ShipmentPackage { WidthCm = 30, HeightCm = 20, DepthCm = 15, WeightGrams = 500 },
    };

    // Act
    var act = () => client.CreateShipmentAsync(command);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>();
}

[Fact]
public async Task CreateShipmentAsync_NonJsonErrorBody_FallsBackToGenericHttpRequestException()
{
    // Arrange — an unparseable body (e.g. an upstream proxy's HTML error page) must not crash
    var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
    {
        Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html"),
    });
    var command = new CreateShipmentCommand
    {
        OrderCode = "0001234",
        CarrierCode = "1",
        PackageCount = 1,
        Package = new ShipmentPackage { WidthCm = 30, HeightCm = 20, DepthCm = 15, WeightGrams = 500 },
    };

    // Act
    var act = () => client.CreateShipmentAsync(command);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>();
}
```

Add `using Anela.Heblo.Application.Features.ShipmentLabels;` to the test file's usings if not already present (it is — `ShipmentLabel` is already imported from there).

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetShipmentClientTests"`
Expected: the two new pass-through cases (`OtherValidationErrorCode`, `NonJsonErrorBody`) already PASS today (current code already throws `HttpRequestException` for every non-2xx case) — only `ShipmentValidationFailed_ThrowsShoptetShipmentValidationException` FAILS, because `CreateShipmentAsync` currently always throws `HttpRequestException`, never `ShoptetShipmentValidationException`.

- [ ] **Step 3: Implement the parsing**

In `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`, replace:

```csharp
        var response = await _http.PostAsJsonAsync("/api/shipments", envelope, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"POST /api/shipments for order {command.OrderCode} returned {(int)response.StatusCode}: {body}");
        }
```

with:

```csharp
        var response = await _http.PostAsJsonAsync("/api/shipments", envelope, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            var validationError = TryParseValidationError(body);
            if (validationError is not null)
            {
                throw new ShoptetShipmentValidationException(
                    command.OrderCode,
                    validationError.ErrorCode!,
                    validationError.Message ?? body,
                    validationError.Instance);
            }

            throw new HttpRequestException(
                $"POST /api/shipments for order {command.OrderCode} returned {(int)response.StatusCode}: {body}");
        }
```

Add this private helper to the class (near `FetchShipmentsAsync`, which does the equivalent parsing on the success path):

```csharp
    /// <summary>
    /// Best-effort parse of a non-2xx /api/shipments response body looking specifically for
    /// Shoptet's "shipment-validation-failed" error code. Returns null for any body that isn't
    /// valid JSON in the expected envelope shape, or that doesn't contain that specific error —
    /// every other case keeps falling back to the generic HttpRequestException path.
    /// </summary>
    private static ShoptetErrorDto? TryParseValidationError(string body)
    {
        ShoptetCreateShipmentResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ShoptetCreateShipmentResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return parsed?.Errors?.FirstOrDefault(e => e.ErrorCode == "shipment-validation-failed");
    }
```

Add `using Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;` if not already present at the top of the file — it already is (line 3), since `ShoptetCreateShipmentRequestEnvelope` etc. come from there; `ShoptetCreateShipmentResponse` and `ShoptetErrorDto` are in the same namespace/file group, so no new `using` is needed. `Anela.Heblo.Application.Features.ShipmentLabels` is also already imported (line 4), so `ShoptetShipmentValidationException` resolves with no new `using` either.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetShipmentClientTests"`
Expected: PASS (all three new tests, plus every pre-existing test in this file — confirms NFR-1: no regression to `FetchShipmentsAsync`/`GetShippingOptionsAsync`/`CancelShipmentAsync`, which this task does not touch).

- [ ] **Step 5: Run the full backend test suite once for this project to catch any incidental regression**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetApi"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs
git commit -m "feat(shoptet): distinguish shipment-validation-failed from generic shipment creation errors"
```

---

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

### task: regenerate-frontend-api-client

**Files:**
- Modify (generated, do not hand-edit): `frontend/src/api/generated/api-client.ts`

Depends on: `add-validation-exception-and-error-code` (needs `ErrorCodes.ShipmentValidationFailed` to exist in the OpenAPI spec the backend serves).

Per `docs/development/api-client-generation.md`, the TypeScript client is generated from the backend's OpenAPI spec via NSwag — it must be regenerated, not hand-edited, so the `ErrorCodes` union type in `api-client.ts` includes `"ShipmentValidationFailed"` and the `surface-validation-message-in-frontend` task's TypeScript compiles against it.

- [ ] **Step 1: Regenerate the client**

Run (from repository root):
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

- [ ] **Step 2: Verify the new error code is present**

Run: `grep -n "ShipmentValidationFailed" frontend/src/api/generated/api-client.ts`
Expected: at least one match, inside the generated `ErrorCodes` enum/union.

- [ ] **Step 3: Diff-review the regenerated file**

Run: `git diff --stat frontend/src/api/generated/api-client.ts`
Expected: a small, additive diff limited to the new `ErrorCodes` member (and whatever else NSwag's determinism produces — if the diff is much larger than expected, stop and investigate before proceeding rather than committing an unreviewed regeneration).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(api-client): regenerate for ShipmentValidationFailed error code"
```

---

### task: surface-validation-message-in-frontend

**Files:**
- Modify: `frontend/src/api/hooks/useScanPackingOrder.ts:101-131`
- Test: `frontend/src/api/hooks/__tests__/useScanPackingOrder.test.ts`

Depends on: `regenerate-frontend-api-client`, `map-validation-exception-to-error-code` (for the fixed `"ShoptetMessage"` `Params` key name).

- [ ] **Step 1: Write the failing tests**

Append to `useScanPackingOrder.test.ts`, directly after the existing `'throws the curated Czech message for a known business error code'` test:

```typescript
it('throws an actionable message naming the missing address fields for ShipmentValidationFailed', async () => {
  mockPackaging_ScanOrder.mockResolvedValue({
    success: false,
    errorCode: 'ShipmentValidationFailed',
    params: { ShoptetMessage: 'Invalid recipient of order, missing fields: city, zip.' },
  });

  const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
  result.current.mutate({ orderCode: '126020133' });

  await waitFor(() => expect(result.current.isError).toBe(true));
  expect(result.current.error?.message).toBe(
    'Adresu příjemce nelze použít pro vytvoření zásilky — opravte v Shoptetu: Invalid recipient of order, missing fields: city, zip..',
  );
});

it('throws the base actionable message for ShipmentValidationFailed when no detail is present', async () => {
  mockPackaging_ScanOrder.mockResolvedValue({
    success: false,
    errorCode: 'ShipmentValidationFailed',
  });

  const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
  result.current.mutate({ orderCode: '126020133' });

  await waitFor(() => expect(result.current.isError).toBe(true));
  expect(result.current.error?.message).toBe(
    'Adresu příjemce nelze použít pro vytvoření zásilky (chybí povinné údaje) — opravte ji v Shoptetu.',
  );
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx react-scripts test src/api/hooks/__tests__/useScanPackingOrder.test.ts --watchAll=false`
Expected: FAIL — both new tests get the generic `'Chyba při skenování objednávky.'` message today, since `ShipmentValidationFailed` isn't in `SCAN_ERROR_MESSAGES` and `params` is discarded by the current `toMessage` callback.

- [ ] **Step 3: Implement the message mapping**

In `useScanPackingOrder.ts`, replace:

```typescript
const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
  PackingUserNotEligible: 'Vybraný balič není aktivní nebo nemá oprávnění balit. Vyberte baliče znovu.',
};

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';
```

with:

```typescript
const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
  PackingUserNotEligible: 'Vybraný balič není aktivní nebo nemá oprávnění balit. Vyberte baliče znovu.',
};

// Shoptet permanently rejected the recipient/shipment data (e.g. missing city/zip) — this is
// not a "try again" condition, so the wording explicitly says to fix the order in Shoptet.
// When the backend forwards the Shoptet message (Params["ShoptetMessage"] — see
// ShipmentCreationService.CreateAndPersistAsync), it's appended so the operator sees exactly
// which fields are missing instead of only the generic category.
const SHIPMENT_VALIDATION_FAILED_BASE =
  'Adresu příjemce nelze použít pro vytvoření zásilky (chybí povinné údaje) — opravte ji v Shoptetu.';

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';
```

The detailed and base messages are two independently-worded sentences (the detailed variant drops "ji" for grammatical fit with the appended clause), not one derived from the other — add this helper next to `SHIPMENT_VALIDATION_FAILED_BASE`:

```typescript
const shipmentValidationFailedDetailed = (detail: string) =>
  `Adresu příjemce nelze použít pro vytvoření zásilky — opravte v Shoptetu: ${detail}.`;
```

Then replace the `toMessage` callback passed to `callApi`:

```typescript
  const response = await callApi(
    () =>
      apiClient.packaging_ScanOrder(
        orderCode,
        numberOfPackages,
        new ScanOrderBody({ packingUserId: packingUserId ?? undefined }),
      ),
    ({ errorCode }) => (errorCode && SCAN_ERROR_MESSAGES[errorCode]) ?? GENERIC_SCAN_ERROR,
  );
```

with:

```typescript
  const response = await callApi(
    () =>
      apiClient.packaging_ScanOrder(
        orderCode,
        numberOfPackages,
        new ScanOrderBody({ packingUserId: packingUserId ?? undefined }),
      ),
    ({ errorCode, params }) => {
      if (errorCode === 'ShipmentValidationFailed') {
        const detail = params?.ShoptetMessage;
        return detail ? shipmentValidationFailedDetailed(detail) : SHIPMENT_VALIDATION_FAILED_BASE;
      }
      return (errorCode && SCAN_ERROR_MESSAGES[errorCode]) ?? GENERIC_SCAN_ERROR;
    },
  );
```

This wording matches Step 1's test expectations verbatim — treat the test assertions there as the source of truth for the exact literal strings.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx react-scripts test src/api/hooks/__tests__/useScanPackingOrder.test.ts --watchAll=false`
Expected: PASS (all tests in the file, new and pre-existing).

- [ ] **Step 5: Lint and typecheck**

Run: `cd frontend && npm run lint && npm run build`
Expected: no lint errors; build succeeds (the build step also re-runs `generate-client`, which should be a no-op diff after the previous task already regenerated it — if it isn't, investigate before committing).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useScanPackingOrder.ts frontend/src/api/hooks/__tests__/useScanPackingOrder.test.ts
git commit -m "feat(packaging): show actionable Shoptet validation message instead of generic retry text"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (distinguish permanent validation failure) → `parse-shoptet-422-in-shipment-client`.
- FR-2 (new non-retryable error code + message in Params) → `add-validation-exception-and-error-code` + `map-validation-exception-to-error-code`.
- FR-3 (actionable operator message) → `regenerate-frontend-api-client` + `surface-validation-message-in-frontend`.
- NFR-1 (no behavior change elsewhere) → every task's test-run steps explicitly re-run the full surrounding test file/module, not just the new test, and `forward-validation-params-through-scan-response` explicitly re-runs the Packaging module including `ResetOrderShipment*` tests, which are deliberately left unmodified.
- NFR-2 (observability parity) → `map-validation-exception-to-error-code` Step 4 keeps a `_logger` call on the new branch (Warning instead of Error, with rationale noted inline).
- Data Model / API design / Dependencies / Out of Scope sections of the spec describe no additional net-new work beyond what's covered above.

**2. Placeholder scan:** No TBD/TODO, no "add appropriate error handling" hand-waving — every step has literal code or an exact command. The one edit in `forward-validation-params-through-scan-response`'s Step 3 (optional parameter with `= null` default) is intentionally minimal-diff rather than a new overload, to avoid touching other `ScanPackingOrderResponse(ErrorCodes)` call sites unnecessarily.

**3. Type consistency:** `ShoptetShipmentValidationException(orderCode, shoptetErrorCode, message, instance)`'s constructor signature is used identically in `parse-shoptet-422-in-shipment-client` (throw site) and `map-validation-exception-to-error-code` (test construction site). `ShipmentCreationResult.Params` (`Dictionary<string, string>?`) is used identically in the service, the handler, and the handler's test. The `"ShoptetMessage"` `Params` key is used identically in the backend (`ShipmentCreationService`), its test, and the frontend (`useScanPackingOrder.ts`) and its test — this is the one value that had to be kept in lockstep across tasks/files per the design doc, and it is spelled identically (`"ShoptetMessage"`) in all four places above.
