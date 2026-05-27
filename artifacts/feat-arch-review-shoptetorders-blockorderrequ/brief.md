## Module
ShoptetOrders

## Finding
`BlockOrderRequest` is defined at the bottom of the API controller file rather than in the Application module's contracts:

```
backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs — lines 56–60
```

```csharp
public class BlockOrderRequest
{
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
```

The API project owns and defines this DTO, which violates the project rule: _"API project never defines or owns DTOs – it only uses them."_ The correct home is `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/` (alongside `BlockOrderProcessingRequest.cs`).

## Why it matters
The rule exists so that the API project stays a thin host layer with no contract ownership. Placing a DTO here means the Application layer cannot reference it (it would create a reverse dependency), so the controller ends up mapping `BlockOrderRequest.Note → BlockOrderProcessingRequest.Note` with a manual property copy instead of a proper contract. Any future consumer of the Application layer (tests, another endpoint) cannot reuse the type without touching the API project.

## Suggested fix
Move `BlockOrderRequest` (with its `[JsonPropertyName]` attribute) to:

```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs
```

Update the `using` in the controller. No logic changes required.

---
_Filed by daily arch-review routine on 2026-05-23._