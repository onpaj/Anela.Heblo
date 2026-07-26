### task: remove-container-name-validation-from-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:61-74,199-221`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

This task removes the now-redundant inline container-name check and its private helper from the handler (the validator added in the previous task fully owns this rule from here on), and trims the handler's own test file down to the cases it still owns (URL-format validation + orchestration).

- [ ] **Step 1: Remove the inline container-name check from `Handle`**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`, delete this block (currently lines 61–74, immediately after the URL-format validation block and before `var redactedUrl = RedactUrl(request.FileUrl);`):

```csharp
        if (!IsValidContainerName(request.ContainerName))
        {
            _logger.LogWarning("Invalid container name: {ContainerName}", request.ContainerName);
            return new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidContainerName,
                Params = new Dictionary<string, string>
                {
                    ["containerName"] = request.ContainerName,
                    ["cause"] = "validation",
                },
            };
        }

```

So that `Handle` reads, immediately after the URL-format validation's closing `}`:

```csharp
        var redactedUrl = RedactUrl(request.FileUrl);
        var sw = Stopwatch.StartNew();
        int attemptCount = 0;
```

- [ ] **Step 2: Remove the `IsValidContainerName` helper method**

In the same file, delete this method (currently lines 199–221, located between `RedactUrl` and `GetBlobNameFromUrl`):

```csharp
    private static bool IsValidContainerName(string containerName)
    {
        if (string.IsNullOrEmpty(containerName) || containerName.Length < 3 || containerName.Length > 63)
            return false;

        if (containerName != containerName.ToLowerInvariant())
            return false;

        if (!char.IsLetterOrDigit(containerName[0]) || !char.IsLetterOrDigit(containerName[^1]))
            return false;

        for (int i = 0; i < containerName.Length; i++)
        {
            var c = containerName[i];
            if (!char.IsLetterOrDigit(c) && c != '-')
                return false;

            if (c == '-' && i < containerName.Length - 1 && containerName[i + 1] == '-')
                return false;
        }

        return true;
    }

```

So that `RedactUrl` and `GetBlobNameFromUrl` are directly adjacent, with no `IsValidContainerName` method between them. Confirm `DownloadFromUrlHandler.cs` no longer contains the strings `IsValidContainerName` or `ErrorCodes.InvalidContainerName` anywhere.

- [ ] **Step 3: Remove the container-name theory cases from `DownloadFromUrlHandlerTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`, delete these three test methods in full (their coverage now lives in `DownloadFromUrlRequestValidatorTests` and `FileStorageValidationPipelineTests`, added in the previous task):

Delete `Handle_InvalidContainerName_ShouldReturnErrorResponse`:

```csharp
    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("very-long-container-name-that-exceeds-sixty-three-characters-limit")]
    [InlineData("InvalidCase")]
    [InlineData("invalid--double-hyphen")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    [InlineData("invalid_underscore")]
    public async Task Handle_InvalidContainerName_ShouldReturnErrorResponse(string invalidContainerName)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = invalidContainerName,
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidContainerName, result.ErrorCode);
    }

```

Delete `Handle_ValidContainerName_ShouldSucceed`:

```csharp
    [Theory]
    [InlineData("valid-container")]
    [InlineData("container123")]
    [InlineData("my-container-name")]
    [InlineData("abc")]
    [InlineData("container-with-exactly-sixty-three-characters-in-total-length")]
    public async Task Handle_ValidContainerName_ShouldSucceed(string validContainerName)
    {
        // Arrange
        var blobUrl = $"https://mock.blob.core.windows.net/{validContainerName}/file.txt";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                validContainerName,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = validContainerName,
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(validContainerName, result.ContainerName);
    }

```

Delete `Handle_ValidationFailure_InvalidContainerName_SetsCauseValidation`:

```csharp
    [Fact]
    public async Task Handle_ValidationFailure_InvalidContainerName_SetsCauseValidation()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "INVALID_UPPERCASE",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidContainerName, result.ErrorCode);
        Assert.Equal("validation", result.Params!["cause"]);
    }

```

Leave every other test in the file untouched, including `Handle_InvalidUrl_ShouldReturnErrorResponse`, `Handle_ValidRequest_ShouldReturnSuccessResponse`, `Handle_ReturnsSuccess_OnHappyPath`, and all the orchestration/error-mapping tests — none of these reference container-name validation and all continue to use a plain valid container name (e.g. `"documents"`, `"exports"`) as before.

- [ ] **Step 4: Build and run the full backend test suite**

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test
```

