using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class UpdateMarketingActionRequest : IRequest<UpdateMarketingActionResponse>
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = null!;

        [MaxLength(5000)]
        public string? Description { get; set; }

        [Required]
        public MarketingActionType ActionType { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public List<string>? AssociatedProducts { get; set; }
        public List<CreateMarketingActionRequest.CreateFolderLinkRequest>? FolderLinks { get; set; }
    }

    public class UpdateMarketingActionResponse : BaseResponse
    {
        public int Id { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string Message { get; set; } = "Marketing action updated successfully";

        public UpdateMarketingActionResponse() : base() { }

        public UpdateMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }
}
