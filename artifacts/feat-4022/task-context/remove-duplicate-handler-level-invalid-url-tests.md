### task: remove-duplicate-handler-level-invalid-url-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

This task removes the two tests that exercise the handler's own invalid-URL branch, while the branch still exists in production code (so the test suite stays green throughout this task — nothing in production code changes yet).

- [ ] Open `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` and delete the `Handle_InvalidUrl_ShouldReturnErrorResponse` theory test. Find this exact block (currently lines 140–159, immediately preceded by `Handle_ValidRequestWithoutBlobName_ShouldGenerateBlobName`'s closing `}` and a blank line, and immediately followed by a blank line then the `Handle_DifferentFileTypes_ShouldExtractCorrectBlobName` theory):

  ```csharp
      [Theory]
      [InlineData("not-a-url")]
      [InlineData("")]
      [InlineData("ftp://example.com/file.txt")]
      public async Task Handle_InvalidUrl_ShouldReturnErrorResponse(string invalidUrl)
      {
          // Arrange
          var request = new DownloadFromUrlRequest
          {
              FileUrl = invalidUrl,
              ContainerName = "documents",
          };

          // Act
          var result = await BuildHandler().Handle(request, CancellationToken.None);

          // Assert
          Assert.False(result.Success);
          Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
      }

  ```

  Delete the entire block above (all 20 lines shown, including the trailing blank line), so `Handle_ValidRequestWithoutBlobName_ShouldGenerateBlobName`'s closing brace is followed directly by the `[Theory]` attribute of `Handle_DifferentFileTypes_ShouldExtractCorrectBlobName`.

- [ ] In the same file, delete the `Handle_ValidationFailure_InvalidUrl_SetsCauseValidation` fact test. Find this exact block (currently lines 419–436, preceded by the closing `}` of `Handle_RedactsUrl_RemovesQueryString` and a blank line, followed by a blank line then `Handle_BlobStorageThrowsHttpRequestException_ReturnsFileDownloadFailed`):

  ```csharp
      [Fact]
      public async Task Handle_ValidationFailure_InvalidUrl_SetsCauseValidation()
      {
          // Arrange
          var request = new DownloadFromUrlRequest
          {
              FileUrl = "not-a-valid-url",
              ContainerName = "exports",
          };

          // Act
          var result = await BuildHandler().Handle(request, CancellationToken.None);

          // Assert
          Assert.False(result.Success);
          Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
          Assert.Equal("validation", result.Params!["cause"]);
      }

  ```

  Delete the entire block above (all 17 lines shown, including the trailing blank line).

- [ ] Confirm no other reference to the deleted test methods remains in the file (they are not called from anywhere else — `grep` for the method names to be sure):

  ```bash
  grep -n "Handle_InvalidUrl_ShouldReturnErrorResponse\|Handle_ValidationFailure_InvalidUrl_SetsCauseValidation" \
    backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
  ```

  Expected output: nothing (no matches, exit code 1).

- [ ] Build the solution to confirm the test file still compiles:

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors.

- [ ] Run the FileStorage test suite to confirm it is still green (production code is unchanged at this point, so all remaining tests — including the ones that still exist in `DownloadFromUrlHandlerTests.cs` — must pass exactly as before):

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FileStorage"
  ```

  Expected: all tests pass, 0 failed. The total test count for `DownloadFromUrlHandlerTests` drops by 4 (3 `[InlineData]` cases from the removed theory + 1 removed fact) compared to before this task.

- [ ] Commit:

  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
  git commit -m "test: remove handler-level invalid-URL tests duplicated by validator/pipeline tests"
  ```

---

