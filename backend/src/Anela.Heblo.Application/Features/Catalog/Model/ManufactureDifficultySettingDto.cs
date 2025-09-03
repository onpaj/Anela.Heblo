namespace Anela.Heblo.Application.Features.Catalog.Model;

public class ManufactureDifficultySettingDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public int DifficultyValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public bool IsCurrent { get; set; }
}