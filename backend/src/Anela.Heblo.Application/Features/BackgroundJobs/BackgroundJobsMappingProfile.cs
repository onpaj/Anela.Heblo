using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public class BackgroundJobsMappingProfile : Profile
{
    public BackgroundJobsMappingProfile()
    {
        CreateMap<RecurringJobConfiguration, RecurringJobDto>();
    }
}
