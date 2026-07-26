using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class MoveMarketingActionRequest : IRequest<MoveMarketingActionResponse>
    {
        public int Id { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }
    }

    public class MoveMarketingActionResponse : BaseResponse
    {
        public int Id { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string Message { get; set; } = "Marketing action moved successfully";

        public MoveMarketingActionResponse() : base() { }

        public MoveMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }
}
