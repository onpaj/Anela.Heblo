using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.GetJournalEntries;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class GetJournalEntriesHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly GetJournalEntriesHandler _handler;

    public GetJournalEntriesHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _handler = new GetJournalEntriesHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_MiddlePage_ReturnsCorrectPaginationMetadata()
    {
        // Arrange
        var request = new GetJournalEntriesRequest
        {
            PageNumber = 2,
            PageSize = 10
        };

        var entry = new JournalEntry
        {
            Id = 1,
            Title = "Entry",
            Content = "Content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user123"
        };

        _repositoryMock
            .Setup(x => x.GetEntriesAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry> { entry },
                TotalCount = 25,
                PageNumber = 2,
                PageSize = 10
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_FirstPageExactMultiple_HasNoNextOrPreviousPage()
    {
        // Arrange: TotalCount is an exact multiple of PageSize and this is the only/last page.
        var request = new GetJournalEntriesRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        _repositoryMock
            .Setup(x => x.GetEntriesAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<JournalEntry>
            {
                Items = new List<JournalEntry>(),
                TotalCount = 10,
                PageNumber = 1,
                PageSize = 10
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalPages.Should().Be(1);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }
}
