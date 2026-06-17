namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class RetagPhotosBody
    {
        public int[] PhotoIds { get; set; } = Array.Empty<int>();
        public bool ClearExistingAiTags { get; set; }
    }
}
