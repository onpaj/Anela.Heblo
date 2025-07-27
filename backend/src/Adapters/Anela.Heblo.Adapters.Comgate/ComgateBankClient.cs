using System.Globalization;
using Anela.Heblo.Adapters.Comgate.Model;
using Anela.Heblo.Bank;
using Anela.Heblo.Bank.Model;

namespace Anela.Heblo.Adapters.Comgate;

public class ComgateBankClient : IBankClient
{
    private readonly HttpClient _httpClient;
    private readonly ComgateSettings _settings;

    private static string GetStatementsUrlTemplate =
        "https://payments.comgate.cz/v1.0/transferList?merchant={0}&secret={1}&date={2}";
    private static string GetStatementUrlTemplate =
        "https://payments.comgate.cz/v1.0/aboSingleTransfer?merchant={0}&secret={1}&transferId={2}&download=true&type=v2";
    
    public ComgateBankClient(HttpClient httpClient, ComgateSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }
    public async Task<BankStatementData> GetStatementAsync(string transferId)
    {
        var client = new HttpClient();
        var url = string.Format(GetStatementUrlTemplate, _settings.MerchantId, _settings.Secret,transferId);
        var response = await client.GetStreamAsync(url);

        using var sr = new StreamReader(response);
        var data = await sr.ReadToEndAsync();
        var abo = AboFile.Parse(data);
        
        return new BankStatementData()
        {
            StatementId = transferId,
            Data = data,
            ItemCount = abo.Lines.Count,
        };
    }

    public async Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime requestStatementDate)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, string.Format(GetStatementsUrlTemplate, _settings.MerchantId, _settings.Secret, requestStatementDate.Date.ToString("yyyy-MM-dd")));

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsAsync<List<ComgateStatementHeader>>();

        return result.Where(w => w.AccountCounterParty == accountNumber)
            .Select(s => new BankStatementHeader()
            {
                StatementId = s.TransferId,
                Date = DateTime.ParseExact(s.TransferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Account = s.AccountCounterParty
            }).ToList();
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
        // Todo add some actual parsing
        var lines = data.Split(Environment.NewLine);

        return lines.Skip(1).Select(s => new AboLine(s)).ToList();
    }

    private static AboHeader GetHeader(string data)
    {
        // Todo add some actual parsing
        return new AboHeader();
    }
}

public class AboLine
{
    public string Raw { get; }

    public AboLine(string rawLine)
    {
        Raw = rawLine;
        // Todo add some actual parsing;
    }
}

public class AboHeader
{
}