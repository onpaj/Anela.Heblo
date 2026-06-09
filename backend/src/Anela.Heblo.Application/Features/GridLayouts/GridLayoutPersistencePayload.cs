using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;

namespace Anela.Heblo.Application.Features.GridLayouts;

internal sealed record GridLayoutPersistencePayload(
    [property: JsonPropertyName("columns")] List<GridColumnStateDto> Columns);
