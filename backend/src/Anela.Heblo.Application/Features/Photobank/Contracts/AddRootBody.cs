namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRootBody
    {
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string DriveId { get; set; } = null!;
    }
}
