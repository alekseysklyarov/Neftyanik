using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

public class MeterInputModel
{
    [Required(ErrorMessage = "Выберите участника.")]
    public int? MemberId { get; set; }

    [StringLength(100)]
    public string? MeterNumber { get; set; }

    [StringLength(200)]
    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    [Required(ErrorMessage = "Выберите расчётный участок.")]
    public int? BillingPlotId { get; set; }

    [Required(ErrorMessage = "Выберите хотя бы один участок.")]
    public List<int> PlotIds { get; set; } = [];
}
