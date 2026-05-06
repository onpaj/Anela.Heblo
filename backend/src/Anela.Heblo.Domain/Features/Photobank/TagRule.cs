using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public class TagRule
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(1000)]
        public string PathPattern { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string TagName { get; set; } = null!;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
    }
}
