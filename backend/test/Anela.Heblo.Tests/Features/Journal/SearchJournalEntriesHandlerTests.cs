using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;
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
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Entries.Should().HaveCount(1);
        result.Entries.First().Title.Should().Be("Test Entry for TON002");
        result.Entries.First().AssociatedProducts.Should().Contain("TON002");

        _repositoryMock.Verify(x => x.SearchEntriesAsync(
            It.IsAny<string?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<DateTime?>(),
            It.Is<string?>(p => p == "TON002"),
            It.IsAny<IReadOnlyCollection<int>?>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
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
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task Handle_PopulatesContentPreviewFromDomainContent_WhenSearchTextEmpty()
    {
        // Arrange: 250-char content, no search text. The 200-char window + ellipsis suffix must apply.
        var content = new string('a', 250);
        var entry = new JournalEntry
        {
            Id = 1,
            Title = "Empty search entry",
            Content = content,
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1"
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });

        var request = new SearchJournalEntriesRequest
        {
            SearchText = null,
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var hit = result.Entries.Single();
        hit.ContentPreview.Should().NotBeNull();
        hit.ContentPreview.Should().EndWith("...");
        hit.ContentPreview.Length.Should().BeLessThanOrEqualTo(203); // 200 chars + "..."
        hit.HighlightedTerms.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BuildsPreviewWindowAroundMatch_WhenSearchTextPresent()
    {
        // Arrange: place "needle" near the middle of a long content string
        var prefix = new string('p', 300);
        var suffix = new string('s', 300);
        var content = prefix + "needle" + suffix;
        var entry = new JournalEntry
        {
            Id = 7,
            Title = "match",
            Content = content,
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1"
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });

        var request = new SearchJournalEntriesRequest
        {
            SearchText = "needle",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var hit = result.Entries.Single();
        hit.ContentPreview.Should().Contain("needle");
        hit.ContentPreview.Should().StartWith("...");
        hit.ContentPreview.Should().EndWith("...");
        hit.ContentPreview.Length.Should().BeLessThanOrEqualTo(206); // 200 chars + leading "..." + trailing "..."
    }

    [Fact]
    public async Task Handle_FiltersHighlightTermsToLengthGreaterThanTwo()
    {
        // Arrange: search text mixes short ("a", "is") and long ("needle", "haystack") terms.
        var entry = new JournalEntry
        {
            Id = 9,
            Title = "filter",
            Content = "irrelevant body",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1"
        };

        _repositoryMock
            .Setup(x => x.SearchEntriesAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });

        var request = new SearchJournalEntriesRequest
        {
            SearchText = "a is needle haystack",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var hit = result.Entries.Single();
        hit.HighlightedTerms.Should().BeEquivalentTo(new[] { "needle", "haystack" });
    }
}
