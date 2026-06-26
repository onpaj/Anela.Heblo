using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingCalendar;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Domain.Marketing;
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

        var action = new MarketingActionTestBuilder()
            .WithId(1)
            .WithTitle("June Launch")
            .WithActionType(MarketingActionType.Event)
            .WithStartDate(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc))
            .WithEndDate(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc))
            .Build();

        action.ProductAssociations.Add(new MarketingActionProduct { ProductCodePrefix = "TON001" });

        var actions = new List<MarketingAction> { action };

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
