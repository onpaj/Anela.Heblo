using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusRequest : IRequest<UpdateManufactureOrderStatusResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    public ManufactureOrderState NewState { get; set; }

    public string? ChangeReason { get; set; }
    public string? Note { get; set; }
    public bool? ManualActionRequired { get; set; }
    public string? SemiProductOrderCode { get; set; }
    public string? ProductOrderCode { get; set; }
    public string? DiscardRedisueDocumentCode { get; set; }
    public bool? WeightWithinTolerance { get; set; }
    public decimal? WeightDifference { get; set; }
    public string? FlexiDocMaterialIssueForSemiProduct { get; set; }
    public string? FlexiDocSemiProductReceipt { get; set; }
    public string? FlexiDocSemiProductIssueForProduct { get; set; }
    public string? FlexiDocMaterialIssueForProduct { get; set; }
    public string? FlexiDocProductReceipt { get; set; }
}