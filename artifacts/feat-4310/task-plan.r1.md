# FileStorage DownloadFromUrlResponse Nullability Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change `DownloadFromUrlResponse.BlobUrl`, `BlobName`, `ContainerName` from non-nullable `string` (`= null!`) to nullable `string?`, so the compiler and the generated TypeScript client truthfully reflect that `DownloadFromUrlHandler.Failure()` never sets them, eliminating a `NullReferenceException` risk for any consumer that reads these fields on a failed response without gating on `Success`.

**Architecture:** One-file backend DTO change (`DownloadFromUrlResponse.cs`) with zero handler/controller logic changes, extended test coverage asserting the three properties are `null` on each of the handler's three failure paths, and a regeneration of the OpenAPI-derived TypeScript client so its types match the corrected contract.

**Tech Stack:** .NET 8, MediatR, xUnit + Moq, NSwag (TypeScript client generation), React/TypeScript frontend.

---

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

### task: regenerate-api-client

**Files:**
- Modify (generated, do not hand-edit content beyond what generation produces): `frontend/src/api/generated/api-client.ts`

**Depends on:** `fix-response-nullability` (the backend DTO must already be nullable before regenerating, so the OpenAPI schema reflects the fix).

- [ ] **Step 1: Regenerate the TypeScript client**

Run: `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`

Expected: succeeds, rewrites `frontend/src/api/generated/api-client.ts` in place. Confirm the `DownloadFromUrlResponse` interface (or class) now types `blobUrl`, `blobName`, `containerName` as optional/nullable (e.g. `blobUrl?: string | undefined;`) instead of a required `string`.

- [ ] **Step 2: Verify the frontend builds cleanly against the regenerated client**

Run: `cd frontend && npm run build`

Expected: PASS. This both re-runs client generation via the `prebuild` script (confirming Step 1's output is reproducible) and type-checks any code in `frontend/src/` that consumes `DownloadFromUrlResponse`'s fields — surfacing a compile error if any caller assumed non-null without a guard.

- [ ] **Step 3: Run frontend lint**

Run: `cd frontend && npm run lint`

Expected: PASS — no new lint errors from the regenerated client or any touched file.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(filestorage): regenerate TS client for nullable DownloadFromUrlResponse fields

Reflects the backend DownloadFromUrlResponse.BlobUrl/BlobName/ContainerName
nullability fix in the generated OpenAPI client."
```

If `git status` shows no changes to `api-client.ts` after Step 1 (NSwag output can be byte-identical if the schema's effective nullability metadata was already inferred correctly), skip this commit — there is nothing to commit, and that is an acceptable outcome, not a failure.
