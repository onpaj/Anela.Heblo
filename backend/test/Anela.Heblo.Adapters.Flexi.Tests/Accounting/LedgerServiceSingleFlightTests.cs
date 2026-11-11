using Anela.Heblo.Adapters.Flexi.Accounting.Ledger;
using Anela.Heblo.Domain.Accounting.Ledger;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Accounting;

public class LedgerServiceSingleFlightTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ILedgerClient> _client = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly CapturingLogger _logger = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly MemoryCache _cache;
    private readonly FlexiLedgerOptions _options = new() { ClosedPeriodCacheMinutes = 360, OpenPeriodCacheMinutes = 15 };

    public LedgerServiceSingleFlightTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
#pragma warning disable CS0618 // ISystemClock is the only way to drive MemoryCache expiry from a fake clock
        _cache = new MemoryCache(new MemoryCacheOptions { Clock = new FakeClock(_time) });
#pragma warning restore CS0618
        _mapper
            .Setup(x => x.Map<List<LedgerItem>>(It.IsAny<object>()))
            .Returns(() => new List<LedgerItem> { new(), new(), new() });
    }

    private LedgerService CreateService() => new(
        _client.Object, _cache, _mapper.Object, _logger, Options.Create(_options), _time);

    private void SetupClient(Func<Task<IReadOnlyList<Rem.FlexiBeeSDK.Model.Accounting.Ledger.LedgerItemFlexiDto>>> factory) =>
        _client
            .Setup(x => x.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(factory);

    private static IReadOnlyList<Rem.FlexiBeeSDK.Model.Accounting.Ledger.LedgerItemFlexiDto> Empty() =>
        new List<Rem.FlexiBeeSDK.Model.Accounting.Ledger.LedgerItemFlexiDto>();

    private void VerifyClientCalls(Times times) =>
        _client.Verify(x => x.GetAsync(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task ParallelIdenticalCalls_HitClientOnce_AndShareResult()
    {
        var gate = new TaskCompletionSource();
        SetupClient(async () => { await gate.Task; return Empty(); });
        var service = CreateService();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))))
            .ToList();
        await Task.Delay(200);
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        VerifyClientCalls(Times.Once());
        results.Should().OnlyContain(r => ReferenceEquals(r, results[0]));
    }

    [Fact]
    public async Task FailedSharedRequest_IsNotCached_AndNextCallRetries()
    {
        var calls = 0;
        SetupClient(() => ++calls == 1
            ? throw new HttpRequestException("boom")
            : Task.FromResult(Empty()));
        var service = CreateService();

        var first = async () => await service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        await first.Should().ThrowAsync<HttpRequestException>();

        var result = await service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        result.Should().HaveCount(3);
        VerifyClientCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task CanceledSharedRequest_IsNotCached_AndNextCallRetries()
    {
        var calls = 0;
        SetupClient(() => ++calls == 1
            ? throw new TaskCanceledException("timeout")
            : Task.FromResult(Empty()));
        var service = CreateService();

        var first = async () => await service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        await first.Should().ThrowAsync<OperationCanceledException>();

        await service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        VerifyClientCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task CallerCancels_OtherWaiterStillGetsResult()
    {
        var gate = new TaskCompletionSource();
        SetupClient(async () => { await gate.Task; return Empty(); });
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var canceling = service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), cancellationToken: cts.Token);
        var other = service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        await Task.Delay(100);

        await cts.CancelAsync();
        var act = async () => await canceling;
        await act.Should().ThrowAsync<OperationCanceledException>();

        gate.SetResult();
        var result = await other;

        result.Should().HaveCount(3);
        VerifyClientCalls(Times.Once());
    }

    [Fact]
    public async Task ClosedPeriod_UsesClosedTtl_ExpiresAfterClosedMinutes()
    {
        SetupClient(() => Task.FromResult(Empty()));
        var service = CreateService();
        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 1, 31); // before 2026-05-01

        await service.GetLedgerItems(from, to);
        _time.Advance(TimeSpan.FromMinutes(359));
        await service.GetLedgerItems(from, to);
        VerifyClientCalls(Times.Once());

        _time.Advance(TimeSpan.FromMinutes(2));
        await service.GetLedgerItems(from, to);
        VerifyClientCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task OpenPeriod_UsesOpenTtl_ExpiresAfterOpenMinutes()
    {
        SetupClient(() => Task.FromResult(Empty()));
        var service = CreateService();
        var from = new DateTime(2026, 5, 1);
        var to = new DateTime(2026, 5, 15); // touches current month

        await service.GetLedgerItems(from, to);
        _time.Advance(TimeSpan.FromMinutes(14));
        await service.GetLedgerItems(from, to);
        VerifyClientCalls(Times.Once());

        _time.Advance(TimeSpan.FromMinutes(2));
        await service.GetLedgerItems(from, to);
        VerifyClientCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task Success_LogsQueryCompletedWithRowsAndSpan()
    {
        SetupClient(() => Task.FromResult(Empty()));
        var service = CreateService();

        await service.GetLedgerItems(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        _logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("FlexiLedger.QueryCompleted")
            && e.Message.Contains("rows=3")
            && e.Message.Contains("spanDays=30"));
    }

#pragma warning disable CS0618
    private sealed class FakeClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private readonly TimeProvider _provider;
        public FakeClock(TimeProvider provider) => _provider = provider;
        public DateTimeOffset UtcNow => _provider.GetUtcNow();
    }
#pragma warning restore CS0618

    private sealed class CapturingLogger : ILogger<LedgerService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
