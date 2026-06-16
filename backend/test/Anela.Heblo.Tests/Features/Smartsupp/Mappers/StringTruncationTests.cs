using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.Mappers;

public class StringTruncationTests
{
    private readonly Mock<ISmartsuppWebhookMetrics> _metrics = new();
    private readonly ILogger _logger = NullLogger.Instance;

    [Fact]
    public void Truncate_ReturnsNull_WhenInputIsNull()
    {
        var result = StringTruncation.Truncate(null, 10, "subject", "c1", _logger, _metrics.Object);
        result.Should().BeNull();
        _metrics.Verify(m => m.RecordTruncation(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Truncate_PassesThrough_WhenWithinLimit()
    {
        var result = StringTruncation.Truncate("hello", 10, "subject", "c1", _logger, _metrics.Object);
        result.Should().Be("hello");
        _metrics.Verify(m => m.RecordTruncation(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Truncate_PassesThrough_WhenExactlyAtLimit()
    {
        var input = new string('x', 10);
        var result = StringTruncation.Truncate(input, 10, "subject", "c1", _logger, _metrics.Object);
        result.Should().Be(input);
        _metrics.Verify(m => m.RecordTruncation(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Truncate_ShortensToLimit_WhenOverLimit()
    {
        var input = new string('x', 20);
        var result = StringTruncation.Truncate(input, 10, "subject", "c1", _logger, _metrics.Object);
        result.Should().HaveLength(10);
        _metrics.Verify(m => m.RecordTruncation("subject"), Times.Once);
    }

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePair_WhenBoundaryFallsMidPair()
    {
        // "😀" (U+1F600) encodes as two UTF-16 code units (high + low surrogate).
        // Build a string whose 10th char is the HIGH surrogate of an emoji.
        var prefix = new string('a', 9); // 9 chars
        var input = prefix + "😀" + new string('b', 5); // chars: 9 + 2 + 5 = 16
        var result = StringTruncation.Truncate(input, 10, "subject", "c1", _logger, _metrics.Object);

        // Length must be 9 (stepped back from a lone high surrogate), not 10.
        result!.Length.Should().Be(9);
        // The trailing char must NOT be a high surrogate.
        char.IsHighSurrogate(result[^1]).Should().BeFalse();
    }

    [Fact]
    public void Truncate_DoesNotLogTheValue_OnTruncation()
    {
        var input = new string('S', 20) + "SECRET_DATA";
        var logger = new TestLogger();
        StringTruncation.Truncate(input, 10, "subject", "c1", logger, _metrics.Object);

        logger.Logs.Should().NotBeEmpty();
        foreach (var line in logger.Logs)
            line.Should().NotContain("SECRET_DATA");
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Logs { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Logs.Add(formatter(state, exception));
        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
