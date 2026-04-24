using System;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public class PhotoTag
    {
        public int PhotoId { get; set; }
        public int TagId { get; set; }

        public PhotoTagSource Source { get; set; }

        public DateTime CreatedAt { get; set; }

        public virtual Photo Photo { get; set; } = null!;
        public virtual Tag Tag { get; set; } = null!;
    }
}
