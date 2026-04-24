using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingCalendar;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

public class GetMarketingCalendarHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repositoryMock;
    private readonly GetMarketingCalendarHandler _handler;

    public GetMarketingCalendarHandlerTests()
    {
        _repositoryMock = new Mock<IMarketingActionRepository>();
        _handler = new GetMarketingCalendarHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenActionsExist_ShouldReturnMappedCalendarDtos()
    {
        // Arrange
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var actions = new List<MarketingAction>
        {
            new()
            {
                Id = 1,
                Title = "June Launch",
                ActionType = MarketingActionType.Event,
                StartDate = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
                ProductAssociations = new List<MarketingActionProduct>
                {
                    new() { ProductCodePrefix = "TON001" },
                },
            },
        };

        _repositoryMock
            .Setup(x => x.GetForCalendarAsync(start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actions);

        var request = new GetMarketingCalendarRequest { StartDate = start, EndDate = end };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Actions.Should().HaveCount(1);
        result.Actions[0].Id.Should().Be(1);
        result.Actions[0].Title.Should().Be("June Launch");
        result.Actions[0].ActionType.Should().Be("Event");
        result.Actions[0].AssociatedProducts.Should().ContainSingle("TON001");
    }

    [Fact]
    public async Task Handle_WhenNoActionsExist_ShouldReturnEmptyList()
    {
        // Arrange
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        _repositoryMock
            .Setup(x => x.GetForCalendarAsync(start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());

        var request = new GetMarketingCalendarRequest { StartDate = start, EndDate = end };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Actions.Should().BeEmpty();
    }
}
