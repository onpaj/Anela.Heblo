using Anela.Heblo.Application.Features.GridLayouts.Contracts;

namespace Anela.Heblo.Application.Features.GridLayouts.Infrastructure;

internal static class GridLayoutStoredMapper
{
    public static StoredGridLayout ToStored(IEnumerable<GridColumnStateDto> columns) =>
        new(columns.Select(c => new StoredColumnState(c.Id, c.Order, c.Width, c.Hidden)).ToList());

    public static List<GridColumnStateDto> ToDtoColumns(StoredGridLayout stored) =>
        stored.Columns.Select(c => new GridColumnStateDto
        {
            Id = c.Id,
            Order = c.Order,
            Width = c.Width,
            Hidden = c.Hidden
        }).ToList();
}
