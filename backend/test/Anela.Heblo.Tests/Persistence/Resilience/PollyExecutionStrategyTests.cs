using System.Diagnostics.Metrics;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Polly.Retry;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class PollyExecutionStrategyTests
{
    [Fact]
    public void RetriesOnFailure_IsTrue()
    {
        using var harness = CreateSut(maxAttempts: 3);

        harness.Sut.RetriesOnFailure.Should().BeTrue();
    }

    [Fact]
    public void Execute_SucceedsFirstAttempt_RecordsNoRetryMetrics()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var calls = 0;

        var result = harness.Sut.Execute<int, int>(0, (_, _) => { calls++; return 42; }, null);

        result.Should().Be(42);
        calls.Should().Be(1);
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.success");
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.failure");
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsFirstAttempt_RecordsNoRetryMetrics()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var calls = 0;

        var result = await harness.Sut.ExecuteAsync<int, int>(
            0, (_, _, _) => { calls++; return Task.FromResult(42); }, null, CancellationToken.None);

        result.Should().Be(42);
        calls.Should().Be(1);
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.success");
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.failure");
    }

    [Fact]
    public void Execute_SucceedsOnSecondAttempt_RecordsRetrySuccessWithAttempts2()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var calls = 0;

        var result = harness.Sut.Execute<int, int>(
            0,
            (_, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new TimeoutException();
                }

                return 7;
            },
            null);

        result.Should().Be(7);
        var success = harness.Capture.Measurements.Should().ContainSingle(m => m.Name == "db.retry.success").Subject;
        success.Value.Should().Be(1);
        success.Tags["total_attempts"].Should().Be(2);
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.failure");
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsOnSecondAttempt_RecordsRetrySuccessWithAttempts2()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var calls = 0;

        var result = await harness.Sut.ExecuteAsync<int, int>(
            0,
            (_, _, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new TimeoutException();
                }

                return Task.FromResult(7);
            },
            null,
            CancellationToken.None);

        result.Should().Be(7);
        var success = harness.Capture.Measurements.Should().ContainSingle(m => m.Name == "db.retry.success").Subject;
        success.Value.Should().Be(1);
        success.Tags["total_attempts"].Should().Be(2);
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.failure");
    }

    [Fact]
    public void Execute_FailsAfterRetries_SetsDataRecordsFailureLogsAndRethrowsSameInstance()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var thrown = new TimeoutException();

        var act = () => harness.Sut.Execute<int, int>(0, (_, _) => throw thrown, null);

        var ex = act.Should().Throw<TimeoutException>().Which;
        ex.Should().BeSameAs(thrown);
        AssertFailure(harness, ex, typeof(TimeoutException).FullName!, 3);
    }

    [Fact]
    public async Task ExecuteAsync_FailsAfterRetries_SetsDataRecordsFailureLogsAndRethrowsSameInstance()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var thrown = new TimeoutException();

        var act = () => harness.Sut.ExecuteAsync<int, int>(
            0, (_, _, _) => throw thrown, null, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<TimeoutException>()).Which;
        ex.Should().BeSameAs(thrown);
        AssertFailure(harness, ex, typeof(TimeoutException).FullName!, 3);
    }

    [Fact]
    public void Execute_NonRetryableFailureOnFirstAttempt_SetsAttempts1()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var thrown = new InvalidOperationException();

        var act = () => harness.Sut.Execute<int, int>(0, (_, _) => throw thrown, null);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Should().BeSameAs(thrown);
        AssertFailure(harness, ex, "System.InvalidOperationException", 1);
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableFailureOnFirstAttempt_SetsAttempts1()
    {
        using var harness = CreateSut(maxAttempts: 3);
        var thrown = new InvalidOperationException();

        var act = () => harness.Sut.ExecuteAsync<int, int>(
            0, (_, _, _) => throw thrown, null, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Should().BeSameAs(thrown);
        AssertFailure(harness, ex, "System.InvalidOperationException", 1);
    }

    private static void AssertFailure(Harness harness, Exception ex, string exceptionType, int attempts)
    {
        ex.Data["Anela.DbRetryAttempts"].Should().Be(attempts);
        harness.Capture.Measurements.Should().NotContain(m => m.Name == "db.retry.success");
        var failure = harness.Capture.Measurements.Should().ContainSingle(m => m.Name == "db.retry.failure").Subject;
        failure.Tags["exception.type"].Should().Be(exceptionType);
        failure.Tags["total_attempts"].Should().Be(attempts);
        harness.Logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static Harness CreateSut(int maxAttempts)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxAttempts - 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder().Handle<TimeoutException>(),
            })
            .Build();

        var meterFactory = new TestMeterFactory();
        var capture = new MetricCapture(meterFactory);
        var metrics = new DbResilienceMetrics(meterFactory);

        var context = new DbContext(
            new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var dependencies = new ExecutionStrategyDependencies(
            Mock.Of<ICurrentDbContext>(c => c.Context == context),
            ((IInfrastructure<IServiceProvider>)context).Instance.GetRequiredService<IDbContextOptions>(),
            Mock.Of<IDiagnosticsLogger<DbLoggerCategory.Infrastructure>>());

        var logger = new Mock<ILogger<PollyExecutionStrategy>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var sut = new PollyExecutionStrategy(
            dependencies,
            new StubPipelineProvider(pipeline),
            metrics,
            logger.Object);

        return new Harness(sut, capture, logger, context, metrics, meterFactory);
    }

    private sealed class Harness : IDisposable
    {
        private readonly DbContext _context;
        private readonly DbResilienceMetrics _metrics;
        private readonly TestMeterFactory _meterFactory;

        public Harness(
            PollyExecutionStrategy sut,
            MetricCapture capture,
            Mock<ILogger<PollyExecutionStrategy>> logger,
            DbContext context,
            DbResilienceMetrics metrics,
            TestMeterFactory meterFactory)
        {
            Sut = sut;
            Capture = capture;
            Logger = logger;
            _context = context;
            _metrics = metrics;
            _meterFactory = meterFactory;
        }

        public PollyExecutionStrategy Sut { get; }
        public MetricCapture Capture { get; }
        public Mock<ILogger<PollyExecutionStrategy>> Logger { get; }

        public void Dispose()
        {
            Capture.Dispose();
            _metrics.Dispose();
            _meterFactory.Dispose();
            _context.Dispose();
        }
    }

    private sealed class StubPipelineProvider : IDbResiliencePipelineProvider
    {
        public StubPipelineProvider(ResiliencePipeline pipeline) => Pipeline = pipeline;

        public ResiliencePipeline Pipeline { get; }
    }

    private sealed record CapturedMeasurement(string Name, long Value, Dictionary<string, object?> Tags);

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<CapturedMeasurement> _measurements = new();

        public MetricCapture(TestMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (meterFactory.Owns(instrument.Meter)
                    && (instrument.Name == "db.retry.success" || instrument.Name == "db.retry.failure"))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var dict = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    dict[tag.Key] = tag.Value;
                }

                lock (_measurements)
                {
                    _measurements.Add(new CapturedMeasurement(instrument.Name, value, dict));
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<CapturedMeasurement> Measurements
        {
            get
            {
                lock (_measurements)
                {
                    return _measurements.ToList();
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version);
            _meters.Add(meter);
            return meter;
        }

        public bool Owns(Meter meter) => _meters.Contains(meter);

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
