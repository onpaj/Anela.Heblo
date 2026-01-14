using System.Text.Json.Serialization;

namespace Anela.Heblo.Domain.Features.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductType
{
    Product = 8,
    Goods = 1,
    Material = 3,
    SemiProduct = 7,
    Set = 99,

    UNDEFINED = 0,
}