## Module
ExpeditionList

## Finding
`RunExpeditionListPrintFixResponse` declares an `ErrorMessage` string field that is never assigned anywhere:

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixResponse.cs:6
public class RunExpeditionListPrintFixResponse : BaseResponse
{
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }  // never set — always null in responses
}
```

The only handler that returns this type (`RunExpeditionListPrintFixHandler.cs:37`) always constructs a success response and never assigns `ErrorMessage`:

```csharp
return new RunExpeditionListPrintFixResponse
{
    Success = true,          // also redundant — BaseResponse() ctor already sets this
    TotalCount = result.TotalCount,
};
```

`BaseResponse` (the base class) already provides `ErrorCode` and `Params` for structured error context, so `ErrorMessage` is not needed for the error path either.

## Why it matters
- The field is serialised into the generated TypeScript client and appears in the API response shape — frontend code could inadvertently rely on it (expecting a non-null string on failure) while it is structurally always `null`.
- It is YAGNI dead code: no handler sets it, no test asserts on it, and `BaseResponse` already covers the error-reporting contract.
- The redundant `Success = true` assignment alongside the dead field suggests this response was copy-pasted from a template and never trimmed to fit its actual use case.

## Suggested fix
1. Delete `public string? ErrorMessage { get; set; }` from `RunExpeditionListPrintFixResponse.cs`.
2. Remove the explicit `Success = true` assignment in `RunExpeditionListPrintFixHandler.cs` — the base constructor already sets it.

No other callers or tests reference these two members, so the change is safe.

---
_Filed by daily arch-review routine on 2026-06-05._