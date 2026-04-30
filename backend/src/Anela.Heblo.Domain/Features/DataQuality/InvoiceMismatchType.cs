namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum InvoiceMismatchType
{
    None = 0,
    MissingInFlexi = 1,
    MissingInShoptet = 2,
    TotalWithVatDiffers = 4,
    TotalWithoutVatDiffers = 8,
    ItemsDiffer = 16
}
