using System.Collections.Generic;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos
{
    public class GetPhotosRequest : IRequest<GetPhotosResponse>
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public bool UseRegex { get; set; }
        public bool WithoutTags { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 48;
    }
}
