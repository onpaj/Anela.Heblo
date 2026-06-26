# Task: update-mock-implementation

## Summary

Updated `MockPhotobankGraphService.GetThumbnailAsync` to return `Task<GetThumbnailResult>` instead of `Task<GraphThumbnail?>`.

## Changes

**File:** `backend/src/Anela.Heblo.Application/Features/Photobank/Services/MockPhotobankGraphService.cs`

- Changed method return type from `Task<GraphThumbnail?>` to `Task<GetThumbnailResult>`
- Replaced nullable `GraphThumbnail?` local variable with `var thumbnail`
- Wrapped the `GraphThumbnail` value in `GetThumbnailResult.Success(thumbnail)` before returning

## Build result

Build produced 1 error (expected), located in `PhotobankGraphService.cs` — not in the mock:

```
error CS0738: 'PhotobankGraphService' does not implement interface member
'IPhotobankGraphService.GetThumbnailAsync(...)'.
'PhotobankGraphService.GetThumbnailAsync(...)' cannot implement
'IPhotobankGraphService.GetThumbnailAsync(...)' because it does not have
the matching return type of 'Task<GetThumbnailResult>'.
```

This error is expected and will be fixed in the next task. The mock itself compiled without errors.
