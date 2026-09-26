using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ProcessDocsMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Anela_ProcessDocs, AccessLevel.Read);

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<ProcessDocsMcpTools>> _logger = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public ProcessDocsMcpToolsTests()
    {
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(true);
    }

    private ProcessDocsMcpTools CreateTools() => new(_mediator.Object, _logger.Object, _currentUserService.Object);

    [Fact]
    public async Task ListProcesses_PassesKindAndSerializesResult()
    {
        _mediator.Setup(m => m.Send(It.Is<ListProcessesRequest>(r => r.Kind == "sync"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListProcessesResponse { Processes = [new ProcessSummaryDto { Name = "sync-a" }] });

        var json = await CreateTools().ListProcesses("sync");

        var result = JsonSerializer.Deserialize<ListProcessesResponse>(json, McpJsonOptions.Default);
        Assert.Equal("sync-a", result!.Processes.Single().Name);
    }

    [Fact]
    public async Task GetProcessDoc_Found_ReturnsDoc()
    {
        _mediator.Setup(m => m.Send(It.Is<GetProcessDocRequest>(r => r.Name == "calc-margins"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetProcessDocResponse { Doc = new ProcessDocDto { Name = "calc-margins", Markdown = "# M" } });

        var json = await CreateTools().GetProcessDoc("calc-margins");

        var result = JsonSerializer.Deserialize<ProcessDocDto>(json, McpJsonOptions.Default);
        Assert.Equal("# M", result!.Markdown);
    }

    [Fact]
    public async Task GetProcessDoc_NotFound_ThrowsMcpExceptionWithSuggestions()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetProcessDocRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetProcessDocResponse { Suggestions = ["calc-margins"] });

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().GetProcessDoc("margins"));

        Assert.Contains("calc-margins", ex.Message);
        Assert.Contains("ListProcesses", ex.Message);
    }

    [Fact]
    public async Task ListProcesses_WithoutPermission_Throws()
    {
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().ListProcesses(null));

        Assert.Contains("FORBIDDEN", ex.Message);
        Assert.Contains(ReadRole, ex.Message);
        _mediator.Verify(m => m.Send(It.IsAny<ListProcessesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
