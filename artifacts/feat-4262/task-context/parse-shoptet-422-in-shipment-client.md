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

