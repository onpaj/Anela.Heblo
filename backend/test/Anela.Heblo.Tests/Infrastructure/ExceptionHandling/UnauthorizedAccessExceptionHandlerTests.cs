using System.Text.Json;
using Anela.Heblo.API.Infrastructure.ExceptionHandling;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.ExceptionHandling;

public class UnauthorizedAccessExceptionHandlerTests
{
    private static (UnauthorizedAccessExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) CreateSut()
    {
        var handler = new UnauthorizedAccessExceptionHandler(
            NullLogger<UnauthorizedAccessExceptionHandler>.Instance);
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (handler, context, body);
    }

    [Fact]
    public async Task TryHandleAsync_WhenUnauthorizedAccessException_Returns401WithProblemDetails()
    {
        var (handler, context, body) = CreateSut();
        var exception = new UnauthorizedAccessException("Authenticated user has no identifiable claim.");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Unauthorized");
        doc.RootElement.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7235#section-3.1");
    }

    [Fact]
    public async Task TryHandleAsync_WhenUnauthorizedAccessException_DoesNotLeakMessageInBody()
    {
        var (handler, context, body) = CreateSut();
        var secretMessage = "INTERNAL-CLAIM-DEBUG-INFO-MUST-NOT-LEAK";
        var exception = new UnauthorizedAccessException(secretMessage);

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        body.Position = 0;
        var json = await new StreamReader(body).ReadToEndAsync();
        json.Should().NotContain(secretMessage);
        json.Should().NotContain("detail");
    }

    [Fact]
    public async Task TryHandleAsync_WhenOtherException_ReturnsFalseAndDoesNotWriteBody()
    {
        var (handler, context, body) = CreateSut();
        var exception = new InvalidOperationException("unrelated");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeFalse();
        body.Length.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK); // unchanged
    }
}
