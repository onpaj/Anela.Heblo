namespace Anela.Heblo.Adapters.Flexi.Accounting.Ledger;

public class LedgerItemFlexiDto
{
    public DateTime? Datum { get; set; }
    public string? CisloDokladu { get; set; }
    public string? NazevFirmy { get; set; }
    public string? VariabilniSymbol { get; set; }
    public string? MaDatiUcet { get; set; }
    public string? MaDatiUcetNazev { get; set; }
    public string? DalUcet { get; set; }
    public string? DalUcetNazev { get; set; }
    public string? Stredisko { get; set; }
    public decimal? Castka { get; set; }
}