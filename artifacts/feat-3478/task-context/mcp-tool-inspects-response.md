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
