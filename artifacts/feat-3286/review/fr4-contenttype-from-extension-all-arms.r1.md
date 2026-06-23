# Code Review: fr4-contenttype-from-extension-all-arms

## Summary
Implementation successfully replaces the placeholder test with comprehensive coverage of all 13 switch arms in `GetContentTypeFromExtension()` plus fallback and uppercase normalization. The Theory test with 15 InlineData rows covers all required MIME type mappings, and a dedicated Fact test validates uppercase extension handling. Build passes with no compilation errors.

## Review Result: PASS

### task: fr4-contenttype-from-extension-all-arms
**Status:** PASS
**Issues:** None

## Detailed Findings

### ✓ Placeholder Test Deleted
- Confirmed: No trace of `GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType` exists in the test file
- The old placeholder has been completely removed

### ✓ Theory Test: `DownloadFromUrlAsync_NoResponseContentType_InfersContentTypeFromExtension`
**Coverage:** 15 InlineData rows covering all switch arms

All required MIME type mappings validated:
1. `.jpg` → `image/jpeg` ✓
2. `.jpeg` → `image/jpeg` ✓
3. `.png` → `image/png` ✓
4. `.gif` → `image/gif` ✓
5. `.webp` → `image/webp` ✓
6. `.pdf` → `application/pdf` ✓
7. `.txt` → `text/plain` ✓
8. `.json` → `application/json` ✓
9. `.xml` → `application/xml` ✓
10. `.zip` → `application/zip` ✓
11. `.doc` → `application/msword` ✓
12. `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document` ✓
13. `.xls` → `application/vnd.ms-excel` ✓
14. `.xlsx` → `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` ✓
15. `.unknown` → `application/octet-stream` (fallback) ✓

### ✓ Fact Test: `DownloadFromUrlAsync_NoResponseContentType_UppercaseExtension_InfersContentType`
- Tests uppercase extension: `PHOTO.JPG` → expects `image/jpeg`
- Validates that `Path.GetExtension(fileName).ToLowerInvariant()` normalizes correctly
- Properly structured to force fallback path

### ✓ Test Technique: Fallback Path Forcing
Both tests correctly use `BuildNoContentTypeHandler()` helper:
- Creates `ByteArrayContent` with no `Content-Type` header
- Forces production code to fall back to `GetContentTypeFromExtension()`
- Ensures the method is exercised end-to-end via `DownloadFromUrlAsync()` integration

### ✓ Assertion Pattern: Moq Verification
Both tests properly assert via Moq `Verify()`:
```csharp
blobMock.Verify(x => x.UploadAsync(
    It.IsAny<Stream>(),
    It.Is<BlobUploadOptions>(opts => opts.HttpHeaders.ContentType == expectedContentType),
    It.IsAny<CancellationToken>()), Times.Once);
```
- Correctly checks `BlobUploadOptions.HttpHeaders.ContentType`
- Verifies exactly once (no duplicate uploads)
- Properly uses `It.Is<>()` to assert the MIME type value

### ✓ Implementation Verification
Source method `GetContentTypeFromExtension()` at `/backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs:298-318`:
- Uses 13 switch arms (covering `.jpg|.jpeg`, `.png`, `.gif`, `.webp`, `.pdf`, `.txt`, `.json`, `.xml`, `.zip`, `.doc`, `.docx`, `.xls`, `.xlsx`)
- Calls `.ToLowerInvariant()` before matching (supports uppercase extensions)
- Falls back to `application/octet-stream` for unknown extensions
- **Note:** The switch has `.jpg or .jpeg => ...` combining two extensions into one arm (counts as 1 implementation arm, but 2 test cases)

### ✓ Build Status
- Solution builds cleanly: `Build succeeded`
- No compilation errors in test file
- All syntactic patterns are correct

### ✓ Test Count
Class now contains:
- 18 Fact tests
- 3 Theory tests: 15 rows + 6 rows + 9 rows = 30 parametrized test cases
- **Total: 48 test cases** (18 + 30)
- FR-4 contribution: 1 Theory (15 cases) + 1 Fact (1 case) = **16 new test cases**

## Overall Notes
The implementation fully satisfies the task specification. The test structure is pedagogically sound:
1. The Theory test clearly documents all supported MIME types through labeled InlineData
2. The Fact test explicitly covers the uppercase normalization path
3. Both tests properly integrate with the production code via `BuildNoContentTypeHandler()` to ensure the fallback is exercised
4. Moq assertions are precise and directly verify the behavior described in the task

No regressions observed — existing test patterns (SetupContainerAndBlobClient, StubHttpMessageHandler) are used consistently throughout.
