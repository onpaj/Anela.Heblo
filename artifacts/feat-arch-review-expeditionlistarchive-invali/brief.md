## Module
ExpeditionListArchive

## Finding
`GetExpeditionListsByDateHandler` silently returns a successful empty response when the date string fails format validation:

```csharp
// GetExpeditionListsByDateHandler.cs, lines 21-24
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return new GetExpeditionListsByDateResponse { Items = new List<ExpeditionListItemDto>() };
}
```

`Success` defaults to `true` (inherited from `BaseResponse`), so the controller returns `200 OK` with `{ items: [] }`. This is indistinguishable from "valid date, no files uploaded that day."

The other two handlers in the same module that also validate input handle the bad-input case consistently:

- `DownloadExpeditionListHandler.cs:21` — returns `DownloadExpeditionListResponse.Fail("Invalid blob path.")`
- `ReprintExpeditionListHandler.cs:23` — returns `ReprintExpeditionListResponse.Fail("Invalid blob path.")`

## Why it matters
The caller (controller + frontend) cannot distinguish a malformed date from a legitimate empty day. The inconsistency within the same module is also a readability trap: a developer familiar with the sibling handlers will expect `Fail()` to be called here too.

## Suggested fix
Add a `Fail` factory (matching the sibling pattern) and call it on invalid input:

```csharp
// GetExpeditionListsByDateResponse.cs
public static GetExpeditionListsByDateResponse Fail(string message) =>
    new() { Success = false, ErrorMessage = message };

// GetExpeditionListsByDateHandler.cs
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return GetExpeditionListsByDateResponse.Fail("Invalid date format. Expected yyyy-MM-dd.");
}
```

The controller already propagates the response as-is, so the HTTP layer can check `response.Success` and return `BadRequest` if needed (mirroring how `Download` and `Reprint` behave).

---
_Filed by daily arch-review routine on 2026-06-04._