Confirm: build succeeds with no new warnings; `dotnet format --verify-no-changes` passes; the full test suite passes, including:
- `DownloadFromUrlHandlerTests` (trimmed, still compiles, remaining tests green)
- `DownloadFromUrlRequestValidatorTests` (from the previous task, still green)
- `FileStorageValidationPipelineTests` (from the previous task, still green)
- `FileStorageControllerTests` (untouched, still green)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
git commit -m "Remove inline container-name validation from DownloadFromUrlHandler

The rule now lives solely in DownloadFromUrlRequestValidator, enforced
by the MediatR pipeline before Handle runs. Handler-level container-
name test cases are relocated to DownloadFromUrlRequestValidatorTests
and FileStorageValidationPipelineTests."
```

---

## Self-review: spec coverage, no placeholders, cross-task consistency

**FR-1 (extract the validator):** Covered by `add-container-name-validator-and-pipeline-wiring` Step 1 — `DownloadFromUrlRequestValidator` created at the exact spec'd path/namespace (`Features/FileStorage/Validators/`, `Anela.Heblo.Application.Features.FileStorage.Validators`), with the `RuleFor(...).Must(...).WithErrorCode(...).WithState(...).WithMessage(...)` rule copied verbatim from the spec, and `IsValidContainerName` copied verbatim (character-for-character) from `DownloadFromUrlHandler.cs`.

**FR-2 (wire into the MediatR pipeline via `ValidationResultBehavior`):** Covered by Step 2 — the exact two-line `AddScoped` registration from the spec/arch-review, in `FileStorageModule.AddFileStorageModule`, with the three `using` additions the arch-review explicitly flagged as missing from the spec (`FluentValidation`, `Common.Behaviors`, `Validators`) plus `MediatR` (needed for `IPipelineBehavior`) and `UseCases.DownloadFromUrl` (needed for the request/response types), spelled out in full rather than assumed. End-to-end short-circuit + contract-preservation proven by Step 4's `FileStorageValidationPipelineTests`.

**FR-3 (remove validation from the handler):** Covered entirely by `remove-container-name-validation-from-handler` Steps 1–2 — both the inline `if` block and the `IsValidContainerName` method are deleted with exact before/after code shown, and Step 2 explicitly directs confirming no remaining references to `IsValidContainerName` or `ErrorCodes.InvalidContainerName` in the handler file.

**FR-4 (update existing tests to match the new validation location):** Covered by Step 3 of the removal task (delete the three container-name-specific handler tests, verbatim, leaving all others intact) plus Steps 3–4 of the first task (new `DownloadFromUrlRequestValidatorTests` porting every `[InlineData]` case from the old theories, and the new `FileStorageValidationPipelineTests` proving the DI-wiring/short-circuit behavior that the arch-review's risk table flagged as otherwise untested). `FileStorageControllerTests.cs` is explicitly left untouched per spec.

**No placeholders:** Every step in both tasks contains complete, paste-ready C# — no "similar to Task N", no "add tests for the above", no elided method bodies. The `IsValidContainerName` predicate, the FluentValidation rule, the DI registration block, both new test classes, and both handler-file edits are each given in full.

**Cross-task consistency:**
- Validator class name `DownloadFromUrlRequestValidator` / namespace `Anela.Heblo.Application.Features.FileStorage.Validators` is identical across the validator file, `FileStorageModule.cs`'s `using`, `DownloadFromUrlRequestValidatorTests.cs`, and `FileStorageValidationPipelineTests.cs`.
- Error code usage is identical everywhere: `((int)ErrorCodes.InvalidContainerName).ToString()` in the rule builder; `ErrorCodes.InvalidContainerName` (enum, not string) in all test assertions; `1802` never hard-coded as a literal anywhere.
- `WithState` dictionary keys (`"containerName"`, `"cause"` → `"validation"`) match between the validator, the validator tests' `CustomState` assertions, and the pipeline test's `Params` assertions — identical to what the handler previously constructed manually.
- File paths referenced in both tasks' `Files:` sections and `git add` commands match exactly what Steps 1–4 of each task create/modify — no drift between the declared file list and the actual edits.
- Task ordering is safe to run sequentially: task 1 is purely additive (build stays green with the rule temporarily duplicated); task 2 only deletes code/tests that task 1 has already made redundant, and re-runs the full suite before committing.
