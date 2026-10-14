using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class MarketingAdsProposalsControllerTests
{
    private static MarketingAdsProposalsController Build(Mock<IMediator> mediator)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new MarketingAdsProposalsController(mediator.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }

    [Fact]
    public async Task RevertProposal_PassesTheBadGatewayBodyThroughWithValidateOnlyCounts()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RevertAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevertAdProposalResponse(ErrorCodes.MarketingAdsRevertFailed)
            {
                ProposalId = 7,
                RevertedActions = 1,
                FailedActions = 2,
                ValidateOnlyActions = 2
            });

        // Act
        var result = await Build(mediator).RevertProposal(7);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        var body = objectResult.Value.Should().BeOfType<RevertAdProposalResponse>().Subject;
        body.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        body.ValidateOnlyActions.Should().Be(2);
        body.FailedActions.Should().Be(2);
    }

    [Fact]
    public async Task ReviseProposal_ReturnsConflictWithBodyOnVersionMismatch_AndBindsRouteId()
    {
        // Arrange
        ReviseAdProposalRequest? sent = null;
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ReviseAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ReviseAdProposalResponse>, CancellationToken>((r, _) => sent = (ReviseAdProposalRequest)r)
            .ReturnsAsync(new ReviseAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange));

        // Act
        var result = await Build(mediator).ReviseProposal(5, new ReviseAdProposalRequest { Id = 99, ExpectedVersion = 1 });

        // Assert
        sent!.Id.Should().Be(5);
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        objectResult.Value.Should().BeOfType<ReviseAdProposalResponse>();
    }
}
