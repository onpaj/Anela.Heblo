# Smartsupp Close Conversation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a one-click "Uzavřít konverzaci" button to the ConversationDetail header that calls Smartsupp's REST API to resolve the conversation, with toast feedback.

**Architecture:** The backend gets a new `CloseConversationAsync` method on `ISmartsuppApiClient`, backed by a MediatR handler and a `POST api/smartsupp/conversations/{id}/close` controller endpoint. The frontend adds a `useCloseConversation` mutation hook in `useSmartsupp.ts` and renders a conditional button in `ConversationDetail.tsx` — visible only when `conversation.status === 'open'`.

**Tech Stack:** .NET 8 / MediatR / xUnit / FluentAssertions / Moq — React 18 / react-query v5 / react-hot-toast / Jest / React Testing Library

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/CloseConversationHandlerTests.cs`
- `frontend/src/api/hooks/__tests__/useCloseConversation.test.ts`

**Modify:**
- `docs/integrations/smartsupp-api.md` — document close endpoint before any code
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `SmartsuppCloseConversationUnavailable = 2708`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs` — add `CloseConversationAsync`
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs` — implement `CloseConversationAsync`
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` — add POST endpoint
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs` — add close tests
- `frontend/src/api/hooks/useSmartsupp.ts` — add `CloseConversationResponse`, `useCloseConversation`
- `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx` — add Close button
- `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx` — add close button tests

---

## Task 1: Research and Document the Smartsupp Close Endpoint

> **GATE:** This task MUST be completed before Tasks 4–6. The exact HTTP method and URL you find here go directly into `SmartsuppApiClient.CloseConversationAsync`. If the endpoint differs from the assumption in Task 6, update only `HttpMethod.Put`, the URL suffix, and the request body in that task.

**Files:**
- Modify: `docs/integrations/smartsupp-api.md`

- [ ] **Step 1: Look up the close/resolve conversation endpoint**

  Open `https://docs.smartsupp.com/rest-api/conversations` in a browser and find the endpoint that sets conversation status to `resolved`. Record:
  - HTTP method (likely `PUT` or `PATCH`)
  - URL (likely `PUT /conversations/{id}` with `{ "status": "resolved" }`, or `POST /conversations/{id}/close`)
  - Request body shape
  - Success response shape
  - Error codes (404 when conversation missing, 422 for invalid status, etc.)

- [ ] **Step 2: Add a new section to `docs/integrations/smartsupp-api.md`**

  After the existing `### POST /conversations/{id}/messages` section, add:

  ```markdown
  ### PUT /conversations/{id}  ← adjust method/URL based on what you find

  **Close (resolve) a conversation.**

  **Verified:** [today's date]

  **Request:**
  ```http
  PUT https://app.smartsupp.com/api/v2/conversations/{id}
  Authorization: Bearer {token}
  Content-Type: application/json

  { "status": "resolved" }
  ```

  **Success:** `200 OK` — body is the updated conversation object (we ignore it).

  **Error codes observed:**
  - `404` — conversation not found
  - `422` — invalid status value or conversation already closed
  ```

- [ ] **Step 3: Commit the doc update**

  ```bash
  git add docs/integrations/smartsupp-api.md
  git commit -m "docs(smartsupp): document close conversation REST endpoint"
  ```

---

## Task 2: Add Error Code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add `SmartsuppCloseConversationUnavailable` after the last Smartsupp error code**

  Find this block in `ErrorCodes.cs` (currently ends at line ~309):
  ```csharp
      [HttpStatusCode(HttpStatusCode.InternalServerError)]
      SmartsuppAgentMappingNotFound = 2707,
  ```

  Replace with:
  ```csharp
      [HttpStatusCode(HttpStatusCode.InternalServerError)]
      SmartsuppAgentMappingNotFound = 2707,
      [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
      SmartsuppCloseConversationUnavailable = 2708,
  ```

- [ ] **Step 2: Verify it compiles**

  ```bash
  dotnet build backend/Anela.Heblo.sln -p:TreatWarningsAsErrors=false 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

---

## Task 3: Write Failing Handler Tests + Stub Files (RED)

Create stub types and tests that fail because the handler is not implemented yet.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationHandler.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/CloseConversationHandlerTests.cs`

