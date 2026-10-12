namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Whether a change on an ad account came through an executed Heblo proposal.</summary>
public enum AdChangeOrigin
{
    Heblo = 1,
    OutOfBand = 2,
}
