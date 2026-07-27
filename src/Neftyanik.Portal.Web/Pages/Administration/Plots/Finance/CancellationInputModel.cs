using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class CancellationInputModel
{
    [StringLength(500, ErrorMessage = "Причина отмены не должна превышать 500 символов.")]
    [Display(Name = "Причина отмены")]
    public string? CancellationReason { get; set; }
}
