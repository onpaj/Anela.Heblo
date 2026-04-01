using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankImportRequestDto
{
    [Required]
    public string AccountName { get; set; } = null!;

    [Required]
    public DateTime DateFrom { get; set; }

    [Required]
    public DateTime DateTo { get; set; }
}
