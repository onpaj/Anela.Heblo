# move-photobankgraphservice-to-adapter — Code Review

## Review Result: PASS

### task: move-photobankgraphservice-to-adapter

**Status:** PASS

---

## Acceptance Criteria Verification

| Criterion | Status | Notes |
|-----------|--------|-------|
| `PhotobankGraphService.cs` deleted from Application layer | ✅ PASS | Verified: `/backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs` does NOT exist |
| New file exists at correct location | ✅ PASS | `/backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs` present |
| Correct namespace | ✅ PASS | `Anela.Heblo.Adapters.Microsoft365.Photobank` (line 9) |
| `GetThumbnailAsync` returns `Task<GetThumbnailResult>` | ✅ PASS | Method signature (line 117–121) returns `Task<GetThumbnailResult>` |
| Non-throwing implementation | ✅ PASS | All exception paths return result variants, never rethrow |
| Catches `MsalException` → `AuthUnavailable` | ✅ PASS | Lines 128–132: catches `MsalException`, returns `new GetThumbnailResult.AuthUnavailable(ex)` |
| Catches `HttpRequestException` → `UpstreamError` | ✅ PASS | Lines 149–153: catches `HttpRequestException`, returns `new GetThumbnailResult.UpstreamError(ex)` |
| HTTP 404 → `NotFound` | ✅ PASS | Line 157–158 |
| HTTP 406 → `NotFound` | ✅ PASS | Line 160–161 |
| HTTP 429 → `Throttled` | ✅ PASS | Lines 163–174: parses `Retry-After` header, returns `new GetThumbnailResult.Throttled(retryAfter)` |
| HTTP 200 → `Success` | ✅ PASS | Lines 176–190: `IsSuccessStatusCode` branch returns `new GetThumbnailResult.Success(...)` with stream and metadata |
| `GetDeltaAsync` present | ✅ PASS | Lines 44–97 |
| `ResolveItemIdAsync` present | ✅ PASS | Lines 99–115 |
| Private helpers present | ✅ PASS | `MapItem`, `CreateRequest`, `DeserializeAsync`, and all private DTOs present (lines 194–335) |
| No `GraphThrottledException` anywhere | ✅ PASS | Grep confirms: no occurrences in file |
| Adapter project builds | ✅ PASS | `dotnet build Anela.Heblo.Adapters.Microsoft365.csproj` succeeds with 0 errors in adapter project |

---

## Technical Assessment

### Correctness
- **Exception handling:** Proper coverage of MSAL, HTTP transport, and Graph status codes. All error paths map to appropriate `GetThumbnailResult` discriminated union variants.
- **Status code handling:** Correct mapping of 404 and 406 to `NotFound`, 429 to `Throttled` with retry-after parsing, other non-2xx to `UpstreamError`.
- **Method signatures:** All public methods match interface contract (token acquisition, cancellation support, proper return types).
- **Private classes:** All Graph deserializer DTOs preserved correctly with proper JSON property names.

### Architecture
- **Clean separation:** Service cleanly moved to Adapter layer; no Application layer logic remains.
- **No cross-layer references:** File properly uses Application interface (`IPhotobankGraphService`) as contract, not implementation detail.
- **Dependent code isolation:** Three known build errors isolated to dependent Application layer files (PhotobankModule.cs, GetThumbnailHandler.cs) — all expected and scoped to subsequent tasks.

### Code Quality
- **Logging:** Appropriate warning/error level logging at key decision points (line 130, 151, 172, 179).
- **Resource handling:** `using` statement on `response` (line 155) and `using` on `client` (line 51) properly manage HTTP resources.
- **Stream handling:** MemoryStream correctly positioned (line 188) before return.

---

## Summary

Implementation is **complete and correct**. All acceptance criteria met. Adapter project compiles without errors. Ready for dependent integration tasks (`wire-adapter-di-registration`, `rewrite-getthumbnailhandler`).
