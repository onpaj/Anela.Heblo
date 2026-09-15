# Design: GetLeafletGenerationHandler not-found path unit test

## Component Design

### `GetLeafletGenerationHandlerTests` (new test class)
- **Responsibility**: Verify `GetLeafletGenerationHandler.Handle` returns the correct not-found error response when `ILeafletGenerationRepository.GetGenerationByIdAsync` yields `null`.
- **Location**: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs`
- **Namespace**: `Anela.Heblo.Tests.Features.Leaflet.UseCases`
- **Dependencies (mocked)**: `Mock<ILeafletGenerationRepository>` — only `GetGenerationByIdAsync(Guid, CancellationToken)` is set up; no other repository members are exercised by this test.
- **Structure**: single `[Fact]` method, following the sibling `GetLeafletChunkDetailHandlerTests` shape:
  - a `private readonly Mock<ILeafletGenerationRepository> _repoMock = new();` field
  - a `private GetLeafletGenerationHandler CreateHandler() => new(_repoMock.Object);` factory
  - Arrange / Act / Assert comments inside the test body

### `GetLeafletGenerationHandler` (existing, unchanged)
- No design changes. This test only exercises its existing not-found branch (`generation is null → return new GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound)`).

## Data Schemas

No schema changes. Shapes relevant to the test (all pre-existing):

**Request** (`GetLeafletGenerationRequest`):
```csharp
public class GetLeafletGenerationRequest : IRequest<GetLeafletGenerationResponse>
{
    public Guid Id { get; set; }
}
```

**Response** (`GetLeafletGenerationResponse : BaseResponse`) — expected shape on the not-found path under test:
```
Success        = false
ErrorCode      = ErrorCodes.LeafletFeedbackNotFound
Params         = null
Id             = Guid.Empty
Topic          = string.Empty
Audience       = string.Empty
Length         = string.Empty
FinalMarkdown  = string.Empty
KbSourceCount  = 0
LeafletSourceCount = 0
DurationMs     = 0
CreatedAt      = default(DateTimeOffset)
UserId         = null
PrecisionScore = null
StyleScore     = null
FeedbackComment = null
```

**Mocked repository call**:
```csharp
Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken)
```
Setup: `_repoMock.Setup(r => r.GetGenerationByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((LeafletGeneration?)null);`
