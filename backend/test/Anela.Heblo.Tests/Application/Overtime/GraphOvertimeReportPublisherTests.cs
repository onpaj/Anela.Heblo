using System.Net;
using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Tests.Application.Overtime;

public class GraphOvertimeReportPublisherTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly Mock<ITokenAcquisition> _tokens = new();
    private HttpRequestMessage? _captured;

    private GraphOvertimeReportPublisher CreateSut(OvertimeOptions options)
    {
        _tokens.Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("token-123");
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(new HttpClient(_handler.Object));

        return new GraphOvertimeReportPublisher(
            factory.Object, _tokens.Object, Options.Create(options),
            NullLogger<GraphOvertimeReportPublisher>.Instance);
    }

    [Fact]
    public void IsConfigured_False_WhenDriveIdEmpty()
        => CreateSut(new OvertimeOptions()).IsConfigured.Should().BeFalse();

    [Fact]
    public async Task Publish_PutsToDrivePath_WithReplaceBehavior()
    {
        var sut = CreateSut(new OvertimeOptions
        {
            ExportDriveId = "drive-1", ExportFolderPath = "Provoz/Mzdy", ExportFileName = "Evidence-prescasu.xlsx"
        });

        await sut.PublishAsync(new byte[] { 1, 2, 3 }, "Evidence-prescasu.xlsx", CancellationToken.None);

        _captured.Should().NotBeNull();
        _captured!.Method.Should().Be(HttpMethod.Put);
        _captured.RequestUri!.ToString().Should().Be(
            "https://graph.microsoft.com/v1.0/drives/drive-1/root:/Provoz/Mzdy/Evidence-prescasu.xlsx:/content?@microsoft.graph.conflictBehavior=replace");
        _captured.Headers.Authorization!.Parameter.Should().Be("token-123");
    }

    [Fact]
    public async Task Publish_Throws_WhenNotConfigured()
    {
        var act = () => CreateSut(new OvertimeOptions()).PublishAsync(Array.Empty<byte>(), "x.xlsx", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
