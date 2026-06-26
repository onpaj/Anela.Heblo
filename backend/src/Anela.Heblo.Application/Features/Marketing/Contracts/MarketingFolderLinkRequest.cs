using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class MarketingFolderLinkRequest
    {
        [Required]
        [MaxLength(100)]
        public string FolderKey { get; set; } = null!;

        [Required]
        public MarketingFolderType FolderType { get; set; }
    }
}
