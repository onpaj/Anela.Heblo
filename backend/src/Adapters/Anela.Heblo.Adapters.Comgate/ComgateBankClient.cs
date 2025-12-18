using System.Diagnostics;
using System.Globalization;
using Anela.Heblo.Adapters.Comgate.Model;
using Anela.Heblo.Domain.Features.Bank;
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

    public async Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime requestStatementDate)
    {
        var url = string.Format(GetStatementsUrlTemplate, _settings.MerchantId, _settings.Secret, requestStatementDate.Date.ToString("yyyy-MM-dd"));
        var anonymizedUrl = AnonymizeUrl(url);

        _logger.LogInformation(
            "Comgate API: Fetching statements list - Account: {AccountNumber}, Date: {StatementDate}, URL: {Url}",
            accountNumber, requestStatementDate.Date.ToString("yyyy-MM-dd"), anonymizedUrl);

        var sw = Stopwatch.StartNew();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var response = await _httpClient.SendAsync(request);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Comgate API: HTTP request failed - StatusCode: {StatusCode}, Account: {AccountNumber}, Duration: {Duration}ms",
                    response.StatusCode, accountNumber, sw.ElapsedMilliseconds);
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsAsync<List<ComgateStatementHeader>>();

            var filteredResults = result.Where(w => w.AccountCounterParty == accountNumber).ToList();

            _logger.LogInformation(
                "Comgate API: Statements list fetched successfully - Account: {AccountNumber}, Total: {TotalCount}, Filtered: {FilteredCount}, Duration: {Duration}ms",
                accountNumber, result.Count, filteredResults.Count, sw.ElapsedMilliseconds);

            return filteredResults
                .Select(s => new BankStatementHeader()
                {
                    StatementId = s.TransferId,
                    Date = DateTime.ParseExact(s.TransferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Account = s.AccountCounterParty
                }).ToList();
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: HTTP request failed - Account: {AccountNumber}, URL: {Url}, Duration: {Duration}ms, Error: {ErrorMessage}",
                accountNumber, anonymizedUrl, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: Failed to fetch statements list - Account: {AccountNumber}, Duration: {Duration}ms, Error: {ErrorMessage}",
                accountNumber, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
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


// https://github.com/jakubzapletal/bank-statements/blob/master/Parser/ABOParser.php
public class AboFile
{
    public AboHeader Header { get; set; }
    public List<AboLine> Lines { get; set; } = new List<AboLine>();

    public static AboFile Parse(string data)
    {
        var file = new AboFile()
        {
            Header = GetHeader(data),
            Lines = GetLines(data)
        };

        return file;
    }

    private static List<AboLine> GetLines(string data)
    {
        var lines = data.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        // Skip header line and process transaction lines
        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new AboLine(line))
            .ToList();
    }

    private static AboHeader GetHeader(string data)
    {
        var lines = data.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault() ?? string.Empty;
        
        return new AboHeader(firstLine);
    }
}

public class AboLine
{
    public string Raw { get; }

    public AboLine(string rawLine)
    {
        Raw = rawLine ?? string.Empty;
        // ABO format parsing can be implemented here if needed for detailed transaction analysis
        // For now, we just store the raw line as FlexiBee will parse it
    }
}

public class AboHeader
{
    public string Raw { get; }

    public AboHeader(string headerLine = "")
    {
        Raw = headerLine;
        // ABO header parsing can be implemented here if needed
        // For now, we just store the raw header as FlexiBee will parse it
    }
}