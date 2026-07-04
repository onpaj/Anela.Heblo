# Implementation Plan: `GenerateLeafletHandler` error-signaling consistency fix

**Goal:** Stop `GenerateLeafletHandler` from throwing `EmptyRetrievalException` for the expected "no KB/leaflet hits" business outcome. Make it return a normal `GenerateLeafletResponse` with `ErrorCode = ErrorCodes.LeafletEmptyRetrieval` instead, let `LeafletController.Generate` delegate to the existing `HandleResponse` helper (dropping its try/catch), update the one MCP tool consumer and the one frontend consumer that special-cased the old exception/`ProblemDetails` shape, and delete the now-dead `EmptyRetrievalException` type. The external HTTP contract keeps `422 Unprocessable Entity` for this case; only the response *body* shape changes.

**Architecture:** Clean Architecture / Vertical Slice monorepo (.NET 8 + React). MediatR request/handler per use case; every handler returns a `BaseResponse`-derived DTO (`Success`, `ErrorCode`, `Params`); `BaseApiController.HandleResponse<T>` reflects on the `[HttpStatusCode]` attribute of the response's `ErrorCode` to pick the HTTP status. This fix makes `GenerateLeafletHandler`/`LeafletController.Generate` conform to that single existing idiom (already used by every other Leaflet handler/action) instead of using a bespoke throw/catch path.

**Tech Stack:** C# / .NET 8 (MediatR, ASP.NET Core, xUnit, Moq, FluentAssertions), TypeScript / React (Jest, React Testing Library, NSwag-generated OpenAPI client).

---

### task: add-error-code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:276-283`

This is a pure enum-member addition — no branching logic, so there is no meaningful unit test to write for it in isolation (its effect is exercised by the tests added in later tasks, once the handler/controller/MCP tool actually use it). Skipping a dedicated test here is deliberate, not an oversight.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. Find the `// Leaflet module errors (25XX)` block:
  ```csharp
      // Leaflet module errors (25XX)
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletChunkNotFound = 2501,
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletFeedbackNotFound = 2502,
      [HttpStatusCode(HttpStatusCode.Conflict)]
      LeafletFeedbackAlreadySubmitted = 2503,

      // Photobank errors (26XX)
  ```
- [ ] Step 2: Add a new member immediately after `LeafletFeedbackAlreadySubmitted = 2503,`, before the `// Photobank errors (26XX)` comment:
  ```csharp
      // Leaflet module errors (25XX)
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletChunkNotFound = 2501,
      [HttpStatusCode(HttpStatusCode.NotFound)]
      LeafletFeedbackNotFound = 2502,
      [HttpStatusCode(HttpStatusCode.Conflict)]
      LeafletFeedbackAlreadySubmitted = 2503,
      [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
      LeafletEmptyRetrieval = 2504,

      // Photobank errors (26XX)
  ```
  Do not renumber or reorder any existing members — their numeric values are the wire contract for the generated TypeScript enum.
- [ ] Step 3: Build the backend to confirm the enum still compiles:
  ```bash
  dotnet build Anela.Heblo.sln
  ```
- [ ] Step 4: Commit.
  ```bash
  git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
  git commit -m "#3478: add ErrorCodes.LeafletEmptyRetrieval (422) for Leaflet empty-retrieval case"
  ```

---

### task: response-error-constructor

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`

Give `GenerateLeafletResponse` the same two-constructor shape `SubmitLeafletFeedbackResponse` already has, so the handler can construct an error response in the next task. Adding constructors to a DTO with no behavior of its own isn't independently unit-testable in a meaningful way (there is no logic branch to assert on beyond "the base class sets these properties," which is already covered by existing `BaseResponse` behavior) — this task is verified by the handler test in the next task, which is the first real caller of the new constructor.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`. Current content:
  ```csharp
  using System.Text.Json.Serialization;
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

  public class GenerateLeafletResponse : BaseResponse
  {
      [JsonPropertyName("content")]
      public string Content { get; set; } = string.Empty;

      [JsonPropertyName("id")]
      public Guid? Id { get; set; }

      [JsonPropertyName("kbSourceCount")]
      public int KbSourceCount { get; set; }

      [JsonPropertyName("leafletSourceCount")]
      public int LeafletSourceCount { get; set; }
  }
  ```
