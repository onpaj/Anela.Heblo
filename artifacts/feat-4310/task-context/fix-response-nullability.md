### task: fix-response-nullability


**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

- [ ] **Step 1: Write the failing tests — assert null properties on each failure path**

Add null-property assertions to the three existing failure-path tests in `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`. These currently only assert `Success`/`ErrorCode`/`Params` — extend each with an assertion on `BlobUrl`, `BlobName`, `ContainerName`.

In `Handle_RetryExhausted_ReturnsFailure_With_Cause_RetryExhausted` (existing test, currently ending at `Assert.DoesNotContain("token=secret", result.Params["fileUrl"]);`), append:

```csharp
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
```

In `Handle_HardHttpStatus_ReturnsFailure_With_Cause_HttpStatus` (existing test, currently ending at `Assert.Equal("1", result.Params["attemptCount"]);`), append:

```csharp
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
```

In `Handle_InnerTimeout_ReturnsFailure_With_Cause_Timeout` (existing test, currently ending at `Assert.Equal("timeout", result.Params!["cause"]);`), append:

```csharp
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
```

These three tests together cover all three `catch` blocks in `DownloadFromUrlHandler.Handle()` (timeout, `HttpRequestException`, generic `Exception` — the third is already separately covered by `Handle_UnexpectedException_ReturnsFileDownloadFailed`, which asserts `Success`/`ErrorCode`/`Params["cause"]`; extend it too):

In `Handle_UnexpectedException_ReturnsFileDownloadFailed` (existing test, currently ending at `Assert.Equal("retry-exhausted", result.Params!["cause"]);`), append:

```csharp
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: **build FAILS** with CS0019-class errors — `Assert.Null(result.BlobUrl)` etc. are legal C#, so this step does not fail to compile on the assertions themselves. Instead run the tests directly to observe the real failure mode:

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadFromUrlHandlerTests"`
Expected: **all four tests still PASS** at this point, because `BlobUrl`/`BlobName`/`ContainerName` are already `null` at runtime today (the bug is that the *type* claims otherwise, not that the *value* is wrong) — `Assert.Null` on a `string` typed `= null!` still evaluates the actual runtime value, which is `null`. This step confirms the test additions compile and pass against current (buggy) code, establishing a baseline before the type change. There is no red step here because this is a type-correctness fix, not a behavior fix — the runtime behavior was already correct; only the compile-time contract was wrong.

- [ ] **Step 3: Change the three properties to nullable**

Edit `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs` to:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;

public class DownloadFromUrlResponse : BaseResponse
{
    public string? BlobUrl { get; set; }

    public string? BlobName { get; set; }

    public string? ContainerName { get; set; }

    public long FileSizeBytes { get; set; }
}
```

- [ ] **Step 4: Run the full FileStorage test suite to verify everything still passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage"`
Expected: PASS — all `DownloadFromUrlHandlerTests`, `FileStorageControllerTests`, `AzureBlobStorageServiceTests`, `MockBlobStorageServiceTests`, `FileStorageValidationPipelineTests`, `DownloadFromUrlRequestValidatorTests` tests green, including the four extended in Step 1.

- [ ] **Step 5: Build the whole backend to confirm no new nullable warnings/errors elsewhere**

Run: `dotnet build`
Expected: PASS — no new CS86xx nullable-reference warnings introduced (the change only relaxes a type; it cannot newly violate nullable-reference rules anywhere else in the solution since no other type references `DownloadFromUrlResponse.BlobUrl`/`BlobName`/`ContainerName` outside the handler and its own tests).

- [ ] **Step 6: Run `dotnet format` and commit**

Run: `dotnet format`

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
git commit -m "fix(filestorage): make DownloadFromUrlResponse blob fields nullable

Failure() never set BlobUrl/BlobName/ContainerName, but they were
declared non-nullable string with the null-forgiving operator,
suppressing the compiler warning and risking a NullReferenceException
for any caller that reads them without checking Success first."
```

---
