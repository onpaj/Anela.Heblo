using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetStockClientResilienceTests
{
    private const string CsvSample =
        "CODE;PAIR;NAME;IMG;IMG2;X;X;X;X;X;X;X;X;X;X;NS;X;X;X;X;X;X;X;X;X;STOCK;LOC;W;H;D;WD;AT\n" +
        "P001;;Product 1;;;;;;;;;;;;;;;;;;;;;;;5;A1;100;10;10;10;0\n";

    private static IServiceProvider BuildProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        Action<ShoptetStockClientOptions>? configureOptions = null,
        CapturingLoggerProvider? logCapture = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var services = new ServiceCollection();
        if (logCapture is not null)
        {
            var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logCapture));
            services.AddSingleton(loggerFactory);
        }
        else
        {
            services.AddLogging();
        }

        var configBuilder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShoptetApi:BaseUrl"] = "https://api.myshoptet.com/",
            ["ShoptetApi:ApiToken"] = "test-token",
            ["ShoptetApi:StockId"] = "1",
            ["StockClient:Url"] = "https://csv.example.com/export?token=secret-token-123",
            ["StockClient:TimeoutSeconds"] = "1",
            ["StockClient:MaxRetryAttempts"] = "3",
            ["StockClient:RetryBaseDelaySeconds"] = "0"
        });
        var configuration = configBuilder.Build();

        services.AddShoptetApiAdapter(configuration);
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.AddHttpClient("ShoptetStockCsv")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingStubHandler(handler));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage CsvOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(CsvSample, Encoding.GetEncoding("windows-1250"))
    };

    private static HttpResponseMessage TransientFailure() => new(HttpStatusCode.ServiceUnavailable)
    {
        Content = new StringContent("transient")
    };

    [Fact]
    public async Task ListAsync_RetriesOnTransient503_AndSucceedsOnThirdAttempt()
    {
        // Arrange
        var calls = 0;
        var provider = BuildProvider((req, ct) =>
        {
            calls++;
            return Task.FromResult(calls < 3 ? TransientFailure() : CsvOk());
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        var result = await client.ListAsync(CancellationToken.None);

        // Assert
        calls.Should().Be(3);
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("P001");
    }

    [Fact]
    public async Task ListAsync_ExhaustsRetries_AndThrowsHttpRequestException()
    {
        // Arrange
        var calls = 0;
        var provider = BuildProvider((req, ct) =>
        {
            calls++;
            return Task.FromResult(TransientFailure());
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        // 1 initial attempt + 3 retries = 4 total
        calls.Should().Be(4);
    }

    [Fact]
    public async Task ListAsync_WhenCallerCancels_DoesNotRetry()
    {
        // Arrange
        var calls = 0;
        using var cts = new CancellationTokenSource();
        var provider = BuildProvider(async (req, ct) =>
        {
            calls++;
            cts.Cancel(); // simulate the caller cancelling between transport attempts
            await Task.Delay(20, ct); // throws OperationCanceledException
            return CsvOk();
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        Func<Task> act = async () => await client.ListAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1, "caller cancellation must not trigger retries");
    }

    [Fact]
    public async Task ListAsync_AbortsRequest_WhenPerAttemptTimeoutExceeded()
    {
        // Arrange — per-attempt timeout = 1s (from BuildProvider config); handler sleeps 5s.
        var calls = 0;
        var provider = BuildProvider(async (req, ct) =>
        {
            calls++;
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return CsvOk();
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>(); // either HttpRequestException or TimeoutRejectedException
        stopwatch.Stop();
        // 1 initial attempt @ 1s + 3 retries @ 1s + backoff (0s) = ~4s — must complete in < 10s.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        calls.Should().BeGreaterThan(1, "timeouts should be classified transient and retried");
    }

    private sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public readonly List<string> Lines = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        {
            // Only capture logs from Anela.Heblo namespace (not System, Microsoft, or other framework logs)
            if (categoryName.StartsWith("Anela.Heblo", StringComparison.Ordinal))
            {
                return new CapturingLogger(Lines);
            }
            return new NullLogger();
        }
        public void Dispose() { }

        private sealed class NullLogger : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) { }
        }

        private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly List<string> _sink;
            public CapturingLogger(List<string> sink) => _sink = sink;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _sink.Add(formatter(state, exception));
            }
            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }

    [Fact]
    public async Task ListAsync_OnTerminalFailure_LogsRedactedUrl_AndStructuredFields()
    {
        // Arrange
        var capture = new CapturingLoggerProvider();
        var provider = BuildProvider(
            (req, ct) => Task.FromResult(TransientFailure()),
            logCapture: capture);
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        try { await client.ListAsync(CancellationToken.None); } catch (HttpRequestException) { /* expected */ }

        // Assert
        // Verify: token redaction works in logs
        var redactedLogs = capture.Lines.Where(l => l.Contains("token=***")).ToList();
        redactedLogs.Should().NotBeEmpty("should have at least one log with redacted token");

        // Critically: no log line should contain the raw secret token
        capture.Lines.Should().NotContain(l => l.Contains("secret-token-123"),
            "raw secret token must never appear in any log line");
    }

    private sealed class DelegatingStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public DelegatingStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    public class RedactTokenTests
    {
        [Theory]
        [InlineData("https://csv.example.com/export?token=abc",                    "https://csv.example.com/export?token=***")]
        [InlineData("https://csv.example.com/export?other=1&token=abc&x=y",        "https://csv.example.com/export?other=1&token=***&x=y")]
        [InlineData("https://csv.example.com/export?hash=zzz",                     "https://csv.example.com/export?hash=***")]
        [InlineData("https://csv.example.com/export?TOKEN=upper",                  "https://csv.example.com/export?TOKEN=***")]
        [InlineData("https://csv.example.com/export",                              "https://csv.example.com/export")]
        [InlineData("",                                                            "")]
        public void RedactToken_ReplacesSensitiveQueryValues(string input, string expected)
        {
            var actual = typeof(ShoptetStockClient)
                .GetMethod("RedactToken", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object[] { input }) as string;

            actual.Should().Be(expected);
        }
    }
}