- [ ] **Step 1: Create `CloseConversationRequest.cs`**

  ```csharp
  using MediatR;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

  public class CloseConversationRequest : IRequest<CloseConversationResponse>
  {
      public string ConversationId { get; set; } = null!;
  }
  ```

- [ ] **Step 2: Create `CloseConversationResponse.cs`**

  ```csharp
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

  public class CloseConversationResponse : BaseResponse
  {
      public CloseConversationResponse() { }
      public CloseConversationResponse(ErrorCodes errorCode) : base(errorCode) { }
  }
  ```

- [ ] **Step 3: Create stub `CloseConversationHandler.cs`**

  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;
  using MediatR;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

  public class CloseConversationHandler : IRequestHandler<CloseConversationRequest, CloseConversationResponse>
  {
      private readonly ISmartsuppRepository _repository;
      private readonly ISmartsuppApiClient _apiClient;
      private readonly ILogger<CloseConversationHandler> _logger;

      public CloseConversationHandler(
          ISmartsuppRepository repository,
          ISmartsuppApiClient apiClient,
          ILogger<CloseConversationHandler> logger)
      {
          _repository = repository;
          _apiClient = apiClient;
          _logger = logger;
      }

      public Task<CloseConversationResponse> Handle(
          CloseConversationRequest request,
          CancellationToken cancellationToken)
          => throw new NotImplementedException();
  }
  ```

- [ ] **Step 4: Add `CloseConversationAsync` to `ISmartsuppApiClient`**

  In `ISmartsuppApiClient.cs`, add after `GetAgentsAsync`:
  ```csharp
      Task CloseConversationAsync(string conversationId, CancellationToken cancellationToken);
  ```

  > This means `SmartsuppApiClient` now has a missing interface member — it won't compile until Step 3 in Task 6 adds the implementation. Add a stub in `SmartsuppApiClient.cs` now to keep it compiling:
  
  At the end of the `SmartsuppApiClient` class body (before the closing `}`), add:
  ```csharp
      public Task CloseConversationAsync(string conversationId, CancellationToken cancellationToken)
          => throw new NotImplementedException();
  ```

- [ ] **Step 5: Create `CloseConversationHandlerTests.cs`**

  ```csharp
  using Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.Smartsupp;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Smartsupp;

  public class CloseConversationHandlerTests
  {
      private readonly Mock<ISmartsuppRepository> _repo = new();
      private readonly Mock<ISmartsuppApiClient> _apiClient = new();
      private readonly Mock<ILogger<CloseConversationHandler>> _logger = new();

      private CloseConversationHandler CreateHandler() =>
          new(_repo.Object, _apiClient.Object, _logger.Object);

      private void SetupConversation(bool exists = true) =>
          _repo.Setup(r => r.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(exists
                  ? new SmartsuppConversation { Id = "conv-1", Status = SmartsuppConversationStatus.Open, Messages = [] }
                  : null);

      [Fact]
      public async Task Handle_ReturnsSuccess_WhenApiCloses()
      {
          // Arrange
          SetupConversation();
          _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

          // Act
          var result = await CreateHandler().Handle(
              new CloseConversationRequest { ConversationId = "conv-1" },
              CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          _apiClient.Verify(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()), Times.Once);
      }

      [Fact]
      public async Task Handle_ReturnsConversationNotFound_WhenConversationMissing()
      {
          // Arrange
          SetupConversation(exists: false);

          // Act
          var result = await CreateHandler().Handle(
              new CloseConversationRequest { ConversationId = "conv-1" },
              CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
          _apiClient.Verify(a => a.CloseConversationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      }

      [Fact]
      public async Task Handle_ReturnsUnavailable_WhenApiThrowsHttpRequestException()
      {
          // Arrange
          SetupConversation();
          _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Service unavailable", null,
                  System.Net.HttpStatusCode.ServiceUnavailable));

          // Act
          var result = await CreateHandler().Handle(
              new CloseConversationRequest { ConversationId = "conv-1" },
              CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.SmartsuppCloseConversationUnavailable);
      }

      [Fact]
      public async Task Handle_ReturnsUnavailable_WhenApiThrowsTimeout()
      {
          // Arrange
          SetupConversation();
          _apiClient.Setup(a => a.CloseConversationAsync("conv-1", It.IsAny<CancellationToken>()))
              .ThrowsAsync(new TimeoutException("Request timed out"));

          // Act
          var result = await CreateHandler().Handle(
              new CloseConversationRequest { ConversationId = "conv-1" },
              CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.SmartsuppCloseConversationUnavailable);
      }
  }
  ```

- [ ] **Step 6: Run tests and confirm they fail (RED)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CloseConversationHandlerTests" 2>&1 | tail -15
  ```

  Expected: Tests fail with `NotImplementedException` (from stub handler).

---

## Task 4: Implement `CloseConversationHandler` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationHandler.cs`

- [ ] **Step 1: Replace the stub `Handle` with the real implementation**

  Replace the entire `Handle` method (the `throw new NotImplementedException()` line) with:

  ```csharp
      public async Task<CloseConversationResponse> Handle(
          CloseConversationRequest request,
          CancellationToken cancellationToken)
      {
          var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
          if (conversation is null)
              return new CloseConversationResponse(ErrorCodes.SmartsuppConversationNotFound);

          try
          {
              await _apiClient.CloseConversationAsync(request.ConversationId, cancellationToken);
          }
          catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                         or ObjectDisposedException
                                         || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
          {
              _logger.LogWarning(ex, "Smartsupp API unavailable while closing conversation {ConversationId}",
                  request.ConversationId);
              return new CloseConversationResponse(ErrorCodes.SmartsuppCloseConversationUnavailable);
          }

          return new CloseConversationResponse();
      }
  ```

  Also add the missing `using System.Net.Http;` if not already present — the handler file needs it for `HttpRequestException`. The full file:

  ```csharp
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.Smartsupp;
  using MediatR;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

  public class CloseConversationHandler : IRequestHandler<CloseConversationRequest, CloseConversationResponse>
  {
      private readonly ISmartsuppRepository _repository;
      private readonly ISmartsuppApiClient _apiClient;
      private readonly ILogger<CloseConversationHandler> _logger;

      public CloseConversationHandler(
          ISmartsuppRepository repository,
          ISmartsuppApiClient apiClient,
          ILogger<CloseConversationHandler> logger)
      {
          _repository = repository;
          _apiClient = apiClient;
          _logger = logger;
      }

      public async Task<CloseConversationResponse> Handle(
          CloseConversationRequest request,
          CancellationToken cancellationToken)
      {
          var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
          if (conversation is null)
              return new CloseConversationResponse(ErrorCodes.SmartsuppConversationNotFound);

          try
          {
              await _apiClient.CloseConversationAsync(request.ConversationId, cancellationToken);
          }
          catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                         or ObjectDisposedException
                                         || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
          {
              _logger.LogWarning(ex, "Smartsupp API unavailable while closing conversation {ConversationId}",
                  request.ConversationId);
              return new CloseConversationResponse(ErrorCodes.SmartsuppCloseConversationUnavailable);
          }

          return new CloseConversationResponse();
      }
  }
  ```

- [ ] **Step 2: Run tests and confirm they pass (GREEN)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CloseConversationHandlerTests" 2>&1 | tail -10
  ```

  Expected: `4 passed, 0 failed`.

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/ \
          backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs \
          backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
          backend/test/Anela.Heblo.Tests/Features/Smartsupp/CloseConversationHandlerTests.cs \
          backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs
  git commit -m "feat(smartsupp): add CloseConversation use case with handler tests"
  ```

---

## Task 5: Write Failing API Client Tests (RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`

- [ ] **Step 1: Add close conversation tests to `SmartsuppApiClientTests.cs`**

  Append these two test methods inside the `SmartsuppApiClientTests` class (after the last existing `[Fact]`):

  ```csharp
      [Fact]
      public async Task CloseConversationAsync_SendsPutWithResolvedStatus()
      {
          // Arrange
          string? capturedUrl = null;
          string? capturedMethod = null;
          string? capturedBody = null;
          string? capturedAuth = null;

          var handler = new Mock<HttpMessageHandler>();
          handler.Protected()
              .Setup<Task<HttpResponseMessage>>("SendAsync",
                  ItExpr.IsAny<HttpRequestMessage>(),
                  ItExpr.IsAny<CancellationToken>())
              .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
              {
                  capturedUrl = req.RequestUri?.ToString();
                  capturedMethod = req.Method.Method;
                  capturedBody = await req.Content!.ReadAsStringAsync();
                  capturedAuth = req.Headers.Authorization?.ToString();
              })
              .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent("{}", Encoding.UTF8, "application/json")
              });

          var client = CreateClient(handler.Object);

          // Act
          await client.CloseConversationAsync("conv-abc", CancellationToken.None);

          // Assert
          capturedUrl.Should().Contain("conversations/conv-abc");
          capturedMethod.Should().Be("PUT"); // ← adjust if Task 1 found a different method
          capturedBody.Should().Contain("resolved");
          capturedAuth.Should().StartWith("Bearer test-token");
      }

      [Fact]
      public async Task CloseConversationAsync_ThrowsHttpRequestException_OnNon2xx()
      {
          // Arrange
          var handler = new Mock<HttpMessageHandler>();
          handler.Protected()
              .Setup<Task<HttpResponseMessage>>("SendAsync",
                  ItExpr.IsAny<HttpRequestMessage>(),
                  ItExpr.IsAny<CancellationToken>())
              .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
              {
                  Content = new StringContent("{\"error\":\"unavailable\"}", Encoding.UTF8, "application/json")
              });

          var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

          // Act
          Func<Task> act = () => client.CloseConversationAsync("conv-abc", CancellationToken.None);

          // Assert
          await act.Should().ThrowAsync<HttpRequestException>();
      }
  ```

  > **Note:** If `SmartsuppApiClientTests.cs` does not already import `System.Net` or `System.Text`, add the missing `using` statements at the top of the file.

- [ ] **Step 2: Run tests and confirm they fail (RED)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~SmartsuppApiClientTests.CloseConversation" 2>&1 | tail -10
  ```

  Expected: Tests fail with `NotImplementedException` (the stub in SmartsuppApiClient still throws).

---

## Task 6: Implement `SmartsuppApiClient.CloseConversationAsync` (GREEN)

> **Dependency on Task 1:** The HTTP method, URL suffix, and body come from the findings in Task 1. The implementation below assumes `PUT /conversations/{id}` with `{ "status": "resolved" }`. If your research found a different endpoint, change `HttpMethod.Put` and the URL/body accordingly.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`

- [ ] **Step 1: Replace the stub `CloseConversationAsync` with the real implementation**

  Find the stub at the bottom of `SmartsuppApiClient`:
  ```csharp
      public Task CloseConversationAsync(string conversationId, CancellationToken cancellationToken)
          => throw new NotImplementedException();
  ```

  Replace with:
  ```csharp
      public async Task CloseConversationAsync(string conversationId, CancellationToken cancellationToken)
      {
          if (string.IsNullOrEmpty(_options.ApiToken))
              throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

          var body = new CloseConversationApiRequest { Status = "resolved" };
          var json = JsonSerializer.Serialize(body, JsonOptions);

          await _pipeline.ExecuteAsync(async ct =>
          {
              var client = _httpClientFactory.CreateClient("Smartsupp");
              using var request = new HttpRequestMessage(
                  HttpMethod.Put, // ← change to HttpMethod.Post or HttpMethod.Patch if Task 1 found differently
                  $"{_options.BaseUrl}conversations/{conversationId}"); // ← append /close if Task 1 found POST /close
              request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");
              request.Content = new StringContent(json, Encoding.UTF8, "application/json");

              var response = await client.SendAsync(request, ct);

              if (!response.IsSuccessStatusCode)
              {
                  var errorBody = await response.Content.ReadAsStringAsync(ct);
                  _logger.LogError("Smartsupp close conversation failed {Status}: {Body}",
                      response.StatusCode, errorBody);
                  var ex = new HttpRequestException(
                      $"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                  if (response.Headers.RetryAfter?.Delta is { } delta)
                      ex.Data["RetryAfter"] = delta;
                  throw ex;
              }
          }, cancellationToken);
      }

      private sealed class CloseConversationApiRequest
      {
          public string Status { get; init; } = null!;
      }
  ```

  > Place `CloseConversationApiRequest` as a private nested class at the bottom of the file, just before the outer class closing `}`. The `_pipeline`, `_httpClientFactory`, `_options`, `_logger`, `JsonOptions`, `Encoding`, and `JsonSerializer` are all already available in this class — no new fields needed.

- [ ] **Step 2: Run tests and confirm they pass (GREEN)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~SmartsuppApiClientTests.CloseConversation" 2>&1 | tail -10
  ```

  Expected: `2 passed, 0 failed`.

  > **If `capturedMethod` assertion fails:** The Smartsupp API uses a different HTTP method than PUT. Go back to Task 1, verify the endpoint, and update `HttpMethod.Put` to the correct method in Step 1 above.

- [ ] **Step 3: Run all Smartsupp tests to catch regressions**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Smartsupp" 2>&1 | tail -10
  ```

  Expected: all pass.

- [ ] **Step 4: Commit**

  ```bash
  git add backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs \
          backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs
  git commit -m "feat(smartsupp): implement CloseConversationAsync in SmartsuppApiClient"
  ```

---

## Task 7: Add Controller Endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`

- [ ] **Step 1: Add the using directive for the new use case**

  At the top of `SmartsuppController.cs`, add:
  ```csharp
  using Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;
  ```

- [ ] **Step 2: Add the action method to `SmartsuppController`**

  Append inside the `SmartsuppController` class body, after `SendMessage`:
  ```csharp
      [HttpPost("conversations/{id}/close")]
      [ProducesResponseType(typeof(CloseConversationResponse), StatusCodes.Status200OK)]
      [ProducesResponseType(StatusCodes.Status404NotFound)]
      [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
      public async Task<ActionResult<CloseConversationResponse>> CloseConversation(
          string id,
          CancellationToken cancellationToken = default)
      {
          var result = await _mediator.Send(
              new CloseConversationRequest { ConversationId = id },
              cancellationToken);
          return HandleResponse(result);
      }
  ```

- [ ] **Step 3: Verify backend builds clean**

  ```bash
  dotnet build backend/Anela.Heblo.sln 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 4: Run dotnet format**

  ```bash
  dotnet format backend/Anela.Heblo.sln 2>&1 | tail -5
  ```

- [ ] **Step 5: Commit**

  ```bash
  git add backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs
  git commit -m "feat(smartsupp): add POST conversations/{id}/close endpoint"
  ```

---

## Task 8: Rebuild TypeScript API Client

The new endpoint must appear in the generated TypeScript client before the frontend can use it.

**Files:** (no source changes — build regenerates)

- [ ] **Step 1: Run the frontend build to regenerate the OpenAPI client**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && npm run build 2>&1 | tail -15
  ```

  Expected: build succeeds. The file `frontend/src/api/generated/api-client.ts` is updated.

  > If the build fails due to an unrelated issue, do not proceed — fix the build first.

---

## Task 9: Write Failing Frontend Hook Test + Implement Hook (TDD)

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useCloseConversation.test.ts`
- Modify: `frontend/src/api/hooks/useSmartsupp.ts`

### 9A — Write the failing test (RED)

- [ ] **Step 1: Create `useCloseConversation.test.ts`**

  ```typescript
  import React from "react";
  import { renderHook, act, waitFor } from "@testing-library/react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { useCloseConversation } from "../useSmartsupp";

  const mockFetch = jest.fn();

  jest.mock("../../client", () => ({
    getAuthenticatedApiClient: () => ({
      baseUrl: "http://localhost:5001",
      http: { fetch: mockFetch },
    }),
  }));

  function wrapper({ children }: { children: React.ReactNode }) {
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    return React.createElement(QueryClientProvider, { client: qc }, children);
  }

  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("useCloseConversation", () => {
    it("calls POST to the close endpoint with the conversation id", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ success: true }),
      });

      const { result } = renderHook(() => useCloseConversation(), { wrapper });

      act(() => {
        result.current.mutate("conv-1");
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5001/api/smartsupp/conversations/conv-1/close",
        expect.objectContaining({ method: "POST" }),
      );
    });

    it("sets error message when API returns non-ok with SmartsuppCloseConversationUnavailable", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        json: async () => ({ errorCode: "SmartsuppCloseConversationUnavailable" }),
      });

      const { result } = renderHook(() => useCloseConversation(), { wrapper });

      act(() => {
        result.current.mutate("conv-1");
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error?.message).toContain("nedostupná");
    });

    it("sets generic error message when API returns non-ok with no error code", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        json: async () => ({}),
      });

      const { result } = renderHook(() => useCloseConversation(), { wrapper });

      act(() => {
        result.current.mutate("conv-2");
      });

      await waitFor(() => expect(result.current.isError).toBe(true));

      expect(result.current.error?.message).toBeTruthy();
    });
  });
  ```

- [ ] **Step 2: Run test and confirm it fails (RED)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && \
    npx jest src/api/hooks/__tests__/useCloseConversation.test.ts --no-coverage 2>&1 | tail -15
  ```

  Expected: Fails with `useCloseConversation is not a function` or similar — the export doesn't exist yet.

