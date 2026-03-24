using System.Diagnostics;
using System.Globalization;
using Anela.Heblo.Adapters.Comgate.Model;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Abo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    public ComgateBankClient(
        HttpClient httpClient,
        IOptions<ComgateSettings> options,
        ILogger<ComgateBankClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            var response = await _httpClient.GetStreamAsync(url);

            using var sr = new StreamReader(response);
            var data = await sr.ReadToEndAsync();

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
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                var response = await _httpClient.SendAsync(request);

                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Comgate API: HTTP request failed - StatusCode: {StatusCode}, Account: {AccountNumber}, Date: {Date}, Duration: {Duration}ms",
                        response.StatusCode, accountNumber, date.ToString("yyyy-MM-dd"), sw.ElapsedMilliseconds);
                }

                response.EnsureSuccessStatusCode();

                var dayResults = await response.Content.ReadAsAsync<List<ComgateStatementHeader>>();

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

    /// <summary>
    /// Anonymizes URL by replacing the secret parameter with asterisks
    /// </summary>
    private string AnonymizeUrl(string url)
    {
        if (string.IsNullOrEmpty(_settings.Secret))
            return url;

        return url.Replace(_settings.Secret, "***SECRET***");
    }
}