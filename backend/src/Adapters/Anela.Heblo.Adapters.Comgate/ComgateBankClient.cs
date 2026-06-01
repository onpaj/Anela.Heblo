using System.Diagnostics;
using System.Globalization;
using System.Net;
using Anela.Heblo.Adapters.Comgate.Model;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Abo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Comgate;

public class ComgateBankClient : IBankClient
{
    private readonly HttpClient _httpClient;
    private readonly ComgateSettings _settings;
    private readonly ILogger<ComgateBankClient> _logger;

    private static string GetStatementsUrlTemplate =
        "https://payments.comgate.cz/v1.0/transferList?merchant={0}&secret={1}&date={2}";
    private static string GetStatementUrlTemplate =
        "https://payments.comgate.cz/v1.0/aboSingleTransfer?merchant={0}&secret={1}&transferId={2}&download=true&type=v2";

    private readonly ResiliencePipeline _resiliencePipeline;

    internal static ResiliencePipeline BuildDefaultPipeline() => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>(ex => ex.StatusCode >= HttpStatusCode.InternalServerError),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>(ex => ex.StatusCode >= HttpStatusCode.InternalServerError),
            FailureRatio = 0.5,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1),
            BreakDuration = TimeSpan.FromSeconds(30),
        })
        .Build();

    public ComgateBankClient(
        HttpClient httpClient,
        IOptions<ComgateSettings> options,
        ILogger<ComgateBankClient> logger,
        ResiliencePipeline? resiliencePipeline = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = resiliencePipeline ?? BuildDefaultPipeline();
    }

    public BankClientProvider Provider => BankClientProvider.Comgate;

    public async Task<BankStatementData> GetStatementAsync(string transferId)
    {
        var url = string.Format(GetStatementUrlTemplate, _settings.MerchantId, _settings.Secret, transferId);
        var anonymizedUrl = AnonymizeUrl(url);

        _logger.LogInformation(
            "Comgate API: Fetching statement - TransferId: {TransferId}, URL: {Url}",
            transferId, anonymizedUrl);

        var sw = Stopwatch.StartNew();
        try
        {
            var data = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }, CancellationToken.None);

            sw.Stop();

            _logger.LogInformation(
                "Comgate API: Parsing ABO data - TransferId: {TransferId}, Size: {SizeKB}KB, Duration: {Duration}ms",
                transferId, data.Length / 1024.0, sw.ElapsedMilliseconds);

            var abo = AboFile.Parse(data);

            _logger.LogInformation(
                "Comgate API: Statement fetched successfully - TransferId: {TransferId}, Lines: {LineCount}, TotalDuration: {Duration}ms",
                transferId, abo.Lines.Count, sw.ElapsedMilliseconds);

            return new BankStatementData()
            {
                StatementId = transferId,
                Data = data,
                ItemCount = abo.Lines.Count,
            };
        }
        catch (BrokenCircuitException ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: Circuit breaker open - TransferId: {TransferId}, Duration: {Duration}ms",
                transferId, sw.ElapsedMilliseconds);
            throw new PaymentGatewayUnavailableException("Comgate payment gateway is temporarily unavailable (circuit breaker open).", null, ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode >= HttpStatusCode.InternalServerError)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: Server error after retries - TransferId: {TransferId}, StatusCode: {StatusCode}, Duration: {Duration}ms",
                transferId, (int?)ex.StatusCode, sw.ElapsedMilliseconds);
            throw new PaymentGatewayUnavailableException(
                $"Comgate payment gateway returned server error {(int?)ex.StatusCode}.", (int?)ex.StatusCode, ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: HTTP request failed - TransferId: {TransferId}, URL: {Url}, Duration: {Duration}ms, Error: {ErrorMessage}",
                transferId, anonymizedUrl, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: Failed to fetch or parse statement - TransferId: {TransferId}, Duration: {Duration}ms, Error: {ErrorMessage}",
                transferId, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    public async Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo)
    {
        var results = new List<BankStatementHeader>();

        for (var date = dateFrom.Date; date <= dateTo.Date; date = date.AddDays(1))
        {
            var url = string.Format(GetStatementsUrlTemplate, _settings.MerchantId, _settings.Secret, date.ToString("yyyy-MM-dd"));
            var anonymizedUrl = AnonymizeUrl(url);

            _logger.LogInformation(
                "Comgate API: Fetching statements list - Account: {AccountNumber}, Date: {StatementDate}, URL: {Url}",
                accountNumber, date.ToString("yyyy-MM-dd"), anonymizedUrl);

            var sw = Stopwatch.StartNew();
            try
            {
                var dayResults = await _resiliencePipeline.ExecuteAsync(async ct =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    var response = await _httpClient.SendAsync(request, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError(
                            "Comgate API: HTTP request failed - StatusCode: {StatusCode}, Account: {AccountNumber}, Date: {Date}, Duration: {Duration}ms",
                            response.StatusCode, accountNumber, date.ToString("yyyy-MM-dd"), sw.ElapsedMilliseconds);
                    }

                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsAsync<List<ComgateStatementHeader>>();
                }, CancellationToken.None);

                sw.Stop();

                var filtered = dayResults
                    .Where(w => w.AccountCounterParty == accountNumber)
                    .Select(s => new BankStatementHeader
                    {
                        StatementId = s.TransferId,
                        Date = DateTime.ParseExact(s.TransferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Account = s.AccountCounterParty
                    })
                    .ToList();

                _logger.LogInformation(
                    "Comgate API: Statements fetched for {Date} - Account: {AccountNumber}, Count: {Count}, Duration: {Duration}ms",
                    date.ToString("yyyy-MM-dd"), accountNumber, filtered.Count, sw.ElapsedMilliseconds);

                results.AddRange(filtered);
            }
            catch (BrokenCircuitException ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Comgate API: Circuit breaker open - Account: {AccountNumber}, Date: {Date}, Duration: {Duration}ms",
                    accountNumber, date.ToString("yyyy-MM-dd"), sw.ElapsedMilliseconds);
                throw new PaymentGatewayUnavailableException("Comgate payment gateway is temporarily unavailable (circuit breaker open).", null, ex);
            }
            catch (HttpRequestException ex) when (ex.StatusCode >= HttpStatusCode.InternalServerError)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Comgate API: Server error after retries - Account: {AccountNumber}, Date: {Date}, StatusCode: {StatusCode}, Duration: {Duration}ms",
                    accountNumber, date.ToString("yyyy-MM-dd"), (int?)ex.StatusCode, sw.ElapsedMilliseconds);
                throw new PaymentGatewayUnavailableException(
                    $"Comgate payment gateway returned server error {(int?)ex.StatusCode}.", (int?)ex.StatusCode, ex);
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Comgate API: HTTP request failed - Account: {AccountNumber}, Date: {Date}, URL: {Url}, Duration: {Duration}ms",
                    accountNumber, date.ToString("yyyy-MM-dd"), anonymizedUrl, sw.ElapsedMilliseconds);
                throw;
            }
        }

        return results;
    }

    private string AnonymizeUrl(string url)
    {
        if (string.IsNullOrEmpty(_settings.Secret))
            return url;

        return url.Replace(_settings.Secret, "***SECRET***");
    }
}