- [ ] Step 2: Add the two constructors (mirroring `SubmitLeafletFeedbackResponse` in `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackRequest.cs:23-26`):
  ```csharp
  using System.Text.Json.Serialization;
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

  public class GenerateLeafletResponse : BaseResponse
  {
      public GenerateLeafletResponse() { }

      public GenerateLeafletResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
          : base(errorCode, details) { }

      [JsonPropertyName("content")]
      public string Content { get; set; } = string.Empty;

      [JsonPropertyName("id")]
      public Guid? Id { get; set; }

      [JsonPropertyName("kbSourceCount")]
      public int KbSourceCount { get; set; }

      [JsonPropertyName("leafletSourceCount")]
      public int LeafletSourceCount { get; set; }
  }
  ```
- [ ] Step 3: Build the backend — confirm the existing success-path usage (`new GenerateLeafletResponse { Content = ..., KbSourceCount = ..., LeafletSourceCount = ... }` in `GenerateLeafletHandler.cs:134-139`) still compiles unchanged:
  ```bash
  dotnet build Anela.Heblo.sln
  ```
- [ ] Step 4: Run the full backend test suite to confirm nothing broke:
  ```bash
  dotnet test Anela.Heblo.sln
  ```
- [ ] Step 5: Commit.
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs
  git commit -m "#3478: add error-constructing constructor to GenerateLeafletResponse"
  ```

---

### task: handler-returns-response

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:1-8,63-67`
- Test: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs:1-11,84-103`

Replace the `throw new EmptyRetrievalException(...)` in `GenerateLeafletHandler.Handle` with an early `return` of the new error DTO from the previous two tasks. `EmptyRetrievalException` itself is **not deleted yet** (its other consumers — `LeafletController` and `LeafletTools` — are updated in later tasks); it simply stops being referenced by the handler.

- [ ] Step 1 (failing test): Open `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`. Add the missing using directive at the top (needed for `ErrorCodes`):
  ```csharp
  using Anela.Heblo.Application.Features.Leaflet;
  using Anela.Heblo.Application.Features.Leaflet.Contracts;
  using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Domain.Features.Leaflet;
  using FluentAssertions;
  using Microsoft.Extensions.AI;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;
  using Moq;
  using Xunit;
  ```
  (only the `using Anela.Heblo.Application.Shared;` line is new — insert it alphabetically where shown).
- [ ] Step 2: Replace the test `Handle_dual_empty_retrieval_throws_EmptyRetrievalException` (currently at lines 84-103):
  ```csharp
      [Fact]
      public async Task Handle_dual_empty_retrieval_throws_EmptyRetrievalException()
      {
          // Arrange
          SetupEmbeddings();

          _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<KnowledgeSearchResult>());
          _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

          var handler = CreateHandler();
          var request = new GenerateLeafletRequest { Topic = "retinol", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

          // Act
          var act = () => handler.Handle(request, CancellationToken.None);

          // Assert
          await act.Should().ThrowAsync<EmptyRetrievalException>();
      }
  ```
  with:
  ```csharp
      [Fact]
      public async Task Handle_dual_empty_retrieval_returns_LeafletEmptyRetrieval_error()
      {
          // Arrange
          SetupEmbeddings();

          _kb.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<KnowledgeSearchResult>());
          _leaflets.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

          var handler = CreateHandler();
          var request = new GenerateLeafletRequest { Topic = "retinol", Audience = AudienceType.EndConsumer, Length = LeafletLength.Short };

          // Act
          var response = await handler.Handle(request, CancellationToken.None);

          // Assert
          response.Success.Should().BeFalse();
          response.ErrorCode.Should().Be(ErrorCodes.LeafletEmptyRetrieval);
          response.Params.Should().NotBeNull();
          response.Params!.Should().ContainKey("detail");
          response.Params!["detail"].Should().Contain("Knowledge Base does not yet cover this topic");
      }
  ```
- [ ] Step 3: Run just this test file to confirm it fails against the current handler (still throws):
  ```bash
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GenerateLeafletHandlerTests.Handle_dual_empty_retrieval_returns_LeafletEmptyRetrieval_error"
  ```
  Expect a failure — the handler still throws `EmptyRetrievalException`, so `await handler.Handle(...)` throws instead of returning.
- [ ] Step 4 (implementation): Open `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`. Add the missing using directive at the top:
  ```csharp
  using Anela.Heblo.Application.Features.Leaflet.Contracts;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Application.Shared.Http;
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Domain.Features.Leaflet;
  using MediatR;
  using Microsoft.Extensions.AI;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;
  ```
  (only `using Anela.Heblo.Application.Shared;` is new).
- [ ] Step 5: Replace lines 63-67:
  ```csharp
          if (kbHits.Count == 0 && leafletHits.Count == 0)
          {
              throw new EmptyRetrievalException(
                  "Knowledge Base does not yet cover this topic; try a broader phrasing");
          }
  ```
  with:
  ```csharp
          if (kbHits.Count == 0 && leafletHits.Count == 0)
          {
              // Params["detail"] is for API-consumer/log diagnostics only, not for direct end-user display.
              return new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval,
                  new() { { "detail", "Knowledge Base does not yet cover this topic; try a broader phrasing" } });
          }
  ```
  No other line in the handler changes.
- [ ] Step 6: Run the test again to confirm it now passes:
  ```bash
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GenerateLeafletHandlerTests.Handle_dual_empty_retrieval_returns_LeafletEmptyRetrieval_error"
  ```
- [ ] Step 7: Run the whole `GenerateLeafletHandlerTests` class (the other tests exercise the non-empty paths and must still pass unchanged):
  ```bash
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GenerateLeafletHandlerTests"
  ```
- [ ] Step 8: Commit.
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs
  git commit -m "#3478: GenerateLeafletHandler returns LeafletEmptyRetrieval error instead of throwing"
  ```

