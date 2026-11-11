using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private static SmartsuppController CreateController(IMediator mediator)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new SmartsuppController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
    }

    private static IFormFile FormFile(string name, int length) =>
        new FormFile(new MemoryStream(new byte[length]), 0, length, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    [Fact]
    public async Task SendMessageWithAttachments_CopiesFilesAndStripsPathFromFileName()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        SendMessageRequest? captured = null;
        mediator.Setup(m => m.Send(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<SendMessageResponse>, CancellationToken>((r, _) => captured = (SendMessageRequest)r)
            .ReturnsAsync(new SendMessageResponse { MessageId = "m1" });
        var controller = new SmartsuppController(mediator.Object);

        // Act
        await controller.SendMessageWithAttachments(
            "conv1", null, null, [FormFile("/tmp/x/a.pdf", 3)], CancellationToken.None);

        // Assert
        captured!.Files.Should().ContainSingle();
        captured.Files[0].FileName.Should().Be("a.pdf");
        captured.Files[0].Content.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendMessageWithAttachments_RejectsTooManyFilesWithoutCallingMediator()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object);
        var files = Enumerable.Range(0, SmartsuppAttachmentPolicy.MaxFiles + 1)
            .Select(i => FormFile($"{i}.pdf", 1)).ToList();

        // Act
        var result = await controller.SendMessageWithAttachments(
            "conv1", null, null, files, CancellationToken.None);

        // Assert
        mediator.Verify(m => m.Send(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<SendMessageResponse>()
            .Which.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentInvalid);
    }

    [Fact]
    public async Task SendMessageWithAttachments_RejectsOversizedFileWithoutCallingMediator()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object);
        var oversized = FormFile("big.pdf", (int)SmartsuppAttachmentPolicy.MaxFileBytes + 1);

        // Act
        var result = await controller.SendMessageWithAttachments(
            "conv1", null, null, [oversized], CancellationToken.None);

        // Assert
        result.Result.Should().BeAssignableTo<ObjectResult>()
            .Which.Value.Should().BeOfType<SendMessageResponse>()
            .Which.ErrorCode.Should().Be(ErrorCodes.SmartsuppAttachmentTooLarge);
        mediator.Verify(m => m.Send(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
