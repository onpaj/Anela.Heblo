using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class MarketingAdsActivityControllerTests
{
    private static MarketingAdsActivityController Build(Mock<IMediator> mediator)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new MarketingAdsActivityController(mediator.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }

    [Fact]
    public async Task ListChangeEvents_ForwardsTheQueryFiltersToTheHandler()
    {
        // Arrange
        var request = new GetAdChangeHistoryRequest
        {
            Origin = AdChangeOrigin.OutOfBand,
            AccountExternalId = "acc-1",
            PageNumber = 2,
            PageSize = 25
        };
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetAdChangeHistoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdChangeHistoryResponse { TotalCount = 42, PageNumber = 2, PageSize = 25 });

        // Act
        var result = await Build(mediator).ListChangeEvents(request);

        // Assert
        mediator.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<GetAdChangeHistoryResponse>().Subject;
        body.TotalCount.Should().Be(42);
        body.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ListChangeEvents_ReturnsTheErrorBodyWhenTheAdsDatabaseIsNotConfigured()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetAdChangeHistoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdChangeHistoryResponse(ErrorCodes.ConfigurationError));

        // Act
        var result = await Build(mediator).ListChangeEvents(new GetAdChangeHistoryRequest());

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        objectResult.Value.Should().BeOfType<GetAdChangeHistoryResponse>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ConfigurationError);
    }

    [Theory]
    [InlineData(ErrorCodes.InvalidValue)]
    [InlineData(ErrorCodes.InvalidDateRange)]
    public async Task ListChangeEvents_ReturnsBadRequestWithTheTypedBodyForAnInvalidFilter(ErrorCodes errorCode)
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetAdChangeHistoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdChangeHistoryResponse(errorCode));

        // Act
        var result = await Build(mediator).ListChangeEvents(new GetAdChangeHistoryRequest());

        // Assert
        var objectResult = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        objectResult.Value.Should().BeOfType<GetAdChangeHistoryResponse>()
            .Which.ErrorCode.Should().Be(errorCode);
    }
}
