namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayout
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
