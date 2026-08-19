### task: setup-test-file

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs`

- [ ] **Step 1: Create the test file skeleton**

Create the file with the class scaffold, matching the constructor shape confirmed in the architecture review (3 dependencies only — no `IMapper`, no `TimeProvider`):

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.DeleteManufactureDifficulty;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class DeleteManufactureDifficultyHandlerTests
{
    private readonly Mock<IManufactureDifficultyRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILogger<DeleteManufactureDifficultyHandler>> _loggerMock;
    private readonly DeleteManufactureDifficultyHandler _handler;

    public DeleteManufactureDifficultyHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureDifficultyRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _loggerMock = new Mock<ILogger<DeleteManufactureDifficultyHandler>>();

        _handler = new DeleteManufactureDifficultyHandler(
            _repositoryMock.Object,
            _catalogRepositoryMock.Object,
            _loggerMock.Object);
    }
}
```

- [ ] **Step 2: Build to confirm the skeleton compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Build succeeded.` (0 errors) — an empty test class with no `[Fact]` methods is valid.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): scaffold DeleteManufactureDifficultyHandlerTests"
```

---
