# Design: GetExpeditionListsByDateResponse.Fail() factory method

## Component Design

**`GetExpeditionListsByDateResponse`** (`Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`)
Response DTO, inherits `BaseResponse` (`Success`, `ErrorCode`, `Params`). Responsibility today: hold the list of matched expedition-list items (`Items`) on success. This change extends its responsibility to also own the shape of its one known failure case (invalid date format), via a new static factory method:

- `public static GetExpeditionListsByDateResponse InvalidDate()` — returns a response instance populated for the "date failed to parse as `yyyy-MM-dd`" case. Takes no parameters; the failure shape is fixed and self-contained (field name and expected format are constants of this one failure condition, not caller-supplied data).

**`GetExpeditionListsByDateHandler`** (`Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`)
MediatR handler, responsibility narrows to: (1) decide *whether* the date is valid, delegating to `GetExpeditionListsByDateResponse.InvalidDate()` for the failure branch instead of constructing the failure response itself, and (2) on the success path, list and filter blobs and map them to `ExpeditionListItemDto`s (unchanged). No other responsibility shifts.

No new components. No change to `GetExpeditionListsByDateRequest`, `IExpeditionListArchiveBlobStore`, `ExpeditionListItemDto`, or any controller/endpoint that invokes this handler through MediatR.

## Data Schemas

No schema change. `GetExpeditionListsByDateResponse`'s serialized shape (as returned over HTTP / represented in the generated OpenAPI/TypeScript client) is identical before and after this change:

```json
{
  "success": false,
  "errorCode": "InvalidFormat",
  "params": { "Field": "Date", "ExpectedFormat": "yyyy-MM-dd" },
  "items": []
}
```

This is exactly the payload produced today by the inline construction in the handler, and exactly what `GetExpeditionListsByDateResponse.InvalidDate()` produces after the change — the factory method is not part of the serialized contract (methods aren't serialized), only a C#-side construction helper. No API client regeneration is triggered by this change.
