namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum ProductPairingMismatch
{
    None = 0,
    MissingInErp = 1,
    MissingInShoptet = 2,
    /// <summary>Legacy: no longer produced (the pair-code check was removed), kept so stored mismatch codes still decode.</summary>
    PairCodeUnresolved = 4
}
