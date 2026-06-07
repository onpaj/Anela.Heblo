using System.Globalization;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryHandler
    : IRequestHandler<GetConsumptionHistoryRequest, GetConsumptionHistoryResponse>
{
    private const int MaxPageSize = 100;

    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetConsumptionHistoryHandler> _logger;

    public GetConsumptionHistoryHandler(
        IPackingMaterialRepository repository,
        ILogger<GetConsumptionHistoryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetConsumptionHistoryResponse> Handle(
        GetConsumptionHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var skip = (pageNumber - 1) * pageSize;

        var filter = new MaterialConsumptionHistoryFilter(
            DateFrom: ParseDateOrNull(request.DateFrom),
            DateTo: ParseDateOrNull(request.DateTo),
            PackingMaterialId: request.PackingMaterialId,
            ConsumptionType: request.ConsumptionType,
            ProductCode: NormalizeNullableString(request.ProductCode),
            InvoiceId: NormalizeNullableString(request.InvoiceId));

        _logger.LogInformation(
            "Loading consumption history page {PageNumber} (size {PageSize})", pageNumber, pageSize);

        var (records, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip, pageSize, ascending: !request.SortDescending, cancellationToken);

        var materialNames = (await _repository.GetAllWithAllocationsAsync(cancellationToken))
            .ToDictionary(m => m.Id, m => m.Name);

        var items = records.Select(r => MapToDto(r, materialNames)).ToList();

        return new GetConsumptionHistoryResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
        };
    }

    private static MaterialConsumptionHistoryItemDto MapToDto(
        MaterialConsumptionHistoryRecord record,
        IReadOnlyDictionary<int, string> materialNames)
        => new()
        {
            RecordType = record.RecordType,
            RecordTypeText = record.RecordType == HistoryRecordType.Consumption ? "Spotřeba" : "Změna množství",
            PackingMaterialId = record.PackingMaterialId,
            MaterialName = materialNames.TryGetValue(record.PackingMaterialId, out var name) ? name : "Neznámý",
            Date = record.Date,
            CreatedAt = record.CreatedAt,
            ConsumptionType = record.ConsumptionType,
            ConsumptionTypeText = record.ConsumptionType?.ToString(),
            InvoiceId = record.InvoiceId,
            ProductCode = record.ProductCode,
            ProductQuantity = record.ProductQuantity,
            Amount = record.Amount,
            OldQuantity = record.OldQuantity,
            NewQuantity = record.NewQuantity,
            ChangeAmount = record.ChangeAmount,
            LogType = record.LogType,
            LogTypeText = record.LogType?.ToString(),
            UserId = record.UserId,
        };

    private static DateOnly? ParseDateOrNull(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string? NormalizeNullableString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
