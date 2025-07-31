namespace Anela.Heblo.Adapters.Comgate.Model;

internal class ComgateStatementHeader
{
    public string TransferId { get; set; }
    public string TransferDate { get; set; }

    public string AccountCounterParty { get; set; }
    public string AccountOutgoing { get; set; }
    public string  VariableSymbol { get; set; }
}