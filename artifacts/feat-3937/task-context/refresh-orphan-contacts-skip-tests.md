### task: refresh-orphan-contacts-skip-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`
- Test: same file (this task creates it)

- [ ] **Step 1: Write the failing tests (class scaffold + FR-1 + FR-2)**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RefreshOrphanContactsHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppContactEnricher> _enricher = new();
    private readonly Mock<ILogger<RefreshOrphanContactsHandler>> _logger = new();

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"orphan_{Guid.NewGuid()}").Options);

    private RefreshOrphanContactsHandler CreateHandler(ApplicationDbContext db) =>
        new(_repo.Object, _apiClient.Object, _enricher.Object, db, _logger.Object);

    private void SetupIds(params string[] ids) =>
        _repo.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.ToList());

    private static SmartsuppConversation MakeLocalConversation(string id) => new()
    {
        Id = id,
        Status = SmartsuppConversationStatus.Open,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SyncedAt = DateTime.UtcNow,
        Messages = new(),
    };

    [Fact]
    public async Task Handle_IncrementsSkippedNoContactId_WhenRemoteContactIdIsNull()
    {
        // Arrange
        SetupIds("conv-1");
        _apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = null });
        using var db = CreateContext();

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(1);
        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        response.Failed.Should().Be(0);
        _enricher.Verify(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound()
    {
        // Arrange
        SetupIds("conv-1");
        _apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-1" });
        using var db = CreateContext(); // no local row seeded for "conv-1"

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        response.Failed.Should().Be(0);
        _enricher.Verify(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: FAIL to compile/run — the file did not previously exist, so this establishes the new tests execute against the real (already-implemented) handler. Since `RefreshOrphanContactsHandler` already has this behavior, expect these two tests to PASS immediately once the file compiles — there is no production code to write for this task. If either test fails against the real handler, STOP: this means the handler does not actually behave as documented in the brief/spec, and that discrepancy must be reported rather than papered over (see spec NFR-1).

- [ ] **Step 3: Confirm both tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: PASS (2 passed, 0 failed)

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs
git commit -m "test(smartsupp): cover RefreshOrphanContactsHandler skip-no-contact-id branches"
```

---

