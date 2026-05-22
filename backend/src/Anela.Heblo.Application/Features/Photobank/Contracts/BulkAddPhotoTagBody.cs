namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class BulkAddPhotoTagBody
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public string TagName { get; set; } = null!;
    }
}
