namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum ProductPairingMismatch
{
    None = 0,
    MissingInErp = 1,
    MissingInShoptet = 2,
    PairCodeUnresolved = 4
}
