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

    [Fact]
    public async Task Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows()
    {
        // Arrange
        SetupIds("conv-fail");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // reset tracking noise from seeding, isolate the handler's own effect

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(0);
        // Without ChangeTracker.Clear(), the entity mutated by `local.ContactId = remote.ContactId`
        // just before the throw would still be tracked as Modified. An empty tracker here proves
        // the handler's catch block actually called _db.ChangeTracker.Clear().
        db.ChangeTracker.Entries().Should().BeEmpty();
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows()
    {
        // Arrange
        SetupIds("conv-fail");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upsert boom"));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(0);
        db.ChangeTracker.Entries().Should().BeEmpty();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesToNextItem_AfterAFailure()
    {
        // Arrange
        SetupIds("conv-fail", "conv-ok");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-ok"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _apiClient.Setup(a => a.GetConversationAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-ok", ContactId = "contact-2" });

        _enricher.Setup(e => e.EnrichContactAsync(
                It.Is<SmartsuppConversation>(c => c.Id == "conv-fail"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));
        _enricher.Setup(e => e.EnrichContactAsync(
                It.Is<SmartsuppConversation>(c => c.Id == "conv-ok"), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(2);
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(1); // conv-ok was still processed despite conv-fail's exception
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "conv-ok"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IncrementsUpdated_WhenItemProcessedSuccessfully()
    {
        // Arrange
        SetupIds("conv-ok");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-ok"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-ok", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(1);
        response.Updated.Should().Be(1);
        response.SkippedNoContactId.Should().Be(0);
        response.Failed.Should().Be(0);
        response.FailedIds.Should().BeEmpty();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "conv-ok" && c.ContactId == "contact-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
