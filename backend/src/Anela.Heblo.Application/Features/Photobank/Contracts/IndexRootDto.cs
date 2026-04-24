using System;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class IndexRootDto
    {
        public int Id { get; set; }
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string? DriveId { get; set; }
        public string? RootItemId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastIndexedAt { get; set; }
    }
}
