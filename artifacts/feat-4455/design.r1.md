# Design: Decouple GiftPackageManufactureService from IManufactureClient

## Component Design
- `IGiftPackageBomSource` (Logistics/Contracts): single read operation returning the BOM parts of a gift package set product.
- `LogisticsGiftPackagePart` (Logistics/Contracts/Models): plain class DTO.
- `ManufactureGiftPackageBomAdapter` (Manufacture/Infrastructure, internal sealed): ctor `(IManufactureClient client)`; `GetSetPartsAsync` awaits `client.GetSetPartsAsync(code, ct)` and projects each `ProductPart` to `LogisticsGiftPackagePart` (`ProductCode`, `ProductName`, `Amount`), returning a new `List`.
- `ManufactureModule`: `services.AddScoped<IGiftPackageBomSource, ManufactureGiftPackageBomAdapter>();` with a "provider-owned DI" comment.
- `GiftPackageManufactureService`: field/ctor param `IGiftPackageBomSource _bomSource` replaces `_manufactureClient` (same ctor position); the loop over parts uses `LogisticsGiftPackagePart`.

## Data Schemas
```csharp
public class LogisticsGiftPackagePart
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Amount { get; set; }
}
```
No database, API or event payload changes.
