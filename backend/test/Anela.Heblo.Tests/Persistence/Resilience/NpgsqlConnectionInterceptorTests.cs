using System.Diagnostics.Metrics;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

/// <summary>
/// Covers the failure-path pool-exhaustion-wait recording added because a connection cancelled while
/// queued for a free pool slot (exactly the scenario behind the chronic Npgsql cancellation signal)
/// previously left no trace: only ConnectionOpened (the success path) fed the metric before this fix.
/// </summary>
public class NpgsqlConnectionInterceptorTests
{
    [Fact]
    public void ConnectionFailed_AfterConnectionOpening_RecordsPoolExhaustionWait_WhenElapsedExceedsThreshold()
    {
        var loggerMock = new Mock<ILogger<NpgsqlConnectionInterceptor>>();
        var measurements = new List<double>();
        using var listener = StartPoolExhaustionListener(measurements);
        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var interceptor = new NpgsqlConnectionInterceptor(metrics, loggerMock.Object);
        var connection = new NpgsqlConnection("Host=localhost;Database=test");
        var exception = new InvalidOperationException("connection cancelled while waiting for a pool slot");

        interceptor.ConnectionOpening(connection, MakeOpeningEventData(connection), default);
        Thread.Sleep(TimeSpan.FromMilliseconds(1100)); // exceed the interceptor's 1s pool-exhaustion threshold
        interceptor.ConnectionFailed(connection, MakeErrorEventData(connection, exception));

        measurements.Should().ContainSingle(m => m > 1.0);
        VerifyWarningLogged(loggerMock, exception, "wait_seconds");
    }

    [Fact]
    public void ConnectionFailed_WithoutPriorConnectionOpening_DoesNotRecordPoolExhaustionWait()
    {
        var loggerMock = new Mock<ILogger<NpgsqlConnectionInterceptor>>();
        var measurements = new List<double>();
        using var listener = StartPoolExhaustionListener(measurements);
        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var interceptor = new NpgsqlConnectionInterceptor(metrics, loggerMock.Object);
        var connection = new NpgsqlConnection("Host=localhost;Database=test");
        var exception = new InvalidOperationException("immediate failure, no open ever started");

        interceptor.ConnectionFailed(connection, MakeErrorEventData(connection, exception));

        measurements.Should().BeEmpty();
    }

    [Fact]
    public void ConnectionOpened_StillRecordsPoolExhaustionWait_AfterSharedHelperRefactor()
    {
        // Regression guard for extracting StopAndGetElapsedSeconds out of RecordOpenLatency —
        // the pre-existing success-path behavior must be unchanged.
        var loggerMock = new Mock<ILogger<NpgsqlConnectionInterceptor>>();
        var measurements = new List<double>();
        using var listener = StartPoolExhaustionListener(measurements);
        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var interceptor = new NpgsqlConnectionInterceptor(metrics, loggerMock.Object);
        var connection = new NpgsqlConnection("Host=localhost;Database=test");

        interceptor.ConnectionOpening(connection, MakeOpeningEventData(connection), default);
        Thread.Sleep(TimeSpan.FromMilliseconds(1100));
        interceptor.ConnectionOpened(connection, MakeEndEventData(connection));

        measurements.Should().ContainSingle(m => m > 1.0);
    }

    private static MeterListener StartPoolExhaustionListener(List<double> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DbResilienceMetrics.MeterName
                && instrument.Name == "npgsql.pool.exhaustion_wait_seconds")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();
        return listener;
    }

    private static ConnectionEventData MakeOpeningEventData(NpgsqlConnection connection) =>
        new(null!, null!, connection, null, Guid.NewGuid(), false, DateTimeOffset.UtcNow);

    private static ConnectionEndEventData MakeEndEventData(NpgsqlConnection connection) =>
        new(null!, null!, connection, null, Guid.NewGuid(), false, DateTimeOffset.UtcNow, TimeSpan.Zero);

    private static ConnectionErrorEventData MakeErrorEventData(NpgsqlConnection connection, Exception exception) =>
        new(null!, null!, connection, null, Guid.NewGuid(), exception, false, DateTimeOffset.UtcNow, TimeSpan.Zero);

    private static void VerifyWarningLogged(
        Mock<ILogger<NpgsqlConnectionInterceptor>> loggerMock, Exception expectedException, string expectedFragment)
    {
        loggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedFragment)),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
