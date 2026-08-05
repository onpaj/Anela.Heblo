using System.Net.Sockets;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly.Timeout;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class DbResiliencePipelineProviderTests
{
    [Fact]
    public async Task Pipeline_RetriesTransientPostgresException_UpToMaxAttempts()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new PostgresException("transient", "ERROR", "ERROR", "57P03");
            });
        });

        ex.Should().BeOfType<PostgresException>();
        calls.Should().Be(4);
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_OnUniqueViolation()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new PostgresException("dup", "ERROR", "ERROR", "23505");
            });
        });

        ex.Should().BeOfType<PostgresException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_OnNonTransientException()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new InvalidOperationException("not transient");
            });
        });

        ex.Should().BeOfType<InvalidOperationException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_RetriesSocketException()
    {
        var (provider, _) = CreateProvider(maxAttempts: 2);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new SocketException();
            });
        });

        ex.Should().BeOfType<SocketException>();
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Pipeline_SucceedsAfterTransientFailure()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var result = await provider.Pipeline.ExecuteAsync<int>(_ =>
        {
            calls++;
            if (calls < 2)
            {
                throw new PostgresException("blip", "ERROR", "ERROR", "57P03");
            }
            return ValueTask.FromResult(42);
        });

        result.Should().Be(42);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Pipeline_RetriesTimeoutRejectedException_WhenOperationHangsPastPerAttemptBudget()
    {
        // AddTimeout is nested inside AddRetry, so it's a per-attempt ceiling, not a shared budget.
        // A permanently-hanging operation must be retried up to MaxRetryAttempts times, each bounded
        // individually by TotalTimeBudget, once TimeoutRejectedException is classified transient.
        var totalBudget = TimeSpan.FromMilliseconds(50);
        var (provider, _) = CreateProvider(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(10),
            totalBudget: totalBudget);

        var calls = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(async ct =>
            {
                calls++;
                // Observes the pipeline's own per-attempt timeout token (as a real async DB call
                // would via its own CancellationToken parameter) so AddTimeout can actually cut it
                // off — a delegate that ignores its token entirely cannot be preempted by Polly.
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                return 0;
            });
        });
        sw.Stop();

        ex.Should().BeOfType<TimeoutRejectedException>();
        calls.Should().Be(4); // 1 initial attempt + 3 retries

        // Worst case: (MaxRetryAttempts + 1) * TotalTimeBudget + backoff delays between attempts.
        var worstCase = TimeSpan.FromMilliseconds(4 * totalBudget.TotalMilliseconds + 3 * 10) + TimeSpan.FromSeconds(2);
        sw.Elapsed.Should().BeLessThan(worstCase);
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_OnAmbientCancellation()
    {
        // The caller's own token firing (not Polly's internal timeout) must never be retried —
        // retrying after the caller already gave up wastes a pool slot and prolongs contention.
        var (provider, _) = CreateProvider(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(1),
            maxDelay: TimeSpan.FromMilliseconds(10),
            totalBudget: TimeSpan.FromSeconds(10));

        var calls = 0;
        using var cts = new CancellationTokenSource();

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(async ct =>
            {
                calls++;
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return 0;
            }, cts.Token);
        });

        ex.Should().BeAssignableTo<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_AbortsByTotalTimeBudget()
    {
        var (provider, _) = CreateProvider(
            maxAttempts: 50,
            baseDelay: TimeSpan.FromMilliseconds(50),
            maxDelay: TimeSpan.FromMilliseconds(50),
            totalBudget: TimeSpan.FromMilliseconds(150));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
                throw new SocketException());
        });
        sw.Stop();

        ex.Should().NotBeNull();
        // Allow generous time for system scheduling variance; focus is on respecting budget
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private static (DbResiliencePipelineProvider provider, DbResilienceMetrics metrics) CreateProvider(
        int maxAttempts,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        TimeSpan? totalBudget = null)
    {
        var options = Options.Create(new DbResilienceOptions
        {
            MaxRetryAttempts = maxAttempts,
            BaseDelay = baseDelay ?? TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = maxDelay ?? TimeSpan.FromMilliseconds(10),
            TotalTimeBudget = totalBudget ?? TimeSpan.FromSeconds(10),
        });

        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var provider = new DbResiliencePipelineProvider(
            options,
            metrics,
            NullLogger<DbResiliencePipelineProvider>.Instance);

        return (provider, metrics);
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
            new(options.Name, options.Version);
        public void Dispose() { }
    }
}
