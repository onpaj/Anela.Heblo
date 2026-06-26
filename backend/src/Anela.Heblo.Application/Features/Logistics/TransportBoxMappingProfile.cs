using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Logistics;

public class TransportBoxMappingProfile : Profile
{
    public TransportBoxMappingProfile()
    {
        CreateMap<TransportBox, TransportBoxDto>()
            .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToString()))
            .ForMember(dest => dest.DefaultReceiveState, opt => opt.MapFrom(src => src.DefaultReceiveState.ToString()))
            .ForMember(dest => dest.ItemCount, opt => opt.MapFrom(src => src.Items.Count))
            .ForMember(dest => dest.StateLog, opt => opt.MapFrom(src => src.StateLog.OrderByDescending(log => log.StateDate)))
            .ForMember(dest => dest.AllowedTransitions, opt => opt.MapFrom(src => src.TransitionNode.GetAllTransitions()))
            .ForMember(dest => dest.IsReceivable, opt => opt.Ignore());

        CreateMap<TransportBoxItem, TransportBoxItemDto>();

        CreateMap<TransportBoxStateLog, TransportBoxStateLogDto>()
            .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToString()));

        CreateMap<TransportBoxTransition, TransportBoxTransitionDto>()
            .ForMember(dest => dest.NewState, opt => opt.MapFrom(src => src.NewState.ToString()))
            .ForMember(dest => dest.TransitionType, opt => opt.MapFrom(src => src.TransitionType.ToString()))
            .ForMember(dest => dest.Label, opt => opt.MapFrom(src => GetStateLabel(src.NewState)));
    }

    private static string GetStateLabel(TransportBoxState state) =>
        state switch
        {
            TransportBoxState.New => "Nový",
            TransportBoxState.Opened => "Otevřený",
            TransportBoxState.InTransit => "V přepravě",
            TransportBoxState.Received => "Přijatý",
            TransportBoxState.Stocked => "Naskladněný",
            TransportBoxState.Reserve => "V rezervě",
            TransportBoxState.Quarantine => "V karanténě",
            TransportBoxState.Closed => "Uzavřený",
            TransportBoxState.Error => "Chyba",
            _ => state.ToString()
        };
}
