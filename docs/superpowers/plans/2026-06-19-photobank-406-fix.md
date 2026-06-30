# Photobank 406 Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `GET /api/photobank/photos/{id}/thumbnail/{size}` returning HTTP 502 when the Graph media endpoint returns 406, by treating 406 as "not thumbnailable" (returns `null` → HTTP 404 to caller) and logging a structured warning for any unexpected 4xx.

**Architecture:** The fix is confined to `PhotobankGraphService.GetThumbnailAsync` — insert a `LogWarning` block before `EnsureSuccessStatusCode()` that fires for any 4xx other than 404 and 429, then add an early `null` return for 406. The handler and controller already convert `null` to `PhotobankThumbnailNotFound` → HTTP 404, so no changes are needed outside the service and its test file.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Logging`, `System.Net.HttpStatusCode`, xUnit + Moq + FluentAssertions.

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs` (lines 185–187) |
| Modify (add test) | `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs` |

No new files. No new directories.

---

### task: add-406-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs`

Add a `[Fact]` to `PhotobankGraphServiceThumbnailTests` that verifies `GetThumbnailAsync` returns `null` when Graph returns HTTP 406. Write the test first (TDD) — it will fail until the implementation is added in the next task.

- [ ] **Step 1: Open the test file and locate the insertion point**

  The test file is at `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs`.
  Insert the new `[Fact]` method after the existing `GetThumbnailAsync_ReturnsNull_WhenGraphReturns404` test (ends at line 131) and before the `GetThumbnailAsync_ThrowsGraphThrottledException_WhenGraphReturns429` test (starts at line 133).

  Add this method:

  ```csharp
  [Fact]
  public async Task GetThumbnailAsync_ReturnsNull_WhenGraphReturns406()
  {
      // Arrange
      var handlerMock = new Mock<HttpMessageHandler>();
      var tokenMock = new Mock<ITokenAcquisition>();

      handlerMock
          .Protected()
          .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>())
          .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotAcceptable));

      var service = CreateService(handlerMock, tokenMock);

      // Act
      var result = await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

      // Assert
      result.Should().BeNull();
  }
  ```

  `HttpStatusCode.NotAcceptable` is in `System.Net` — it is already imported at the top of the file via `using System.Net;` (line 1).

- [ ] **Step 2: Run the new test to confirm it fails**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetThumbnailAsync_ReturnsNull_WhenGraphReturns406"
  ```

  Expected: **FAIL** — `HttpRequestException` is thrown (because `EnsureSuccessStatusCode()` fires on 406 in the current code), not `null` returned.

- [ ] **Step 3: Commit the failing test**

  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs
  git commit -m "test(photobank): add failing test for 406 thumbnail handling"
  ```

---

### task: implement-406-handling

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs:185-187`

Insert the warning log block and the 406 early-return between the end of the 429 block (line 184) and `EnsureSuccessStatusCode()` (line 187 — becomes line 195 after insertion).

- [ ] **Step 1: Locate the insertion point in PhotobankGraphService.cs**

  `GetThumbnailAsync` currently looks like this at lines 183–187 (after the 429 block):

  ```csharp
              throw new GraphThrottledException(retryAfter);
          }

          response.EnsureSuccessStatusCode();
  ```

- [ ] **Step 2: Insert the warning log and 406 early-return**

  Replace the blank line + `EnsureSuccessStatusCode()` section so it reads:

  ```csharp
              throw new GraphThrottledException(retryAfter);
          }

          // Log a warning for any 4xx that is not 404 (already handled) or 429 (already handled).
          if (!response.IsSuccessStatusCode
              && response.StatusCode is not System.Net.HttpStatusCode.NotFound
              && response.StatusCode is not System.Net.HttpStatusCode.TooManyRequests)
          {
              _logger.LogWarning(
                  "Graph thumbnail request returned {StatusCode} for drive {DriveId} item {FileId}. URL: {Url}",
                  (int)response.StatusCode, driveId, fileId, url);
          }

          // 406 means the item cannot be thumbnailed (permanent). Surface as null → 404 to caller.
          if (response.StatusCode == System.Net.HttpStatusCode.NotAcceptable)
              return null;

          response.EnsureSuccessStatusCode();
  ```

  Notes:
  - `url` is already in scope (defined at line 165).
  - `driveId` and `fileId` are method parameters — already in scope.
  - `_logger` is the injected `ILogger<PhotobankGraphService>` — already in scope.
  - `System.Net.HttpStatusCode` is in the BCL; no new `using` is needed since the file already references `System.Net.HttpStatusCode.NotFound` and `System.Net.HttpStatusCode.TooManyRequests` by their fully-qualified names in the same method.

- [ ] **Step 3: Run the new 406 test to confirm it now passes**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetThumbnailAsync_ReturnsNull_WhenGraphReturns406"
  ```

  Expected: **PASS**

- [ ] **Step 4: Run the full Photobank thumbnail test suite to confirm no regressions**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PhotobankGraphServiceThumbnailTests"
  ```

  Expected: **All 7 tests PASS** (the original 6 plus the new 406 test).

- [ ] **Step 5: Build the backend to confirm no compile errors**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: **Build succeeded, 0 error(s)**.

- [ ] **Step 6: Run dotnet format to confirm style compliance**

  ```bash
  dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
  ```

  If formatting issues are reported, run without `--verify-no-changes` to auto-fix:

  ```bash
  dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

- [ ] **Step 7: Commit the implementation**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs
  git commit -m "fix(photobank): handle 406 from Graph as not-thumbnailable, log warning for unexpected 4xx"
  ```

---

## Verification Checklist

After both tasks are committed, confirm:

- [ ] `PhotobankGraphServiceThumbnailTests` — all 7 tests pass (run command from task 2, step 4)
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `GetThumbnailAsync` source reads: warn log block → 406 null return → `EnsureSuccessStatusCode()` — in that order, after the 429 block
- [ ] No changes to `IPhotobankGraphService`, `GetThumbnailHandler`, `GetThumbnailRequest`, `GetThumbnailResponse`, `PhotobankController`, or `ErrorCodes`
