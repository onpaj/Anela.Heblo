using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public class Tag
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!; // always lowercase-normalized

        public virtual ICollection<PhotoTag> PhotoTags { get; set; } = new List<PhotoTag>();
    }
}
