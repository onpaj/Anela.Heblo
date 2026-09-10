using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class ProductPriceFlexiDto
{
    [JsonProperty("idcenik")]
    public int ProductId { get; set; }

    [JsonProperty("kod")]
    public required string ProductCode { get; set; }

    [JsonProperty("cena")]
    public decimal Price { get; set; }

    [JsonProperty("cenanakup")]
    public decimal PurchasePrice { get; set; }

    [JsonProperty("typszbdphk")]
    public required string VatLevel { get; set; }

    [JsonProperty("typzasobyk")]
    public required string ProductType { get; set; }

    [JsonProperty("idKusovnik")]
    public int? BoMId { get; set; }

    /// <summary>
    /// Says which VAT semantics <see cref="Price"/> (<c>cenaZakl</c>) was entered under:
    /// "typCeny.bezDph" (excl-VAT) or "typCeny.sDph" (incl-VAT). User query 41 may not expose
    /// this field at all — it can come back null. Never assume it is excl-VAT without checking.
    /// </summary>
    [JsonProperty("typCenyDphK")]
    public string? TypCenyDphK { get; set; }

    /// <summary>True only when <see cref="TypCenyDphK"/> is explicitly "typCeny.sDph". A null
    /// value (the field is absent from query 41) is treated as excl-VAT by the caller, with a
    /// logged warning — never silently here.</summary>
    public bool IsPriceIncludingVat => TypCenyDphK == "typCeny.sDph";

    /// <summary>
    /// Standard Czech VAT rate, used as the READ-path fallback when <see cref="VatLevel"/>
    /// is not one this adapter recognises. Never used on the write path — see
    /// <see cref="VatRate"/>.
    /// </summary>
    private const decimal FallbackVatRate = 21m;

    /// <summary>
    /// Recognised <c>typszbdphk</c> values and the rate each stands for.
    ///
    /// BOTH vocabularies are listed on purpose. Flexi user query 41's definition is not
    /// visible from this repository, so nobody here can say whether it returns the enum
    /// vocabulary the rest of this adapter uses (<c>typSzbDph.dphZakl</c> and friends — see
    /// <c>Invoices/FlexiInvoiceMappingProfile</c>) or the Czech labels the original mapping
    /// was written against. The original mapping recognised only two Czech literals, one of
    /// them misspelled ("ovobozeno"), with an unconditional <c>_ =&gt; 21</c> fallback — so a
    /// genuinely 12% item was silently priced as if it were 21%. Recognising both
    /// vocabularies removes the guess in either direction.
    ///
    /// Do NOT "simplify" this back to a switch with a catch-all: anything not listed here is
    /// deliberately reported as unrecognised (<see cref="VatRate"/> is null), which makes
    /// <c>SetProductPriceHandler</c> refuse the live write rather than encode an assumption
    /// into a real ERP price. Note "snížená" is 12, not the pre-2024 15.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, decimal> VatRatesByLevel =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["typSzbDph.dphZakl"] = 21m,
            ["dphZakl"] = 21m,
            ["základní"] = 21m,
            ["zakladni"] = 21m,

            ["typSzbDph.dphSniz"] = 12m,
            ["dphSniz"] = 12m,
            ["snížená"] = 12m,
            ["snizena"] = 12m,

            ["typSzbDph.dphSniz2"] = 10m,
            ["dphSniz2"] = 10m,
            ["druhá snížená"] = 10m,
            ["druha snizena"] = 10m,

            ["typSzbDph.dphOsv"] = 0m,
            ["dphOsv"] = 0m,
            ["osvobozeno"] = 0m,
        };

    /// <summary>
    /// The VAT rate Flexi's own band positively identifies, or null when
    /// <see cref="VatLevel"/> is not a value this adapter recognises. Null is a real answer,
    /// not an error: the write path refuses on it (nothing is written anywhere) while the
    /// read path falls back to <see cref="Vat"/>.
    /// </summary>
    public decimal? VatRate =>
        VatLevel is not null && VatRatesByLevel.TryGetValue(VatLevel.Trim(), out var rate)
            ? rate
            : null;

    /// <summary>
    /// READ-path VAT rate: the recognised band, or 21 when the band is unrecognised. The
    /// fallback keeps the comparison screen behaving exactly as it did before the band
    /// mapping existed. Anything that WRITES must use <see cref="VatRate"/> instead.
    /// </summary>
    public decimal Vat => VatRate ?? FallbackVatRate;

    public bool HasCalculatedPurchasePrice => ProductType == "Výrobek";
    public bool HasBillOfMaterials => BoMId != null;
}