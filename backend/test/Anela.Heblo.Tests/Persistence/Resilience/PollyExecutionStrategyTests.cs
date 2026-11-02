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
