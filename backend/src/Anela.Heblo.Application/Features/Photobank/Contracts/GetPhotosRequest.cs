using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class GetPhotosRequest : IRequest<GetPhotosResponse>
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 48;
    }

    public class GetPhotosResponse : BaseResponse
    {
        public List<PhotoDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
