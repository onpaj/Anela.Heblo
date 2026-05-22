# Catalog Documents — Switch Upload to OBO Delegated Auth

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace app-only auth with user-delegated (OBO) auth for SharePoint upload calls in `GraphCatalogDocumentsStorage`, and remove the now-redundant `CatalogDocumentsUpload` authorization policy.

**Architecture:** Reads (folder discovery, file listing) keep app-only auth so every user sees the same list. Writes (upload) switch to `GetAccessTokenForUserAsync` with `Files.ReadWrite.All` scope, using the same single-flight token cache and `MsalUiRequiredException` wrap that `GraphPlannerService` already uses. The `CatalogDocumentsUpload` policy and `catalog_manager` claim in mock auth are removed because SharePoint is now the source of truth for write permission.

**Tech Stack:** C# / ASP.NET Core / Microsoft Identity Web (`ITokenAcquisition`) / xUnit / Moq / FluentAssertions

---

## Files touched

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` | Add delegated token helpers; switch `UploadFileAsync` to use them |
| `backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs` | Remove `[Authorize(Policy = CatalogDocumentsUpload)]` from both upload endpoints |
| `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` | Remove `CatalogDocumentsUpload` policy registration |
| `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs` | Remove `CatalogDocumentsUpload` policy constant |
| `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs` | Remove `CatalogManager` role claim (dead code after policy removal) |
| `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs` | New: unit tests for delegated token behavior |

---

## Task 1: Write failing tests for delegated token in `GraphCatalogDocumentsStorage`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class GraphCatalogDocumentsStorageTests
{
    private const string AppToken = "app-token";
    private const string DelegatedToken = "delegated-token";

    private static (GraphCatalogDocumentsStorage Storage, Mock<ITokenAcquisition> TokenAcquisition, RecordingHandler Handler)
        CreateStorage(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync(AppToken);
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null))
            .ReturnsAsync(DelegatedToken);

        var handler = new RecordingHandler(responder);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(new HttpClient(handler));

        var storage = new GraphCatalogDocumentsStorage(
            tokenAcquisition.Object,
            factory.Object,
            NullLogger<GraphCatalogDocumentsStorage>.Instance);

        return (storage, tokenAcquisition, handler);
    }

    // ─── UploadFileAsync — delegated token ───────────────────────────────────

    [Fact]
    public async Task UploadFileAsync_UsesUserDelegatedToken_NotAppToken()
    {
        // Arrange
        var (storage, tokenAcquisition, handler) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"test.pdf"}""",
                    Encoding.UTF8, "application/json")
            });

        // Act
        using var stream = new MemoryStream(new byte[100]);
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", 100);

        // Assert — token in Authorization header must be the delegated one
        handler.Requests.Should().NotBeEmpty();
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be(DelegatedToken);

        tokenAcquisition.Verify(
            t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null),
            Times.Once,
            "upload must acquire a delegated token");
        tokenAcquisition.Verify(
            t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null),
            Times.Never,
            "upload must not fall back to the app token");
    }

    [Fact]
    public async Task FindFolderAsync_UsesAppToken_NotDelegatedToken()
    {
        // Arrange
        var (storage, tokenAcquisition, handler) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"f1","name":"MAT001__TDS","folder":{"childCount":0}}]}""",
                    Encoding.UTF8, "application/json")
            });

        // Act
        await storage.FindFolderAsync("drive-1", "/Materials", "MAT001__", false);

        // Assert
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be(AppToken);
        tokenAcquisition.Verify(
            t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null),
            Times.Once);
        tokenAcquisition.Verify(
            t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null),
            Times.Never);
    }

    [Fact]
    public async Task UploadFileAsync_WhenConsentMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null))
            .ThrowsAsync(new MsalUiRequiredException("invalid_grant", "AADSTS65001: consent required"));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph"))
            .Returns(new HttpClient(new RecordingHandler(_ =>
                throw new InvalidOperationException("Graph must not be called when token acquisition failed"))));

        var storage = new GraphCatalogDocumentsStorage(
            tokenAcquisition.Object,
            factory.Object,
            NullLogger<GraphCatalogDocumentsStorage>.Instance);

        // Act
        using var stream = new MemoryStream(new byte[100]);
        var act = () => storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", 100);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Microsoft 365 consent required*");
    }

    // ─── Recording infrastructure ─────────────────────────────────────────────

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
```

