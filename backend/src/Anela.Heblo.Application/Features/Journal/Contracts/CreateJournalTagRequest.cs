using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class CreateJournalTagRequest : IRequest<CreateJournalTagResponse>
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = null!;

        [MaxLength(7)]
        public string Color { get; set; } = "#6B7280";
    }

    public class CreateJournalTagResponse : BaseResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;

        public CreateJournalTagResponse() : base() { }
        public CreateJournalTagResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
    }
}