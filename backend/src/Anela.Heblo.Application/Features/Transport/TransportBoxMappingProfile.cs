using Anela.Heblo.Application.Features.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Transport;

public class TransportBoxMappingProfile : Profile
{
    public TransportBoxMappingProfile()
    {
        CreateMap<TransportBox, TransportBoxDto>()
            .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToString()))
            .ForMember(dest => dest.DefaultReceiveState, opt => opt.MapFrom(src => src.DefaultReceiveState.ToString()))
            .ForMember(dest => dest.ItemCount, opt => opt.MapFrom(src => src.Items.Count));

        CreateMap<TransportBoxItem, TransportBoxItemDto>();

        CreateMap<TransportBoxStateLog, TransportBoxStateLogDto>()
            .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToString()));
    }
}