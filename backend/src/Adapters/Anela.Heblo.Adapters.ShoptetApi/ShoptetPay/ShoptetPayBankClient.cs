using System.Net.Http.Json;
using Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Abo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;

public class ShoptetPayBankClient : IBankClient
{
    private readonly HttpClient _httpClient;
    private readonly ShoptetPaySettings _settings;
    private readonly ILogger<ShoptetPayBankClient> _logger;

    public BankClientProvider Provider => BankClientProvider.ShoptetPay;

    public ShoptetPayBankClient(
        HttpClient httpClient,
        IOptions<ShoptetPaySettings> options,
        ILogger<ShoptetPayBankClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo)
    {
        // Payout reports are periodic settlements (not daily) — expand to cover the full month range
        // so reports with any overlap in the requested period are returned
        var rangeFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var rangeTo = new DateTime(dateTo.Year, dateTo.Month, DateTime.DaysInMonth(dateTo.Year, dateTo.Month));

        var url = $"/v1/reports/payout?dateFrom={rangeFrom:yyyy-MM-ddTHH:mm:ssZ}&dateTo={rangeTo:yyyy-MM-ddTHH:mm:ssZ}&limit=1000";

        _logger.LogInformation(
            "ShoptetPay API: Fetching payout reports - DateFrom: {DateFrom}, DateTo: {DateTo} (expanded to full month range)",
            rangeFrom.ToString("yyyy-MM-dd"), rangeTo.ToString("yyyy-MM-dd"));

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var rawBody = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("ShoptetPay API: Raw response body: {Body}", rawBody);

        var result = System.Text.Json.JsonSerializer.Deserialize<PayoutReportListResponse>(rawBody,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new PayoutReportListResponse();

        _logger.LogInformation(
            "ShoptetPay API: Received {Count} payout reports (total: {Total})",
            result.Data.Count, result.Total);

        return result.Data.Select(r => new BankStatementHeader
        {
            StatementId = r.Id,
            Date = r.DateTo,
            Account = r.Currency
        }).ToList();
    }

    public async Task<BankStatementData> GetStatementAsync(string statementId)
    {
        var url = $"/v1/reports/payout/{statementId}/abo";

        _logger.LogInformation("ShoptetPay API: Downloading ABO report - StatementId: {StatementId}", statementId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadAsStringAsync();
        var abo = AboFile.Parse(data);

        _logger.LogInformation(
            "ShoptetPay API: ABO report downloaded - StatementId: {StatementId}, Lines: {LineCount}",
            statementId, abo.Lines.Count);

        return new BankStatementData
        {
            StatementId = statementId,
            Data = data,
            ItemCount = abo.Lines.Count
        };
    }
}
