using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppControllerSendWithAttachmentsTests
{
    [Fact]
    public async Task SendMessageWithAttachments_TreatsNullFilesAsEmptyList()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        SendMessageRequest? captured = null;
        mediator.Setup(m => m.Send(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<SendMessageResponse>, CancellationToken>((r, _) => captured = (SendMessageRequest)r)
            .ReturnsAsync(new SendMessageResponse { MessageId = "m1" });
        var controller = new SmartsuppController(mediator.Object);

        // Act
        await controller.SendMessageWithAttachments("conv1", "Text", null, null, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Files.Should().BeEmpty();
        captured.Content.Should().Be("Text");
    }
}
