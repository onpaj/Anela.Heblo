namespace Anela.Heblo.Adapters.Comgate.Model;

internal class ComgateStatementHeader
{
    public required string TransferId { get; set; }
    public required string TransferDate { get; set; }

    public required string AccountCounterParty { get; set; }
    public required string AccountOutgoing { get; set; }
    public required string VariableSymbol { get; set; }
}