using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Domain.Entities;

public class Plot
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Number { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Address { get; set; }

    public decimal? AreaSquareMeters { get; set; }

    [StringLength(100)]
    public string? CadastralNumber { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public List<PlotOwnership> PlotOwnerships { get; set; } = [];

    public List<PlotOwnershipHistory> OwnershipHistory { get; set; } = [];

    public List<MemberElectricityMeterPlot> MemberElectricityMeterPlots { get; set; } = [];

    public List<MemberElectricityMeter> BillingMemberElectricityMeters { get; set; } = [];

    public List<Charge> Charges { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];
}
