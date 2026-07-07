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
