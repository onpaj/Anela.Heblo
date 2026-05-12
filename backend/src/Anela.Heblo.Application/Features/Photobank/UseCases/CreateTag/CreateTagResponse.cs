using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag
{
    public class CreateTagResponse : BaseResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public bool AlreadyExisted { get; set; }

        public CreateTagResponse() : base() { }

        public CreateTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