### 9B — Implement the hook (GREEN)

- [ ] **Step 3: Update imports in `useSmartsupp.ts`**

  Replace the current import line:
  ```typescript
  import { useQuery } from "@tanstack/react-query";
  import { getClientAndBaseUrl, apiGet } from "../smartsuppClient";
  ```

  With:
  ```typescript
  import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
  import { getClientAndBaseUrl, apiGet, apiPost } from "../smartsuppClient";
  ```

- [ ] **Step 4: Add `CloseConversationResponse` interface and `useCloseConversation` hook at the end of `useSmartsupp.ts`**

  Append after the last exported function (`useSmartsuppVisitorInfo`):

  ```typescript
  export interface CloseConversationResponse {
    success: boolean;
    errorCode?: string;
  }

  const CLOSE_ERROR_MESSAGES: Record<string, string> = {
    SmartsuppCloseConversationUnavailable:
      "Nepodařilo se uzavřít konverzaci — služba je nedostupná. Zkuste to prosím znovu.",
    SmartsuppConversationNotFound: "Konverzace nebyla nalezena.",
  };

  function messageForCloseError(code?: string): string {
    if (code && CLOSE_ERROR_MESSAGES[code]) return CLOSE_ERROR_MESSAGES[code];
    return "Nepodařilo se uzavřít konverzaci.";
  }

  export function useCloseConversation() {
    const queryClient = useQueryClient();
    return useMutation<CloseConversationResponse, Error, string>({
      mutationFn: async (conversationId: string) => {
        const { apiClient, baseUrl } = getClientAndBaseUrl();
        const response = await apiPost(
          apiClient,
          `${baseUrl}/api/smartsupp/conversations/${conversationId}/close`,
          {},
        );

        if (!response.ok) {
          const errData = await response.json().catch(() => ({})) as Partial<CloseConversationResponse>;
          throw new Error(messageForCloseError(errData?.errorCode));
        }

        const data = (await response.json()) as CloseConversationResponse;
        if (!data.success) {
          throw new Error(messageForCloseError(data?.errorCode));
        }

        return data;
      },
      onSuccess: (_data, conversationId) => {
        queryClient.invalidateQueries({
          queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        });
        queryClient.invalidateQueries({
          queryKey: ["smartsupp", "conversations"],
        });
      },
    });
  }
  ```

