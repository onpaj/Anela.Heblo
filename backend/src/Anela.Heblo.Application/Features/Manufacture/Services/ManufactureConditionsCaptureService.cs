using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureConditionsCaptureService : IManufactureConditionsCaptureService
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManufactureConditionsCaptureService> _logger;
    private readonly IConditionsReadingProvider _conditionsProvider;

    public ManufactureConditionsCaptureService(
        TimeProvider timeProvider,
        ILogger<ManufactureConditionsCaptureService> logger,
        IConditionsReadingProvider conditionsProvider)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _conditionsProvider = conditionsProvider;
    }

    public async Task<ManufactureOrderConditionsReading> CaptureAsync(
        ManufactureOrder order,
        ManufactureOrderState stage,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _conditionsProvider.GetCurrentSnapshotAsync(cancellationToken);
            return new ManufactureOrderConditionsReading
            {
                ManufactureOrderId = order.Id,
                Stage = stage,
                InnerTemperature = snapshot.InnerTemperature,
                InnerHumidity = snapshot.InnerHumidity,
                OuterTemperature = snapshot.OuterTemperature,
                OuterHumidity = snapshot.OuterHumidity,
                RecordedAt = snapshot.RecordedAt,
                Source = snapshot.Source,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture conditions reading for order {OrderId}, stage {Stage}", order.Id, stage);
            return new ManufactureOrderConditionsReading
            {
                ManufactureOrderId = order.Id,
                Stage = stage,
                RecordedAt = _timeProvider.GetUtcNow().DateTime,
                Source = ConditionsReadingSource.Unavailable,
            };
        }
    }
}
