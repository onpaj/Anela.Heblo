using System;
using System.Linq;
using Anela.Heblo.Application.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobNextRunCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsNull_WhenJobDisabled()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: false,
            timeZoneId: "UTC",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "Not/A/Real/Zone",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        // Warning logged with the unknown timezone and the job name
        VerifyWarningLogged(logger, "Not/A/Real/Zone", "test-job");
    }

    [Fact]
    public void Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "not a cron",
            isEnabled: true,
            timeZoneId: "UTC",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        // Warning logged with the invalid cron expression and the job name
        VerifyWarningLogged(logger, "not a cron", "test-job");
    }

    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone()
    {
        // Arrange
        var utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "UTC",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone()
    {
        // Arrange
        // 2026-01-01T04:59:00Z = 2026-01-01T05:59:00 local Europe/Prague time
        // (CET = UTC+1 in winter, no DST in effect on this date).
        var utcNow = new DateTime(2026, 1, 1, 4, 59, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour()
    {
        // Arrange
        // Europe/Prague DST ends 2026-10-25: local clocks fall back from 03:00 CEST to 02:00 CET,
        // so local 02:00-03:00 occurs twice that day. TimeZoneInfo.ConvertTimeToUtc resolves an
        // ambiguous unspecified-kind DateTime using the zone's standard (CET, UTC+1) offset --
        // verified directly against this repo's NCrontab.Advanced/TimeZoneInfo combination.
        var utcNow = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "30 2 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_Throws_AroundDstSpringForwardGap()
    {
        // Arrange
        // Europe/Prague DST starts 2026-03-29: local clocks jump forward from 02:00 CET to 03:00 CEST,
        // so local 02:00-03:00 never occurs that day. TimeZoneInfo.ConvertTimeToUtc throws
        // ArgumentException for any local time inside this gap. RecurringJobNextRunCalculator.Calculate
        // only catches TimeZoneNotFoundException and CrontabException, so this exception is NOT caught
        // and propagates to the caller. This test documents CURRENT, uncaught behavior -- it is not
        // an endorsement of it. Fixing it (e.g. catching ArgumentException and returning null like the
        // other error paths) is a legitimate follow-up but explicitly out of scope for this coverage-only
        // ticket -- see spec.r1.md Out of Scope. Consider filing a separate issue after this PR merges.
        var utcNow = new DateTime(2026, 3, 28, 20, 0, 0, DateTimeKind.Utc);

        // Act
        Action act = () => RecurringJobNextRunCalculator.Calculate(
            cronExpression: "30 2 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies exactly one Warning-level log call whose message contains every
    /// expected substring. Extracted to avoid duplicating the Mock&lt;ILogger&gt;
    /// verification lambda across tests that check a warning message's content.
    /// </summary>
    private static void VerifyWarningLogged(Mock<ILogger> logger, params string[] expectedSubstrings)
    {
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => expectedSubstrings.All(s => v.ToString()!.Contains(s))),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
