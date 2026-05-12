using Anela.Heblo.Domain.Features.Manufacture.Conditions;

namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderConditionsReading
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public ManufactureOrder ManufactureOrder { get; set; } = null!;
    public ManufactureOrderState Stage { get; set; }
    public decimal? InnerTemperature { get; set; }
    public decimal? InnerHumidity { get; set; }
    public decimal? OuterTemperature { get; set; }
    public decimal? OuterHumidity { get; set; }
    public DateTime RecordedAt { get; set; }
    public ConditionsReadingSource Source { get; set; }
}
