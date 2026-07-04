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
