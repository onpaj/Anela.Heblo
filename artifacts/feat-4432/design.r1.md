# Design: DataQualityMappingProfile tests

## Component Design
Single xUnit test class `DataQualityMappingProfileTests` (namespace `Anela.Heblo.Tests.Features.DataQuality`) with a private `Map(InvoiceMismatchType)` helper that builds an `InvoiceDqtResult` via `Create(Guid.NewGuid(), "INV-1", type, null, null, null)` and maps it with a real AutoMapper `IMapper`.

Tests:
- `[Theory]` single flag -> `[name]` and `MismatchType` int (1,2,4,8,16,32 literals).
- `[Fact]` compound (MissingInFlexi | TotalWithVatDiffers) -> exactly `["MissingInFlexi","TotalWithVatDiffers"]`.
- `[Fact]` all flags -> six names in ascending order, none "None".
- `[Fact]` None -> empty list, `MismatchType == 0`.
- `[Fact]` DqtRun enum-to-string mapping (optional).

## Data Schemas
No schema changes. Output DTO: `InvoiceDqtResultDto { int MismatchType; List<string> MismatchFlags }`.
