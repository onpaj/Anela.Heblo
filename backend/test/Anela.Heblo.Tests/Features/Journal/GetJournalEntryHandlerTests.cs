using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.GetJournalEntry;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class GetJournalEntryHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly GetJournalEntryHandler _handler;

    public GetJournalEntryHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _handler = new GetJournalEntryHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenEntryExists_ShouldReturnSuccessResponseWithEntry()
    {
        // Arrange
        var entryId = 1;
        var request = new GetJournalEntryRequest { Id = entryId };

        var journalEntry = new JournalEntry
        {
            Id = entryId,
            Title = "Test Title",
            Content = "Test content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        // Add some associations for testing
        journalEntry.AssociateWithProduct("TON001");
        journalEntry.AssignTag(1);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journalEntry);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Entry.Should().NotBeNull();
        result.Entry.Id.Should().Be(entryId);
        result.Entry.Title.Should().Be("Test Title");
        result.Entry.Content.Should().Be("Test content");
        result.Entry.CreatedByUserId.Should().Be("user123");

        // Verify repository call
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEntryDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var entryId = 999;
        var request = new GetJournalEntryRequest { Id = entryId };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntry)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.JournalEntryNotFound);
        result.Entry.Should().BeNull();
        result.Params.Should().ContainKey("entryId");
        result.Params["entryId"].Should().Be(entryId.ToString());

        // Verify repository call
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEntryWithTagsAndProducts_ShouldMapAllProperties()
    {
        // Arrange
        var entryId = 1;
        var request = new GetJournalEntryRequest { Id = entryId };

        var tag1 = new JournalEntryTag { Id = 1, Name = "Tag1", Color = "#FF0000" };
        var tag2 = new JournalEntryTag { Id = 2, Name = "Tag2", Color = "#00FF00" };

        var journalEntry = new JournalEntry
        {
            Id = entryId,
            Title = "Complex Entry",
            Content = "Content with multiple associations",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow.AddHours(1),
            CreatedByUserId = "user123",
            ModifiedByUserId = "user456"
        };

        // Add multiple product associations
        journalEntry.AssociateWithProduct("TON001");
        journalEntry.AssociateWithProduct("TON002");
        journalEntry.AssociateWithProduct("ABC001");

        // Add tag assignments
        journalEntry.AssignTag(tag1.Id);
        journalEntry.AssignTag(tag2.Id);

        // Mock the tag data (normally this would come from the repository with includes)
        journalEntry.TagAssignments.Clear();
        journalEntry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = tag1.Id, Tag = tag1 });
        journalEntry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = tag2.Id, Tag = tag2 });

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journalEntry);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Entry.Should().NotBeNull();

        // Check product associations
        result.Entry.AssociatedProducts.Should().HaveCount(3);
        result.Entry.AssociatedProducts.Should().Contain(new[] { "TON001", "TON002", "ABC001" });

        // Check tag assignments
        result.Entry.Tags.Should().HaveCount(2);
        result.Entry.Tags.Should().Contain(t => t.Id == 1 && t.Name == "Tag1" && t.Color == "#FF0000");
        result.Entry.Tags.Should().Contain(t => t.Id == 2 && t.Name == "Tag2" && t.Color == "#00FF00");

        // Check other properties
        result.Entry.ModifiedByUserId.Should().Be("user456");
        result.Entry.ModifiedAt.Should().BeAfter(result.Entry.CreatedAt);
    }
}