---

### task: controller-drops-trycatch

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs:31-67`
- Test: `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs:42-155`

Make `LeafletController.Generate` match every other action in the file: no try/catch, `return HandleResponse(result);`, return type `Task<ActionResult<GenerateLeafletResponse>>`. `EmptyRetrievalException` is still not deleted (the MCP tool still references it until the next task), but after this task nothing in `LeafletController.cs` or `LeafletControllerTests.cs` references it any more.

- [ ] Step 1 (failing tests): Open `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs`. Replace the existing `Generate_returns_200_with_response_on_success` test (lines 42-72) — the assertion must change from `Assert.IsType<OkObjectResult>(result)` to `Assert.IsType<OkObjectResult>(result.Result)` because `Generate`'s return type is about to change from `Task<IActionResult>` to `Task<ActionResult<GenerateLeafletResponse>>`:
  ```csharp
      [Fact]
      public async Task Generate_returns_200_with_response_on_success()
      {
          // Arrange
          var request = new GenerateLeafletRequest
          {
              Topic = "Vitamin C serum",
              Audience = AudienceType.EndConsumer,
              Length = LeafletLength.Short,
          };

          var expectedResponse = new GenerateLeafletResponse
          {
              Success = true,
              Content = "Vitamin C serum is great for your skin.",
          };

          _mediatorMock
              .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
              .ReturnsAsync(expectedResponse);

          var controller = CreateController();

          // Act
          var result = await controller.Generate(request, CancellationToken.None);

          // Assert
          var okResult = Assert.IsType<OkObjectResult>(result.Result);
          var response = Assert.IsType<GenerateLeafletResponse>(okResult.Value);
          Assert.Equal(expectedResponse.Content, response.Content);
      }
  ```
- [ ] Step 2: Replace `Generate_returns_422_on_EmptyRetrievalException` (lines 74-102) with a test asserting the response-based 422 path:
  ```csharp
      [Fact]
      public async Task Generate_returns_422_on_LeafletEmptyRetrieval_error()
      {
          // Arrange
          var request = new GenerateLeafletRequest
          {
              Topic = "Unknown topic",
              Audience = AudienceType.EndConsumer,
              Length = LeafletLength.Short,
          };

          var errorResponse = new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval,
              new() { { "detail", "Knowledge Base does not yet cover this topic; try a broader phrasing" } });

          _mediatorMock
              .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
              .ReturnsAsync(errorResponse);

          var controller = CreateController();

          // Act
          var result = await controller.Generate(request, CancellationToken.None);

          // Assert
          var objectResult = Assert.IsType<ObjectResult>(result.Result);
          Assert.Equal(422, objectResult.StatusCode);

          var response = Assert.IsType<GenerateLeafletResponse>(objectResult.Value);
          Assert.False(response.Success);
          Assert.Equal(ErrorCodes.LeafletEmptyRetrieval, response.ErrorCode);
      }
  ```
- [ ] Step 3: Replace `Generate_returns_502_on_unexpected_exception` (lines 104-133) with a test asserting the exception now propagates unhandled (mirroring `Generate_propagates_OperationCanceledException` immediately below it):
  ```csharp
      [Fact]
      public async Task Generate_propagates_unexpected_exception()
      {
          // Arrange
          var request = new GenerateLeafletRequest
          {
              Topic = "Retinol cream",
              Audience = AudienceType.EndConsumer,
              Length = LeafletLength.Short,
          };

          _mediatorMock
              .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Internal system failure with stack trace details"));

          var controller = CreateController();

          // Act & Assert
          await Assert.ThrowsAsync<InvalidOperationException>(
              () => controller.Generate(request, CancellationToken.None));
      }
  ```
- [ ] Step 4: Leave `Generate_propagates_OperationCanceledException` (lines 135-155) exactly as-is — its shape (`await Assert.ThrowsAsync<OperationCanceledException>(() => controller.Generate(...))`) already matches the post-fix behavior.
- [ ] Step 5: Run the `LeafletControllerTests` class to confirm the three edited tests fail against the current controller (it still has the try/catch and the `Task<IActionResult>` signature, so `result.Result` won't even compile yet — that's expected, it proves the test now demands the new signature):
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect a **compile error** on `result.Result` (current `Generate` returns `Task<IActionResult>`, which has no `.Result` property) — this is the "red" state proving the test drives the next step.
- [ ] Step 6 (implementation): Open `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`. Replace the `Generate` method and its attributes (lines 31-67):
  ```csharp
      [HttpPost("generate")]
      [FeatureAuthorize(Feature.Marketing_Leaflet, AccessLevel.Write)]
      [ProducesResponseType(typeof(GenerateLeafletResponse), 200)]
      [ProducesResponseType(typeof(ProblemDetails), 400)]
      [ProducesResponseType(typeof(ProblemDetails), 422)]
      [ProducesResponseType(typeof(ProblemDetails), 502)]
      public async Task<IActionResult> Generate([FromBody] GenerateLeafletRequest request, CancellationToken ct)
      {
          try
          {
              var response = await _mediator.Send(request, ct);
              return Ok(response);
          }
          catch (EmptyRetrievalException ex)
          {
              return UnprocessableEntity(new ProblemDetails
              {
                  Status = 422,
                  Title = "Insufficient knowledge",
                  Detail = ex.Message,
              });
          }
          catch (OperationCanceledException)
          {
              throw;
          }
          catch (Exception ex)
          {
              Logger.LogError(ex, "Leaflet generation failed");
              return StatusCode(502, new ProblemDetails
              {
                  Status = 502,
                  Title = "Generation failed",
                  Detail = "Leaflet generation failed. Please try again.",
              });
          }
      }
  ```
  with:
  ```csharp
      [HttpPost("generate")]
      [FeatureAuthorize(Feature.Marketing_Leaflet, AccessLevel.Write)]
      [ProducesResponseType(typeof(GenerateLeafletResponse), 200)]
      [ProducesResponseType(typeof(ProblemDetails), 400)]
      [ProducesResponseType(typeof(GenerateLeafletResponse), 422)]
      public async Task<ActionResult<GenerateLeafletResponse>> Generate([FromBody] GenerateLeafletRequest request, CancellationToken ct)
      {
          var result = await _mediator.Send(request, ct);
          return HandleResponse(result);
      }
  ```
  Note what changed: the try/catch is gone; the return type changed from `Task<IActionResult>` to `Task<ActionResult<GenerateLeafletResponse>>` (matching `HandleResponse<T>`'s generic constraint and every sibling action); the `422` attribute now points at `GenerateLeafletResponse` instead of `ProblemDetails`; the `502` attribute is removed entirely (no code path produces it any more — an unexpected exception now propagates to ASP.NET's global `AddProblemDetails()` pipeline, which is already registered in `ServiceCollectionExtensions.AddCrossCuttingServices` and needs no changes here).
- [ ] Step 7: Build, then run the `LeafletControllerTests` class:
  ```bash
  dotnet build Anela.Heblo.sln
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletControllerTests"
  ```
  All tests in the class should now pass, including the untouched `Generate_propagates_OperationCanceledException`.
- [ ] Step 8: Commit.
  ```bash
  git add backend/src/Anela.Heblo.API/Controllers/LeafletController.cs backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs
  git commit -m "#3478: LeafletController.Generate delegates to HandleResponse, drops try/catch"
  ```

---

### task: mcp-tool-inspects-response

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs:1-61`
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs:1-16,62-78`

`LeafletTools.GenerateLeaflet` currently catches `EmptyRetrievalException` and rethrows it as `McpException`. Since the handler no longer throws that exception (previous task), this catch is dead code, and the tool would otherwise silently return a `success: false` JSON payload instead of surfacing an MCP-level error. Replace the catch with a post-`Send` check on `response.Success`.

- [ ] Step 1 (failing test): Open `backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs`. Add the missing using directive:
  ```csharp
  using System.Text.Json;
  using Anela.Heblo.API.MCP.Tools;
  using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
  using Anela.Heblo.Application.Shared;
  using MediatR;
  using Microsoft.Extensions.Logging;
  using ModelContextProtocol;
  using Moq;
  using Xunit;
  ```
  (only `using Anela.Heblo.Application.Shared;` is new).
- [ ] Step 2: Replace `GenerateLeaflet_wraps_EmptyRetrievalException_as_McpException` (lines 62-78):
  ```csharp
      [Fact]
      public async Task GenerateLeaflet_wraps_EmptyRetrievalException_as_McpException()
      {
          // Arrange
          const string emptyRetrievalMessage = "No relevant documents were found for the given topic.";

          _mediator
              .Setup(m => m.Send(It.IsAny<GenerateLeafletRequest>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new EmptyRetrievalException(emptyRetrievalMessage));

          // Act
          var exception = await Assert.ThrowsAsync<McpException>(() =>
              CreateTools().GenerateLeaflet("Some topic", "B2B", "Medium"));

          // Assert
          Assert.Equal(emptyRetrievalMessage, exception.Message);
      }
  ```
  with:
  ```csharp
      [Fact]
      public async Task GenerateLeaflet_throws_McpException_on_LeafletEmptyRetrieval_response()
      {
          // Arrange
          var errorResponse = new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval,
              new() { { "detail", "Knowledge Base does not yet cover this topic; try a broader phrasing" } });

          _mediator
              .Setup(m => m.Send(It.IsAny<GenerateLeafletRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(errorResponse);

          // Act
          var exception = await Assert.ThrowsAsync<McpException>(() =>
              CreateTools().GenerateLeaflet("Some topic", "B2B", "Medium"));

          // Assert
          Assert.Equal("Knowledge Base does not yet cover this topic; try a broader phrasing", exception.Message);
      }
  ```
- [ ] Step 3: Run this test to confirm it fails against the current tool (it currently checks a thrown `EmptyRetrievalException`, so a returned `GenerateLeafletResponse` with `Success == false` falls through to `return JsonSerializer.Serialize(response);` instead of throwing):
  ```bash
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GenerateLeaflet_throws_McpException_on_LeafletEmptyRetrieval_response"
  ```
  Expect failure (no `McpException` is thrown).
- [ ] Step 4 (implementation): Open `backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs`. Add the missing using directive:
  ```csharp
  using System.ComponentModel;
  using System.Text.Json;
  using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
  using Anela.Heblo.Application.Shared;
  using MediatR;
  using Microsoft.Extensions.Logging;
  using ModelContextProtocol;
  using ModelContextProtocol.Server;
  ```
  (only `using Anela.Heblo.Application.Shared;` is new).
- [ ] Step 5: Replace the body of `GenerateLeaflet` (lines 25-61):
  ```csharp
      public async Task<string> GenerateLeaflet(
          [Description("Leaflet topic (1-200 characters), e.g. 'Bisabolol pro citlivou pleť'")] string topic,
          [Description("Audience: 'EndConsumer' or 'B2B'")] string audience,
          [Description("Length: 'Short', 'Medium', or 'Long'")] string length,
          CancellationToken ct = default)
      {
          try
          {
              if (!Enum.TryParse<AudienceType>(audience, ignoreCase: true, out var audienceEnum))
                  throw new McpException($"Invalid audience '{audience}'");

              if (!Enum.TryParse<LeafletLength>(length, ignoreCase: true, out var lengthEnum))
                  throw new McpException($"Invalid length '{length}'");

              var response = await _mediator.Send(new GenerateLeafletRequest
              {
                  Topic = topic,
                  Audience = audienceEnum,
                  Length = lengthEnum
              }, ct);

              return JsonSerializer.Serialize(response);
          }
          catch (McpException)
          {
              throw;
          }
          catch (EmptyRetrievalException ex)
          {
              throw new McpException(ex.Message);
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "MCP GenerateLeaflet failed");
              throw new McpException("Leaflet generation failed. Please try again.");
          }
      }
  ```
  with:
  ```csharp
      public async Task<string> GenerateLeaflet(
          [Description("Leaflet topic (1-200 characters), e.g. 'Bisabolol pro citlivou pleť'")] string topic,
          [Description("Audience: 'EndConsumer' or 'B2B'")] string audience,
          [Description("Length: 'Short', 'Medium', or 'Long'")] string length,
          CancellationToken ct = default)
      {
          try
          {
              if (!Enum.TryParse<AudienceType>(audience, ignoreCase: true, out var audienceEnum))
                  throw new McpException($"Invalid audience '{audience}'");

              if (!Enum.TryParse<LeafletLength>(length, ignoreCase: true, out var lengthEnum))
                  throw new McpException($"Invalid length '{length}'");

              var response = await _mediator.Send(new GenerateLeafletRequest
              {
                  Topic = topic,
                  Audience = audienceEnum,
                  Length = lengthEnum
              }, ct);

              if (!response.Success)
              {
                  var message = response.ErrorCode == ErrorCodes.LeafletEmptyRetrieval
                      ? "Knowledge Base does not yet cover this topic; try a broader phrasing"
                      : "Leaflet generation failed. Please try again.";
                  throw new McpException(message);
              }

              return JsonSerializer.Serialize(response);
          }
          catch (McpException)
          {
              throw;
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "MCP GenerateLeaflet failed");
              throw new McpException("Leaflet generation failed. Please try again.");
          }
      }
  ```
  The outer `catch (McpException)` rethrow and the generic `catch (Exception)` block are kept — they handle genuinely unexpected failures (e.g. embedding/chat client exceptions), a legitimate MCP-protocol boundary translation, not the "business logic in caller" problem this fix targets.
- [ ] Step 6: Run the full `LeafletToolsTests` class (the success test and the two invalid-enum tests, plus the unrelated `GenerateLeaflet_wraps_unexpected_exception_with_generic_message` test, must all still pass unchanged):
  ```bash
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletToolsTests"
  ```
- [ ] Step 7: Commit.
  ```bash
  git add backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs
  git commit -m "#3478: LeafletTools.GenerateLeaflet inspects response.Success instead of catching EmptyRetrievalException"
  ```

---

### task: remove-exception-and-tests

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs`

