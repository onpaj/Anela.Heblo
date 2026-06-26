using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.Marketing;

public class GetMarketingActionsHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly GetMarketingActionsHandler _handler;

    public GetMarketingActionsHandlerTests()
    {
        _repository
            .Setup(x => x.GetPagedAsync(It.IsAny<MarketingActionQueryCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<MarketingAction>
            {
                Items = new List<MarketingAction>(),
                TotalCount = 0,
                PageNumber = 1,
                PageSize = 20,
            });

        _handler = new GetMarketingActionsHandler(_repository.Object);
    }

    [Fact]
    public async Task Handle_WhenActionTypeProvided_PropagatesItToCriteria()
    {
        // Arrange
        var request = new GetMarketingActionsRequest { ActionType = MarketingActionType.Blog };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repository.Verify(
            r => r.GetPagedAsync(
                It.Is<MarketingActionQueryCriteria>(c => c.ActionType == MarketingActionType.Blog),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenActionTypeOmitted_PassesNullActionTypeOnCriteria()
    {
        // Arrange
        var request = new GetMarketingActionsRequest();

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repository.Verify(
            r => r.GetPagedAsync(
                It.Is<MarketingActionQueryCriteria>(c => c.ActionType == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
