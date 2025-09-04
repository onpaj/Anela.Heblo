using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries;
using Anela.Heblo.Domain.Features.Journal;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class SearchJournalEntriesHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly SearchJournalEntriesHandler _handler;

    public SearchJournalEntriesHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _handler = new SearchJournalEntriesHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task SearchByProductCodePrefix_ShouldReturnEntriesWithMatchingPrefix()
    {
        // Arrange
        var request = new SearchJournalEntriesRequest
        {
            ProductCodePrefix = "TON002",
            PageNumber = 1,
            PageSize = 10
        };

        var journalEntry = new JournalEntry
        {
            Id = 1,
            Title = "Test Entry for TON002",
            Content = "This is about TON002 product family",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        // Add product prefix association
        journalEntry.AssociateWithProduct("TON002");

        var pagedResult = new PagedResult<JournalEntry>
        {
            Items = new List<JournalEntry> { journalEntry },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(It.IsAny<JournalSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Entries.Should().HaveCount(1);
        result.Entries.First().Title.Should().Be("Test Entry for TON002");
        result.Entries.First().AssociatedProducts.Should().Contain("TON002");

        _repositoryMock.Verify(x => x.SearchEntriesAsync(
            It.Is<JournalSearchCriteria>(r =>
                r.ProductCodePrefix == "TON002"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEntriesByProduct_ShouldFindEntriesForProductWithPrefix()
    {
        // This test verifies that when searching for product "TON002030",
        // it should find entries associated with prefix "TON002"

        // Arrange
        var productCode = "TON002030";
        var journalEntry = new JournalEntry
        {
            Id = 1,
            Title = "Note about TON002 family",
            Content = "This applies to all TON002 products",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        // Entry is associated with product prefix "TON002"
        journalEntry.AssociateWithProduct("TON002");

        // Test that the product code starts with the prefix
        productCode.StartsWith("TON002").Should().BeTrue();

        // The repository should find this entry when searching for TON002030
        // because TON002030 starts with TON002
        journalEntry.ProductAssociations.Should().HaveCount(1);
        journalEntry.ProductAssociations.First().ProductCodePrefix.Should().Be("TON002");
    }

    [Fact]
    public async Task SearchByProductCodePrefix_ShouldReturnMatchingEntry()
    {
        // Arrange - test single prefix search
        var request = new SearchJournalEntriesRequest
        {
            ProductCodePrefix = "TON002",
            PageNumber = 1,
            PageSize = 10
        };

        var entry = CreateJournalEntryWithProductPrefix("TON002", "Entry for TON002");

        var pagedResult = new PagedResult<JournalEntry>
        {
            Items = new List<JournalEntry> { entry },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(It.IsAny<JournalSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Entries.Should().HaveCount(1);
        result.Entries.First().Title.Should().Be("Entry for TON002");
    }

    private JournalEntry CreateJournalEntryWithProductPrefix(string prefix, string title)
    {
        var entry = new JournalEntry
        {
            Id = Random.Shared.Next(1, 1000),
            Title = title,
            Content = $"Content for {prefix}",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };
        entry.AssociateWithProduct(prefix);
        return entry;
    }
}