- [ ] **Step 2: Run tests to confirm they FAIL (RED)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphCatalogDocumentsStorageTests" \
  --no-build 2>&1 | tail -20
```

Expected: tests compile but `UploadFileAsync_UsesUserDelegatedToken_NotAppToken` and `UploadFileAsync_WhenConsentMissing_ThrowsInvalidOperationException` FAIL. `FindFolderAsync_UsesAppToken_NotDelegatedToken` should PASS (existing behavior unchanged).

Note: If the project hasn't been built yet, run `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` first, then re-run the filter command without `--no-build`.

---

## Task 2: Implement delegated token in `GraphCatalogDocumentsStorage`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`

- [ ] **Step 3: Add delegated scope constant, single-flight field, and helper methods**

Open `GraphCatalogDocumentsStorage.cs`. After the existing `UploadSessionThresholdBytes` constant (line 16) and before the constructor, add:

```csharp
private const string DelegatedUploadScope = "https://graph.microsoft.com/Files.ReadWrite.All";

// Single-flight delegated-token cache (service is Scoped — one instance per request).
private Task<string>? _delegatedTokenTask;
```

Then at the bottom of the class, before the closing `}`, add the two private methods:

```csharp
private Task<string> GetDelegatedTokenAsync()
{
    return _delegatedTokenTask ??= AcquireDelegatedTokenAsync();
}

private async Task<string> AcquireDelegatedTokenAsync()
{
    try
    {
        return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedUploadScope });
    }
    catch (Microsoft.Identity.Client.MsalUiRequiredException ex)
    {
        _logger.LogError(ex,
            "User consent required for Graph scope {Scope}. Grant admin consent in Azure Portal.",
            DelegatedUploadScope);
        throw new InvalidOperationException(
            $"Microsoft 365 consent required for scope {DelegatedUploadScope}. An admin must grant consent in Azure Portal.", ex);
    }
}
```

- [ ] **Step 4: Switch `UploadFileAsync` to use the delegated token**

In `UploadFileAsync` (currently line 127), replace:
```csharp
var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
```
with:
```csharp
var token = await GetDelegatedTokenAsync();
```

Leave `FindFolderAsync` (line 34) and `ListFilesAsync` (line 92) unchanged — they keep `GetAccessTokenForAppAsync`.

- [ ] **Step 5: Run tests to confirm they PASS (GREEN)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphCatalogDocumentsStorageTests" \
  --no-build 2>&1 | tail -10
```

Expected: all 3 tests PASS.

- [ ] **Step 6: Run the full CatalogDocuments test suite (regression)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "CatalogDocuments" \
  --no-build 2>&1 | tail -15
```

Expected: all tests PASS (handler tests mock `ICatalogDocumentsStorage` and are unaffected by this change).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs \
        backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs
git commit -m "feat(catalog-documents): switch upload to user-delegated OBO auth"
```

---

## Task 3: Remove `CatalogDocumentsUpload` policy from controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs`

- [ ] **Step 8: Remove policy attribute from both upload endpoints**

In `CatalogDocumentsController.cs`:

On line 50, remove:
```csharp
[Authorize(Policy = AuthorizationConstants.Policies.CatalogDocumentsUpload)]
```
from the `UploadMaterialDocument` action.

On line 82, remove:
```csharp
[Authorize(Policy = AuthorizationConstants.Policies.CatalogDocumentsUpload)]
```
from the `UploadPifDocument` action.

The controller-level `[Authorize]` on line 14 (default `HebloUser` policy) stays — users still need to be authenticated app users.

