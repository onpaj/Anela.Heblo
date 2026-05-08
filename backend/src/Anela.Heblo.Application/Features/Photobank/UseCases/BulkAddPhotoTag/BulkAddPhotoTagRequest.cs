using System.Collections.Generic;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag
{
    public class BulkAddPhotoTagRequest : IRequest<BulkAddPhotoTagResponse>
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public string TagName { get; set; } = null!;
    }
}
