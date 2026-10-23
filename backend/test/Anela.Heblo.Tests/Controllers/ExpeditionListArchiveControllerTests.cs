using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class ExpeditionListArchiveControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly ExpeditionListArchiveController _controller;

    public ExpeditionListArchiveControllerTests()
    {
        _controller = new ExpeditionListArchiveController(_mediatorMock.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private void SetupDownload(DownloadExpeditionListResponse response) =>
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<DownloadExpeditionListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    [Fact]
    public async Task Download_BlobNotFound_ReturnsNotFound()
    {
        SetupDownload(DownloadExpeditionListResponse.NotFound());

        var result = await _controller.Download("2026-01-01/list.pdf");

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeOfType<DownloadExpeditionListResponse>()
            .Which.ErrorCode.Should().Be(ErrorCodes.BlobNotFound);
    }

    [Fact]
    public async Task Download_InvalidBlobPath_ReturnsBadRequest()
    {
        SetupDownload(DownloadExpeditionListResponse.Fail());

        var result = await _controller.Download("../evil");

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<DownloadExpeditionListResponse>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidBlobPath);
    }

    [Fact]
    public async Task Download_Success_ReturnsFileStream()
    {
        SetupDownload(new DownloadExpeditionListResponse
        {
            Success = true,
            Stream = new MemoryStream(new byte[] { 1, 2, 3 }),
            ContentType = "application/pdf",
            FileName = "list.pdf"
        });

        var result = await _controller.Download("2026-01-01/list.pdf");

        var file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().Be("list.pdf");
    }

    [Fact]
    public async Task Download_SuccessWithNullStream_ReturnsBadRequest()
    {
        SetupDownload(new DownloadExpeditionListResponse { Success = true, Stream = null });

        var result = await _controller.Download("2026-01-01/list.pdf");

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
