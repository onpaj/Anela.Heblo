### task: update-handler-to-use-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs:21-33`
- Test: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` (existing tests, no new test needed — behavior is unchanged)

- [ ] **Step 1: Confirm the existing behavioral test still describes the required contract**

The existing test `Handle_ReturnsFailure_WhenDateIsInvalid` (already in the test file, added before this plan) asserts:

```csharp
Assert.False(result.Success);
Assert.Equal(ErrorCodes.InvalidFormat, result.ErrorCode);
Assert.NotNull(result.Params);
Assert.Equal("Date", result.Params!["Field"]);
Assert.Equal("yyyy-MM-dd", result.Params!["ExpectedFormat"]);
Assert.Empty(result.Items);
_blobStoreMock.Verify(s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
```

This test already fully specifies the required post-refactor behavior of the handler and needs no changes. Run it now to confirm it currently passes against the pre-refactor handler:

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.Handle_ReturnsFailure_WhenDateIsInvalid"`
Expected: PASS (5 theory cases)

- [ ] **Step 2: Update the handler to delegate to the factory**

Modify `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`. Replace lines 21–33:

```csharp
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return new GetExpeditionListsByDateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidFormat,
                Params = new Dictionary<string, string>
                {
                    { "Field", "Date" },
                    { "ExpectedFormat", "yyyy-MM-dd" }
                }
            };
        }
```

with:

```csharp
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return GetExpeditionListsByDateResponse.InvalidDate();
        }
```

The full method body after this edit:

```csharp
    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return GetExpeditionListsByDateResponse.InvalidDate();
        }

        var blobs = await _blobStore.ListBlobsAsync(_containerName, request.Date, cancellationToken);

        var items = blobs
            .Where(b => b.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(b => new ExpeditionListItemDto
            {
                BlobPath = b.Name,
                FileName = b.FileName,
                ListId = Path.GetFileNameWithoutExtension(b.FileName),
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength
            })
            .ToList();

        return new GetExpeditionListsByDateResponse { Items = items };
    }
```

- [ ] **Step 3: Build and check for an unused `using` directive**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds. If the compiler/analyzer emits a warning that `using Anela.Heblo.Application.Shared;` (line 2 of the handler file) is now unused, remove that single `using` line from `GetExpeditionListsByDateHandler.cs`. If no such warning appears, leave the `using` directives untouched — do not remove anything speculatively.

- [ ] **Step 4: Run the full test file to verify no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"`
Expected: PASS — all tests in the file, including `Handle_ReturnsItemsForDate`, `Handle_FiltersPdfFilesOnly`, `Handle_ReturnsFailure_WhenDateIsInvalid` (5 cases), and `InvalidDate_ReturnsExpectedFailureShape` from the previous task.

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — no other test in the solution references `GetExpeditionListsByDateResponse` construction directly, so no other file should be affected. If any other test fails, stop and investigate before proceeding (systematic-debugging) — do not paper over an unrelated failure.

- [ ] **Step 6: Format and final build check**

Run: `dotnet format backend/Anela.Heblo.sln`
Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Both succeed with no new warnings introduced by this change.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs
git commit -m "refactor(expedition-list-archive): delegate invalid-date response construction to GetExpeditionListsByDateResponse.InvalidDate()"
```

---

