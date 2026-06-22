# introduce-getthumbnailresult-du — Implementation Summary

## Changes made

**File:** `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs`

1. **Deleted** `GraphThrottledException` class (previously between `GraphThumbnail` and `GraphPhotoItem`).

2. **Added** `GetThumbnailResult` abstract discriminated union immediately after `GraphThumbnail`, with five sealed cases:
   - `Success` — wraps a `GraphThumbnail`
   - `NotFound` — empty sentinel
   - `Throttled` — carries `TimeSpan? RetryAfter`
   - `UpstreamError` — carries `Exception Cause`
   - `AuthUnavailable` — carries `Exception Cause`

3. **Updated** `IPhotobankGraphService.GetThumbnailAsync` return type from `Task<GraphThumbnail?>` to `Task<GetThumbnailResult>`.

## Build result

`Anela.Heblo.Application.csproj` build produces 2 expected errors in implementation classes:
- `MockPhotobankGraphService.GetThumbnailAsync` — return type mismatch (CS0738)
- `PhotobankGraphService.GetThumbnailAsync` — return type mismatch (CS0738)

These errors are anticipated; the implementations will be updated in subsequent tasks. The interface file itself compiles correctly.
