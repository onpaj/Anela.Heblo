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

