using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PriceConflictResolution
{
    /// <summary>Heblo's price wins; the next sync run overwrites the downstream edit.</summary>
    KeepHebloPrice = 1,

    /// <summary>The downstream edit wins and becomes Heblo's master value.</summary>
    AcceptRemotePrice = 2,
}
