using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog;

public class ManufactureDifficultySetting : Entity<int>
{
    public string ProductCode { get; set; } = null!;
    public int DifficultyValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}