- [ ] **Step 5: Run test and confirm it passes (GREEN)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && \
    npx jest src/api/hooks/__tests__/useCloseConversation.test.ts --no-coverage 2>&1 | tail -10
  ```

  Expected: `3 passed, 0 failed`.

- [ ] **Step 6: Commit**

  ```bash
  git add frontend/src/api/hooks/useSmartsupp.ts \
          frontend/src/api/hooks/__tests__/useCloseConversation.test.ts
  git commit -m "feat(smartsupp): add useCloseConversation mutation hook"
  ```

---

## Task 10: Add Close Button to ConversationDetail (TDD)

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`

### 10A — Write failing tests (RED)

- [ ] **Step 1: Update the mock in `ConversationDetail.test.tsx` to include `useCloseConversation`**

  Replace the existing `jest.mock("../../../../api/hooks/useSmartsupp", ...)` block with:

  ```typescript
  const mockMutate = jest.fn();

  jest.mock("../../../../api/hooks/useSmartsupp", () => {
    const actual = jest.requireActual("../../../../api/hooks/useSmartsupp");
    return {
      ...actual,
      useSmartsuppConversation: () => ({
        data: {
          success: true,
          conversation: null,
          messages: [
            {
              id: "m1",
              authorType: "visitor",
              authorName: "Jana",
              content: "Dotaz",
              createdAt: new Date().toISOString(),
              isFirstReply: false,
            },
          ],
        },
        isLoading: false,
      }),
      useCloseConversation: () => ({
        mutate: mockMutate,
        isPending: false,
      }),
    };
  });
  ```

  Also add a mock for `react-hot-toast` (so toast calls don't throw in tests). Add after the import block and before any `jest.mock`:

  ```typescript
  jest.mock("react-hot-toast", () => ({
    toast: {
      success: jest.fn(),
      error: jest.fn(),
    },
  }));
  ```

- [ ] **Step 2: Add new test cases at the end of `describe("ConversationDetail", ...)`**

  ```typescript
    it("renders a close button when conversation status is 'open'", () => {
      render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
      expect(screen.getByTestId("close-conversation-btn")).toBeInTheDocument();
    });

    it("does not render a close button when conversation status is not 'open'", () => {
      const resolvedConv = { ...conv, status: "resolved" };
      render(wrap(<ConversationDetail conversationId="c1" conversation={resolvedConv} />));
      expect(screen.queryByTestId("close-conversation-btn")).not.toBeInTheDocument();
    });

    it("calls mutate with conversationId when the close button is clicked", () => {
      render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
      fireEvent.click(screen.getByTestId("close-conversation-btn"));
      expect(mockMutate).toHaveBeenCalledWith(
        "c1",
        expect.objectContaining({ onSuccess: expect.any(Function), onError: expect.any(Function) }),
      );
    });
  ```

- [ ] **Step 3: Run tests and confirm the new ones fail (RED)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && \
    npx jest src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx --no-coverage 2>&1 | tail -15
  ```

  Expected: new tests fail with "Unable to find element by: `[data-testid="close-conversation-btn"]`".

### 10B — Implement the Close button (GREEN)

- [ ] **Step 4: Update imports in `ConversationDetail.tsx`**

  Replace the existing import line:
  ```typescript
  import { ArrowLeft, Info } from "lucide-react";
  ```
  With:
  ```typescript
  import { ArrowLeft, Info, Loader2 } from "lucide-react";
  ```

  Replace:
  ```typescript
  import { ConversationDto, MessageDto, useSmartsuppConversation } from "../../../api/hooks/useSmartsupp";
  ```
  With:
  ```typescript
  import { ConversationDto, MessageDto, useSmartsuppConversation, useCloseConversation } from "../../../api/hooks/useSmartsupp";
  ```

  Add after the lucide import:
  ```typescript
  import { toast } from "react-hot-toast";
  ```

- [ ] **Step 5: Wire up the mutation inside `ConversationDetail`**

  Inside the `ConversationDetail` component body, just after `const { data, isLoading } = useSmartsuppConversation(conversationId);`, add:

  ```typescript
    const { mutate: closeConversation, isPending: isClosing } = useCloseConversation();

    const handleClose = () => {
      closeConversation(conversationId, {
        onSuccess: () => toast.success("Konverzace byla uzavřena"),
        onError: (err) => toast.error(err.message),
      });
    };
  ```

- [ ] **Step 6: Add the button to the header JSX**

  Find the header's `ml-auto` div block:
  ```tsx
        <div className="ml-auto flex items-center gap-2">
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {onOpenContactDetails && (
  ```

  Replace with:
  ```tsx
        <div className="ml-auto flex items-center gap-2">
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {conversation.status === 'open' && (
            <button
              type="button"
              data-testid="close-conversation-btn"
              onClick={handleClose}
              disabled={isClosing}
              aria-label="Uzavřít konverzaci"
              className="inline-flex items-center gap-1.5 rounded-md border border-gray-300 px-2.5 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {isClosing && <Loader2 className="h-4 w-4 animate-spin" />}
              Uzavřít konverzaci
            </button>
          )}
          {onOpenContactDetails && (
  ```

- [ ] **Step 7: Run tests and confirm all pass (GREEN)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && \
    npx jest src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx --no-coverage 2>&1 | tail -10
  ```

  Expected: all tests pass (existing + 3 new).

- [ ] **Step 8: Commit**

  ```bash
  git add frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx \
          frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx
  git commit -m "feat(smartsupp): add Uzavřít konverzaci button to ConversationDetail header"
  ```

---

## Task 11: Frontend Build Gate

- [ ] **Step 1: Run build**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && npm run build 2>&1 | tail -15
  ```

  Expected: `✓ built in` (no TypeScript errors).

- [ ] **Step 2: Run lint**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && npm run lint 2>&1 | tail -10
  ```

  Expected: no errors.

- [ ] **Step 3: Run all Smartsupp-related tests**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/hyderabad-v1/frontend && \
    npx jest --testPathPattern="smartsupp|Smartsupp|useClose" --no-coverage 2>&1 | tail -15
  ```

  Expected: all pass.

---

## Task 12: Backend Final Build Gate

- [ ] **Step 1: Run build**

  ```bash
  dotnet build backend/Anela.Heblo.sln 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 2: Run format**

  ```bash
  dotnet format backend/Anela.Heblo.sln 2>&1 | tail -5
  ```

- [ ] **Step 3: Run all Smartsupp backend tests**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Smartsupp" 2>&1 | tail -10
  ```

  Expected: all pass.

- [ ] **Step 4: Commit any format changes**

  ```bash
  git status
  # If dotnet format changed anything:
  git add -u
  git commit -m "chore: apply dotnet format after smartsupp close conversation feature"
  ```

---

## Self-Review

**Spec coverage check:**
- [x] Button in detail header only — Task 10 adds it to `ConversationDetail.tsx` header
- [x] Always `resolved` — Task 6 hardcodes `"status": "resolved"`
- [x] One-click, no confirmation — single `onClick` handler in Task 10
- [x] Toast feedback — `toast.success`/`toast.error` in `handleClose` in Task 10
- [x] Button visible only when `status === 'open'` — conditional render in Task 10 Step 6
- [x] Webhook round-trip unchanged — no changes to existing webhook handlers
- [x] API endpoint documented first — Task 1 is the gate
- [x] DTOs are classes, not records — `CloseConversationRequest`, `CloseConversationResponse` both use `class`
- [x] Error code for API failure — `SmartsuppCloseConversationUnavailable = 2708` in Task 2
- [x] Cache invalidated on success — `queryClient.invalidateQueries` in Task 9

**Placeholder scan:** None — all steps have concrete code.

**Type consistency check:**
- `useCloseConversation()` returns `{ mutate, isPending }` — referenced correctly in `ConversationDetail.tsx`
- `mutate(conversationId, { onSuccess, onError })` — called in `handleClose` with `conversationId` (string)
- `CloseConversationRequest.ConversationId` (string) — set from route `id` in controller
- `ISmartsuppApiClient.CloseConversationAsync(string, CancellationToken)` — same signature in interface, client, handler, and tests
