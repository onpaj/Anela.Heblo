using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetManufactureLog;

public class GetManufactureLogHandler : IRequestHandler<GetManufactureLogRequest, GetManufactureLogResponse>
{
    private readonly IGiftPackageManufactureRepository _repository;
    private readonly IMapper _mapper;

    public GetManufactureLogHandler(
        IGiftPackageManufactureRepository repository,
        IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetManufactureLogResponse> Handle(GetManufactureLogRequest request, CancellationToken cancellationToken)
    {
        var logs = await _repository.GetRecentManufactureLogsAsync(request.Count, cancellationToken);
        var dtos = _mapper.Map<List<GiftPackageManufactureDto>>(logs);

        return new GetManufactureLogResponse
        {
            ManufactureLogs = dtos
        };
    }
}