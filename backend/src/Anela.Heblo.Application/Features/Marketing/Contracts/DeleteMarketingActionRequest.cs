using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class DeleteMarketingActionRequest : IRequest<DeleteMarketingActionResponse>
    {
        public int Id { get; set; }
    }

    public class DeleteMarketingActionResponse : BaseResponse
    {
        public int Id { get; set; }
        public string Message { get; set; } = "Marketing action deleted successfully";

        public DeleteMarketingActionResponse() : base() { }

        public DeleteMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }
}
