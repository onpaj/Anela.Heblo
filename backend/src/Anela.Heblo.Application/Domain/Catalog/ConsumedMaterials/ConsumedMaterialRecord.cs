namespace Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;

public class ConsumedMaterialRecord
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public DateTime Date { get; set; }
}