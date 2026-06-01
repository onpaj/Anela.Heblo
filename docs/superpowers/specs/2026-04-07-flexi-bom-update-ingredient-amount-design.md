# FlexiBeeSDK: BoM Update Ingredient Amount

## Context

`FlexiManufactureClient.UpdateBoMIngredientAmountAsync` in Heblo constructs the FlexiBee HTTP request manually — raw `HttpClient`, Basic Auth credentials, hand-rolled JSON body — bypassing the `Rem.FlexiBeeSDK.Client` package entirely. This is faulty: authentication handling, URL construction, and envelope formatting are duplicated outside the SDK and will drift.

The fix is to add `UpdateIngredientAmountAsync` to `IBoMClient` / `BoMClient` in the FlexiBeeSDK package, then replace the manual code in Heblo with a single SDK call.

## Scope

Two repositories are affected:

- **`FlexiBeeSDK`** — adds the new method (SDK change, package version bump)
- **`Anela.Heblo`** — replaces manual HTTP code with the SDK call, updates NuGet reference

## Design

### 1. New SDK method signature

```csharp
// IBoMClient
Task UpdateIngredientAmountAsync(
    string productCode,
    string ingredientCode,
    double newAmount,
    CancellationToken cancellationToken = default);
```

### 2. New request model (`Rem.FlexiBeeSDK.Model`)

A small model class in `src/Rem.FlexiBeeSDK.Model/Products/` to carry the PUT payload:

```csharp
public class UpdateBoMIngredientAmountRequest
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("mnozstvi")]
    public double Amount { get; set; }
}
```

Follows the same pattern as `RecalculatePriceRequest`.

### 3. `BoMClient` implementation

```csharp
public async Task UpdateIngredientAmountAsync(
    string productCode,
    string ingredientCode,
    double newAmount,
    CancellationToken cancellationToken = default)
{
    var bom = await GetAsync(productCode, cancellationToken);

    var ingredient = bom.FirstOrDefault(item =>
        item.Level != 1 &&
        item.IngredientCode.RemoveCodePrefix() == ingredientCode);

    if (ingredient == null)
        throw new InvalidOperationException(
            $"Ingredient '{ingredientCode}' not found in BoM for product '{productCode}'");

    var document = new Dictionary<string, object>
    {
        {
            ResourceIdentifier,
            new UpdateBoMIngredientAmountRequest { Id = ingredient.Id, Amount = newAmount }
        }
    };

    await PutAsync(document, cancellationToken: cancellationToken);
}
```

Uses the existing `PutAsync<TRequest>` from `ResourceClient` — same pattern as `RecalculatePurchasePrice`. No raw `HttpClient`, no manual auth.

### 4. Heblo change

`FlexiManufactureClient.UpdateBoMIngredientAmountAsync` is replaced entirely:

```csharp
public async Task UpdateBoMIngredientAmountAsync(
    string productCode,
    string ingredientCode,
    double newAmount,
    CancellationToken cancellationToken = default)
{
    await _bomClient.UpdateIngredientAmountAsync(productCode, ingredientCode, newAmount, cancellationToken);
}
```

The `IHttpClientFactory` injection is removed from `FlexiManufactureClient` if no other method uses it.

NuGet reference updated to the new package version.

## Error Handling

- Ingredient not found → `InvalidOperationException` thrown by the SDK (same behaviour as current Heblo code, no regression).
- Failed HTTP response → `InvalidOperationException` via existing `PutAsync` / `ResourceClient` infrastructure (same pattern as rest of SDK).
- `ManufactureOrderApplicationService` catches exceptions from `UpdateBoMIngredientAmountAsync` as warnings — this behaviour is unchanged.

## Testing

### FlexiBeeSDK (`BoMClient` unit tests)
- Happy path: `GetAsync` returns a BoM containing the ingredient → PUT is called with correct `id` and `mnozstvi`.
- Ingredient not found: `GetAsync` returns BoM without the ingredient → `InvalidOperationException`.

### Heblo
- Existing `ManufactureOrderApplicationService` tests mock `IManufactureClient.UpdateBoMIngredientAmountAsync` — no changes required.
- No new `FlexiManufactureClient` unit test needed; the method is a one-liner delegation.

## Package Version

Bump `Rem.FlexiBeeSDK.Client` patch version (e.g. `0.1.122` → `0.1.123`) and update the reference in `Anela.Heblo.Adapters.Flexi.csproj`.
