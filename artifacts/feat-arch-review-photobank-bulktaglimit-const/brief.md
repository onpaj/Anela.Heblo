## Module
Photobank

## Finding
The constant `private const int BulkTagLimit = 5_000;` is defined independently in two separate handler classes:

- `BulkAddPhotoTagHandler.cs`, line 15
- `BulkAddPhotoTagByIdsHandler.cs`, line 15

Both use it to enforce the same business rule: no more than 5 000 photos per bulk-tag operation. The values are identical and the error response code (`ErrorCodes.BulkTagLimitExceeded`) is also shared.

## Why it matters
If the limit is ever changed, it must be updated in two places. Given that both handlers reference `ErrorCodes.BulkTagLimitExceeded` and the same error response shape (`Params["Count"]`, `Params["Limit"]`), the duplication is real — a developer raising the limit from 5 000 to 10 000 would need to know both files exist. This violates DRY and is an easy miss.

## Suggested fix
Move the constant to `PhotobankConstants.cs` (create the file if it doesn't exist yet — the filesystem convention for this is `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs`) and reference it from both handlers:

```csharp
// PhotobankConstants.cs
public static class PhotobankConstants
{
    public const int BulkTagLimit = 5_000;
}
```

Then in both handlers:
```csharp
if (total > PhotobankConstants.BulkTagLimit)
```

---
_Filed by daily arch-review routine on 2026-05-27._