After the change, both upload methods should look like this (no extra `[Authorize]`):

```csharp
[HttpPost("materials/{productCode}")]
[RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
public async Task<ActionResult<UploadDocumentResponse>> UploadMaterialDocument(
    ...

[HttpPost("pif/{productCode}")]
[RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
public async Task<ActionResult<UploadDocumentResponse>> UploadPifDocument(
    ...
```

Also remove the now-unused `using Anela.Heblo.Domain.Features.Authorization;` import from the top of the file, if no other references to `AuthorizationConstants` remain in the controller.

- [ ] **Step 9: Verify the controller still compiles**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore 2>&1 | tail -10
```

Expected: 0 errors.

---

## Task 4: Remove `CatalogDocumentsUpload` policy registration and constant

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs`

- [ ] **Step 10: Remove policy registration from `AuthenticationExtensions.cs`**

In `ConfigureAuthorizationPolicies` (lines 120–122), remove:

```csharp
options.AddPolicy(AuthorizationConstants.Policies.CatalogDocumentsUpload, policy =>
    policy.RequireAuthenticatedUser()
          .RequireRole(AuthorizationConstants.Roles.CatalogManager));
```

The method body after the change should contain only `KnowledgeBaseUpload` and `MarketingReader` policy registrations.

- [ ] **Step 11: Remove `CatalogDocumentsUpload` constant from `AuthorizationConstants.cs`**

In the `Policies` nested class (lines 70–72), remove:

```csharp
/// <summary>
/// Policy required for uploading catalog documents to SharePoint
/// </summary>
public const string CatalogDocumentsUpload = "CatalogDocumentsUpload";
```

Keep `KnowledgeBaseUpload` and `MarketingReader`. Keep the `Roles.CatalogManager` constant — it may be referenced elsewhere in the future.

- [ ] **Step 12: Build to confirm no remaining references**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore 2>&1 | tail -10
```

Expected: 0 errors. If there's a CS0103 for `CatalogDocumentsUpload`, track down the remaining reference and remove it.

- [ ] **Step 13: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs \
        backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
git commit -m "fix(catalog-documents): remove CatalogDocumentsUpload policy — SharePoint enforces write access"
```

---

## Task 5: Revert `CatalogManager` claim from `MockAuthenticationHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`

- [ ] **Step 14: Remove the `CatalogManager` role claim (line 39)**

In `HandleAuthenticateAsync`, remove:

```csharp
new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.CatalogManager),
```

This was added in commit `cc336fb2` as a workaround for the policy that no longer exists. Mock auth environments use `NoOpCatalogDocumentsStorage`, so this claim has no effect there regardless.

- [ ] **Step 15: Build and run full suite**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore 2>&1 | tail -5 && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "CatalogDocuments" \
  --no-build 2>&1 | tail -15
```

Expected: 0 build errors, all CatalogDocuments tests PASS.

- [ ] **Step 16: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs
git commit -m "chore(auth): remove dead CatalogManager claim from MockAuthenticationHandler"
```

---

## Verification checklist (from spec)

- [ ] `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` succeeds
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "CatalogDocuments"` — all pass
- [ ] Mock-auth local dev: upload UI still works (uses `NoOpCatalogDocumentsStorage`, no Graph call)
- [ ] **Manual — real auth happy path**: logged-in user with SP write access uploads → 200 OK; SP audit shows the real user (not service principal)
- [ ] **Manual — real auth denied**: user without SP write access uploads → backend propagates Graph 403 (500 to client confirms SP is gating)
- [ ] **Manual — missing admin consent**: first deploy without `Files.ReadWrite.All` delegated permission granted → logs show `InvalidOperationException: Microsoft 365 consent required for scope ...`

> **Azure Portal action required (manual, by admin):** Add delegated permission `Files.ReadWrite.All` (Microsoft Graph) to the app registration and grant admin consent before testing real-auth paths.
