using System.Collections.Generic;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds
{
    public class BulkAddPhotoTagByIdsRequest : IRequest<BulkAddPhotoTagByIdsResponse>
    {
        public List<int> PhotoIds { get; set; } = null!;
        public string TagName { get; set; } = null!;
    }
}
