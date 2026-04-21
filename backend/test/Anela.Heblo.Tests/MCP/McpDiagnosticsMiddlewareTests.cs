using Anela.Heblo.API.MCP;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpDiagnosticsMiddlewareTests
{
    private readonly Mock<ILogger<McpDiagnosticsMiddleware>> _loggerMock;

    public McpDiagnosticsMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<McpDiagnosticsMiddleware>>();
    }

    private McpDiagnosticsMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, _loggerMock.Object);

    [Fact]
    public async Task InvokeAsync_NonMcpPath_DoesNotLog()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/api/other", 404);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_McpGet200_DoesNotLog()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", 200);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_McpPost404_DoesNotLog()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var context = CreateContext("POST", "/mcp", 404);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_McpGet404WithSessionId_LogsWarningWithTruncatedId()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", 404, sessionId: "abc123def456");

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("session resumption failed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_McpGet404WithoutSessionId_LogsWarningWithPath()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", 404);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("GET returned 404")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_McpSubPath404_LogsWarning()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp/sse", 404);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("abc123def456", "abc123de***")]
    [InlineData("short", "***")]
    [InlineData("exactly8!", "exactly8***")]
    public void TruncateId_ReturnsExpectedResult(string input, string expected)
    {
        McpDiagnosticsMiddleware.TruncateId(input).Should().Be(expected);
    }

    private static DefaultHttpContext CreateContext(
        string method, string path, int statusCode, string? sessionId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (sessionId is not null)
            context.Request.QueryString = new QueryString($"?sessionId={sessionId}");
        context.Response.StatusCode = statusCode;
        return context;
    }
}