After the three previous tasks, `EmptyRetrievalException` has zero remaining consumers in source or test code. Confirm that, then delete it.

- [ ] Step 1: Search the whole repository for any remaining reference:
  ```bash
  grep -rn "EmptyRetrievalException" backend/ --include="*.cs"
  ```
  Expect exactly one match: the type's own definition in `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs`. If any other match appears, stop and fix it before continuing (it means an earlier task's edit was incomplete).
- [ ] Step 2: Delete the file:
  ```bash
  git rm backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs
  ```
- [ ] Step 3: Build the backend to confirm nothing else referenced it:
  ```bash
  dotnet build Anela.Heblo.sln
  ```
- [ ] Step 4: Run the full backend test suite:
  ```bash
  dotnet test Anela.Heblo.sln
  ```
- [ ] Step 5: Re-run the grep to confirm zero remaining references anywhere in the repo (source and tests):
  ```bash
  grep -rn "EmptyRetrievalException" backend/ --include="*.cs"
  ```
  Expect no output.
- [ ] Step 6: Commit.
  ```bash
  git commit -m "#3478: delete dead EmptyRetrievalException type"
  ```

---

### task: frontend-error-branch-fix

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`
- Test: `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` (new file)

Changing the `422` `[ProducesResponseType]` from `ProblemDetails` to `GenerateLeafletResponse` (previous controller task) changes what the generated TypeScript client parses and throws on a 422 response. Regenerate the client first so the exact generated shape is known, then fix the one component that special-cased the old shape, verified with a new test file (none existed for this component before).

- [ ] Step 1: Regenerate the OpenAPI TypeScript client from the now-updated backend (requires the backend to build successfully, which it does after the previous tasks):
  ```bash
  dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
  ```
- [ ] Step 2: Confirm the regenerated `frontend/src/api/generated/api-client.ts` now has `LeafletEmptyRetrieval` in the `ErrorCodes` TS enum, and that `processLeaflet_Generate`'s `422` branch now parses the body as `GenerateLeafletResponse.fromJS(...)` instead of `ProblemDetails.fromJS(...)`:
  ```bash
  grep -n "LeafletEmptyRetrieval" frontend/src/api/generated/api-client.ts
  grep -n -A6 'status === 422' frontend/src/api/generated/api-client.ts | grep -A6 "processLeaflet_Generate" -m1
  ```
  If the `422` branch still says `ProblemDetails.fromJS`, stop — the backend attribute change (`controller-drops-trycatch` task) or the regeneration step didn't take effect; re-run step 1 after confirming `dotnet build backend/Anela.Heblo.API` succeeds.
- [ ] Step 3 (failing test): Create `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`:
  ```tsx
  import React from 'react';
  import { render, screen, fireEvent } from '@testing-library/react';
  import '@testing-library/jest-dom';
  import LeafletGenerateTab from '../LeafletGenerateTab';
  import { getAuthenticatedApiClient } from '../../../api/client';
  import { ErrorCodes, GenerateLeafletResponse } from '../../../api/generated/api-client';

  jest.mock('../../../api/client', () => ({
    getAuthenticatedApiClient: jest.fn(),
  }));

  const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.Mock;

  const fillTopicAndSubmit = () => {
    fireEvent.change(screen.getByLabelText('Téma'), { target: { value: 'Bisabolol' } });
    fireEvent.click(screen.getByRole('button', { name: 'Vygenerovat leták' }));
  };

  describe('LeafletGenerateTab', () => {
    let mockGenerate: jest.Mock;

    beforeEach(() => {
      jest.clearAllMocks();
      mockGenerate = jest.fn();
      mockGetAuthenticatedApiClient.mockReturnValue({ leaflet_Generate: mockGenerate });
    });

    it('shows the amber insufficient-knowledge banner when the server returns LeafletEmptyRetrieval', async () => {
      const errorResponse = new GenerateLeafletResponse();
      errorResponse.success = false;
      errorResponse.errorCode = ErrorCodes.LeafletEmptyRetrieval;
      mockGenerate.mockRejectedValue(errorResponse);

      render(<LeafletGenerateTab />);
      fillTopicAndSubmit();

      const banner = await screen.findByRole('alert');
      expect(banner).toHaveTextContent(
        'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.'
      );
      expect(banner.className).toContain('bg-amber-100');
    });

    it('shows the red transient banner for any other thrown error', async () => {
      mockGenerate.mockRejectedValue(new Error('network error'));

      render(<LeafletGenerateTab />);
      fillTopicAndSubmit();

      const banner = await screen.findByRole('alert');
      expect(banner).toHaveTextContent('Generování selhalo. Zkuste to prosím znovu.');
      expect(banner.className).toContain('bg-red-100');
    });
  });
  ```
- [ ] Step 4: Run the new test file to confirm it fails against the current component (it still checks `isApiError(err) && err.status === 422`, and the mocked rejected `GenerateLeafletResponse` instance has no `.status` field, so both tests would currently show the red "transient" banner — the first test's assertion on `bg-amber-100` will fail):
  ```bash
  cd frontend && npx react-scripts test src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx --watchAll=false
  ```
  Expect the first test (`shows the amber insufficient-knowledge banner...`) to fail.
- [ ] Step 5 (implementation): Open `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`. Current content:
  ```tsx
  import React, { useState } from 'react';
  import LeafletForm from './LeafletForm';
  import LeafletResult from './LeafletResult';
  import { getAuthenticatedApiClient } from '../../api/client';
  import { AudienceType, GenerateLeafletRequest, LeafletLength } from '../../api/generated/api-client';

  interface ErrorBanner {
    kind: 'insufficient' | 'transient';
    message: string;
  }

  interface ApiError {
    status: number;
    detail?: string;
  }

  function isApiError(err: unknown): err is ApiError {
    return typeof err === 'object' && err !== null && typeof (err as Record<string, unknown>)['status'] === 'number';
  }

  const LeafletGenerateTab: React.FC = () => {
    const [topic, setTopic] = useState('');
    const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
    const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
    const [result, setResult] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [generationId, setGenerationId] = useState<string | null>(null);
    const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);

    const generate = async () => {
      setIsLoading(true);
      setGenerationId(null);
      setErrorBanner(null);
      try {
        const client = getAuthenticatedApiClient();
        const response = await client.leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }));
        setResult(response.content ?? '');
        setGenerationId((response as any).id ?? null);
      } catch (err: unknown) {
        if (isApiError(err) && err.status === 422) {
          setErrorBanner({
            kind: 'insufficient',
            message:
              err.detail ??
              'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
          });
        } else {
          setErrorBanner({
            kind: 'transient',
            message: 'Generování selhalo. Zkuste to prosím znovu.',
          });
        }
      } finally {
        setIsLoading(false);
      }
    };
  ```
  Replace it with:
  ```tsx
  import React, { useState } from 'react';
  import LeafletForm from './LeafletForm';
  import LeafletResult from './LeafletResult';
  import { getAuthenticatedApiClient } from '../../api/client';
  import {
    AudienceType,
    ErrorCodes,
    GenerateLeafletRequest,
    GenerateLeafletResponse,
    LeafletLength,
  } from '../../api/generated/api-client';

  interface ErrorBanner {
    kind: 'insufficient' | 'transient';
    message: string;
  }

  const LeafletGenerateTab: React.FC = () => {
    const [topic, setTopic] = useState('');
    const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
    const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
    const [result, setResult] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [generationId, setGenerationId] = useState<string | null>(null);
    const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);

    const generate = async () => {
      setIsLoading(true);
      setGenerationId(null);
      setErrorBanner(null);
      try {
        const client = getAuthenticatedApiClient();
        const response = await client.leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }));
        setResult(response.content ?? '');
        setGenerationId((response as any).id ?? null);
      } catch (err: unknown) {
        if (err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval) {
          setErrorBanner({
            kind: 'insufficient',
            message: 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
          });
        } else {
          setErrorBanner({
            kind: 'transient',
            message: 'Generování selhalo. Zkuste to prosím znovu.',
          });
        }
      } finally {
        setIsLoading(false);
      }
    };
  ```
  The rest of the file (the JSX in the `return (...)` block) is unchanged. The now-unused `ApiError` interface and `isApiError` function are removed — nothing else in the file used them.
- [ ] Step 6: Run the new test file again to confirm both tests now pass:
  ```bash
  cd frontend && npx react-scripts test src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx --watchAll=false
  ```
- [ ] Step 7: Run the full frontend test suite to confirm no regression elsewhere (in particular `LeafletGeneratorPage.test.tsx`, which mocks `LeafletGenerateTab` wholesale and is unaffected):
  ```bash
  cd frontend && npm test -- --watchAll=false
  ```
- [ ] Step 8: Run the frontend build and lint (required by this repo's validation gate):
  ```bash
  cd frontend && npm run build && npm run lint
  ```
- [ ] Step 9: Commit (include the regenerated `api-client.ts`, since the frontend build depends on it being in sync with the backend contract):
  ```bash
  git add frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx frontend/src/api/generated/api-client.ts
  git commit -m "#3478: LeafletGenerateTab detects LeafletEmptyRetrieval via errorCode instead of HTTP status"
  ```

---

## Final verification (after all tasks)

- [ ] `dotnet build Anela.Heblo.sln` and `dotnet format Anela.Heblo.sln` — both clean.
- [ ] `dotnet test Anela.Heblo.sln` — full backend suite green.
- [ ] `cd frontend && npm run build && npm run lint` — both clean.
- [ ] `cd frontend && npm test -- --watchAll=false` — full frontend suite green.
- [ ] `grep -rn "EmptyRetrievalException" backend/` returns no matches anywhere in the repository.
- [ ] Manual sanity check of the acceptance criteria in `artifacts/feat-3478/spec.r1.md`: FR-1 (enum member + TS enum), FR-2 (response ctor), FR-3 (handler returns, doesn't throw), FR-4 (controller has no try/catch, signature matches), FR-5 (MCP tool inspects `response.Success`), FR-6 (exception type deleted), FR-7 (frontend banner logic), FR-8 (all four listed test files updated, no test deleted without a replacement) are each satisfied by the task(s) above.
