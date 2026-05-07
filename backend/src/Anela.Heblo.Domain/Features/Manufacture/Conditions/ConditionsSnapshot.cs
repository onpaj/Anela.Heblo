namespace Anela.Heblo.Domain.Features.Manufacture.Conditions;

public sealed record ConditionsSnapshot(
    decimal? InnerTemperature,
    decimal? InnerHumidity,
    decimal? OuterTemperature,
    decimal? OuterHumidity,
    DateTime RecordedAt,
    ConditionsReadingSource Source);
