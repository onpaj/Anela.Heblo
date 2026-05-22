namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class BulkAddPhotoTagByIdsBody
    {
        public List<int> PhotoIds { get; set; } = [];
        public string TagName { get; set; } = null!;
    }
}
